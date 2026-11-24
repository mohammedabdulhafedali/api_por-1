using System.Security.Claims;
using ApiGovernance.Modules.VersionsFiles.Configuration;
using ApiGovernance.Modules.VersionsFiles.Contracts;
using ApiGovernance.Modules.VersionsFiles.Scanning;
using ApiGovernance.Modules.VersionsFiles.Storage;
using ApiGovernance.Modules.VersionsFiles.Validation;
using ApiGovernance.Shared.Contracts;
using ApiGovernance.Shared.Persistence;
using ApiGovernance.Shared.Persistence.Entities;
using ApiGovernance.Shared.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ApiGovernance.Modules.VersionsFiles.Services;

public interface IVersionsFilesService
{
    Task<IntegrationVersionsResponse> GetIntegrationVersionsAsync(
        ClaimsPrincipal actorPrincipal,
        Guid integrationId,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<ApiVersionDto> CreateApiVersionAsync(
        ClaimsPrincipal actorPrincipal,
        Guid integrationId,
        CreateApiVersionRequest request,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<IntegrationFilesWorkspaceResponse> GetFilesWorkspaceAsync(
        ClaimsPrincipal actorPrincipal,
        Guid integrationId,
        Guid? versionId,
        Guid? revisionId,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<DocumentRevisionDto> CreateInitialDraftRevisionAsync(
        ClaimsPrincipal actorPrincipal,
        Guid versionId,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RevisionFileDto>> GetRevisionFilesAsync(
        ClaimsPrincipal actorPrincipal,
        Guid revisionId,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<(int StatusCode, RevisionFileDto File)> UploadRevisionFileAsync(
        ClaimsPrincipal actorPrincipal,
        Guid revisionId,
        Guid fileTypeId,
        IFormFile uploadedFile,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<(int StatusCode, RevisionFileDto File)> RescanRevisionFileAsync(
        ClaimsPrincipal actorPrincipal,
        Guid revisionId,
        Guid revisionFileId,
        string traceId,
        CancellationToken cancellationToken = default);

    Task DeleteDraftRevisionFileAsync(
        ClaimsPrincipal actorPrincipal,
        Guid revisionId,
        Guid revisionFileId,
        CancellationToken cancellationToken = default);

    Task<FileScanStatusDto> GetFileScanStatusAsync(
        ClaimsPrincipal actorPrincipal,
        Guid revisionFileId,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<(Stream ContentStream, string FileName, string ContentType, long SizeBytes, string Sha256)> DownloadCleanRevisionFileAsync(
        ClaimsPrincipal actorPrincipal,
        Guid revisionId,
        Guid revisionFileId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Implements Sprint S08 Tasks V01, V02, V03, and V04:
/// - V01: Private non-web immutable object storage with SHA-256 verification.
/// - V02: Strict extension, quota, magic-byte, and archive safety checks + quarantine isolation.
/// - V03: Fail-closed AV scanning (PENDING_SCAN when engine absent; INFECTED rejected/quarantined).
/// - V04: Manual free-text API version labels cleanly decoupled from document revisions.
/// </summary>
public sealed class VersionsFilesService(
    ApiGovernanceDbContext dbContext,
    IServerAuthorizationService serverAuthorization,
    IDocumentAuthorizationService documentAuthorization,
    IPrivateFileStore privateFileStore,
    IFileSecurityScanner fileScanner,
    FileUploadContentValidator contentValidator,
    IOptions<FileStorageOptions> storageOptions) : IVersionsFilesService
{
    private readonly FileStorageOptions _storageOptions = storageOptions.Value;

    public async Task<IntegrationVersionsResponse> GetIntegrationVersionsAsync(
        ClaimsPrincipal actorPrincipal,
        Guid integrationId,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var (access, integration) = await documentAuthorization.RequireIntegrationDocumentPermissionAsync(
            actorPrincipal,
            integrationId,
            DocumentPermissionCatalog.View,
            "API_VERSIONS_READ_DENIED",
            cancellationToken);

        var (derivedTitle, providerName, consumerName, apiGroupName) =
            await LoadDocumentPartyLabelsAsync(access.Document, cancellationToken);

        var isAssignedDev = await documentAuthorization.IsUserAssignedDeveloperAsync(
            access.Actor.Id, integration.DocumentId, integration.Id, cancellationToken);

        var versions = await LoadApiVersionDtosAsync(integration.Id, traceId, cancellationToken);

        var hasGeneralVersionCreate = await serverAuthorization.HasGeneralPermissionAsync(
            actorPrincipal, "versions.create", cancellationToken);
        var hasDocVersionManage = access.EffectivePermissions.TryGetValue(DocumentPermissionCatalog.VersionManage, out var vm) && vm.Allowed;

        return new IntegrationVersionsResponse(
            integration.Id,
            integration.DocumentId,
            integration.ReferenceCode,
            derivedTitle,
            providerName,
            consumerName,
            apiGroupName,
            CanCreateVersion: hasGeneralVersionCreate && hasDocVersionManage && isAssignedDev && access.Document.State != "ARCHIVED",
            IsCurrentUserAssignedDeveloper: isAssignedDev,
            Versions: versions,
            TraceId: traceId);
    }

    public async Task<ApiVersionDto> CreateApiVersionAsync(
        ClaimsPrincipal actorPrincipal,
        Guid integrationId,
        CreateApiVersionRequest request,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var (access, integration) = await documentAuthorization.RequireIntegrationDocumentPermissionAsync(
            actorPrincipal,
            integrationId,
            DocumentPermissionCatalog.VersionManage,
            "API_VERSION_CREATE_DENIED",
            cancellationToken);

        await RequireAssignedDeveloperAsync(
            access.Actor.Id,
            integration,
            "API_VERSION_CREATE_DENIED",
            cancellationToken);

        if (string.Equals(access.Document.State, "ARCHIVED", StringComparison.OrdinalIgnoreCase))
        {
            throw ApiContractException.UnprocessableEntity(
                messageAr: "لا يمكن إنشاء إصدار API جديد لوثيقة مؤرشفة.",
                messageEn: "Cannot create an API version for an archived API document.");
        }

        // Free-text version label (zero SemVer enforcement per PRD-04 & V04)
        var label = request.VersionLabel.Trim();
        var exists = await dbContext.ApiVersions.AsNoTracking()
            .AnyAsync(v => v.IntegrationId == integration.Id && v.VersionLabel.ToLower() == label.ToLower(), cancellationToken);

        if (exists)
        {
            throw ApiContractException.Conflict(
                messageAr: $"يوجد إصدار مسجل مسبقًا بنفس التسمية ({label}) لهذا التكامل.",
                messageEn: $"An API version with label '{label}' already exists for this integration.");
        }

        var entity = new ApiVersionEntity
        {
            Id = Guid.NewGuid(),
            IntegrationId = integration.Id,
            VersionLabel = label,
            IsArchived = false,
            CreatedAt = DateTimeOffset.UtcNow
        };

        dbContext.ApiVersions.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        await serverAuthorization.WriteAuditEventAsync(
            access.Actor.Id,
            "API_VERSION_CREATED",
            "API_VERSION",
            entity.Id,
            "SUCCESS",
            new { integrationId = integration.Id, integration.DocumentId, entity.VersionLabel },
            cancellationToken);

        return new ApiVersionDto(
            entity.Id,
            entity.IntegrationId,
            entity.VersionLabel,
            entity.IsArchived,
            entity.CreatedAt,
            RevisionsCount: 0,
            LatestRevisionStatus: null,
            TraceId: traceId);
    }

    public async Task<IntegrationFilesWorkspaceResponse> GetFilesWorkspaceAsync(
        ClaimsPrincipal actorPrincipal,
        Guid integrationId,
        Guid? versionId,
        Guid? revisionId,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var (access, integration) = await documentAuthorization.RequireIntegrationDocumentPermissionAsync(
            actorPrincipal,
            integrationId,
            DocumentPermissionCatalog.View,
            "FILES_WORKSPACE_READ_DENIED",
            cancellationToken);

        var (derivedTitle, _, _, _) = await LoadDocumentPartyLabelsAsync(access.Document, cancellationToken);
        var isAssignedDev = await documentAuthorization.IsUserAssignedDeveloperAsync(
            access.Actor.Id, integration.DocumentId, integration.Id, cancellationToken);

        var versions = await LoadApiVersionDtosAsync(integration.Id, traceId, cancellationToken);

        Guid? effectiveVersionId = versionId;
        IReadOnlyList<DocumentRevisionDto> revisions = [];
        DocumentRevisionDto? selectedRevision = null;
        IReadOnlyList<RevisionFileDto> files = [];

        if (revisionId.HasValue)
        {
            var revEntity = await dbContext.DocumentRevisions.AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == revisionId.Value, cancellationToken);
            if (revEntity is not null && versions.Any(v => v.Id == revEntity.ApiVersionId))
            {
                effectiveVersionId = revEntity.ApiVersionId;
            }
        }

        effectiveVersionId ??= versions.FirstOrDefault()?.Id;

        if (effectiveVersionId.HasValue && versions.Any(v => v.Id == effectiveVersionId.Value))
        {
            revisions = await LoadRevisionDtosAsync(effectiveVersionId.Value, access.Document.RequirementTemplateId, traceId, cancellationToken);
            selectedRevision = revisionId.HasValue
                ? revisions.FirstOrDefault(r => r.Id == revisionId.Value) ?? revisions.FirstOrDefault()
                : revisions.FirstOrDefault();

            if (selectedRevision is not null)
            {
                files = await LoadRevisionFileDtosAsync(selectedRevision.Id, traceId, cancellationToken);
            }
        }

        var hasFilesUpload = await serverAuthorization.HasGeneralPermissionAsync(actorPrincipal, "files.upload", cancellationToken);
        var hasFilesDelete = await serverAuthorization.HasGeneralPermissionAsync(actorPrincipal, "files.delete", cancellationToken);
        var hasFilesDownload = await serverAuthorization.HasGeneralPermissionAsync(actorPrincipal, "files.download", cancellationToken);
        var hasRevisionsCreate = await serverAuthorization.HasGeneralPermissionAsync(actorPrincipal, "revisions.create", cancellationToken);

        var hasDocFilesManage = access.EffectivePermissions.TryGetValue(DocumentPermissionCatalog.FilesManage, out var fm) && fm.Allowed;
        var hasDocEdit = access.EffectivePermissions.TryGetValue(DocumentPermissionCatalog.Edit, out var de) && de.Allowed;
        var scannerInfo = fileScanner.GetRuntimeInfo();

        return new IntegrationFilesWorkspaceResponse(
            integration.Id,
            integration.DocumentId,
            integration.ReferenceCode,
            derivedTitle,
            CanCreateDraftRevision: hasRevisionsCreate && hasDocEdit && isAssignedDev && access.Document.State != "ARCHIVED",
            CanUploadFiles: hasFilesUpload && hasDocFilesManage && isAssignedDev && access.Document.State != "ARCHIVED",
            CanDeleteFiles: hasFilesDelete && hasDocFilesManage && isAssignedDev && access.Document.State != "ARCHIVED",
            CanDownloadFiles: hasFilesDownload,
            IsCurrentUserAssignedDeveloper: isAssignedDev,
            ScannerConfigured: scannerInfo.IsConfigured,
            ScannerSummaryAr: scannerInfo.SummaryAr,
            ScannerSummaryEn: scannerInfo.SummaryEn,
            Versions: versions,
            SelectedVersionId: effectiveVersionId,
            Revisions: revisions,
            SelectedRevision: selectedRevision,
            Files: files,
            TraceId: traceId);
    }

    public async Task<DocumentRevisionDto> CreateInitialDraftRevisionAsync(
        ClaimsPrincipal actorPrincipal,
        Guid versionId,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var version = await dbContext.ApiVersions.AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == versionId, cancellationToken);

        if (version is null)
        {
            throw ApiContractException.NotFound(
                messageAr: "إصدار API المطلوب غير موجود أو غير متاح.",
                messageEn: "API version was not found or access is restricted.");
        }

        var (access, integration) = await documentAuthorization.RequireIntegrationDocumentPermissionAsync(
            actorPrincipal,
            version.IntegrationId,
            DocumentPermissionCatalog.Edit,
            "REVISION_CREATE_DENIED",
            cancellationToken);

        await RequireAssignedDeveloperAsync(
            access.Actor.Id,
            integration,
            "REVISION_CREATE_DENIED",
            cancellationToken);

        if (string.Equals(access.Document.State, "ARCHIVED", StringComparison.OrdinalIgnoreCase))
        {
            throw ApiContractException.UnprocessableEntity(
                messageAr: "لا يمكن إنشاء مسودة مراجعة توثيق لوثيقة مؤرشفة.",
                messageEn: "Cannot create a documentation revision for an archived API document.");
        }

        var hasActiveDraft = await dbContext.DocumentRevisions.AsNoTracking()
            .AnyAsync(r => r.ApiVersionId == version.Id && r.Status == "DRAFT", cancellationToken);
        if (hasActiveDraft)
        {
            throw ApiContractException.Conflict(
                messageAr: "توجد مسودة مراجعة نشطة بالفعل لهذا الإصدار؛ استخدمها لرفع الملفات.",
                messageEn: "An active DRAFT documentation revision already exists for this API version.");
        }

        var maxNum = await dbContext.DocumentRevisions.AsNoTracking()
            .Where(r => r.ApiVersionId == version.Id)
            .Select(r => (int?)r.RevisionNumber)
            .MaxAsync(cancellationToken) ?? 0;

        var revision = new DocumentRevisionEntity
        {
            Id = Guid.NewGuid(),
            ApiVersionId = version.Id,
            RevisionNumber = maxNum + 1,
            BaseRevisionId = null,
            ChangeRequestId = null,
            Status = "DRAFT",
            CreatedBy = access.Actor.Id,
            CreatedAt = DateTimeOffset.UtcNow,
            ApprovedAt = null
        };

        // Snapshot current requirement template rules into revision_file_requirements
        var templateRules = await dbContext.FileRequirementRules.AsNoTracking()
            .Where(r => r.TemplateId == access.Document.RequirementTemplateId)
            .ToListAsync(cancellationToken);

        await using var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        dbContext.DocumentRevisions.Add(revision);

        foreach (var rule in templateRules)
        {
            dbContext.RevisionFileRequirements.Add(new RevisionFileRequirementEntity
            {
                Id = Guid.NewGuid(),
                RevisionId = revision.Id,
                FileTypeId = rule.FileTypeId,
                RequirementMode = rule.RequirementMode,
                MinCount = rule.MinCount,
                MaxCount = rule.MaxCount
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        await serverAuthorization.WriteAuditEventAsync(
            access.Actor.Id,
            "DOCUMENT_REVISION_CREATED",
            "DOCUMENT_REVISION",
            revision.Id,
            "SUCCESS",
            new { integrationId = integration.Id, apiVersionId = version.Id, version.VersionLabel, revision.RevisionNumber },
            cancellationToken);

        var dtos = await LoadRevisionDtosAsync(version.Id, access.Document.RequirementTemplateId, traceId, cancellationToken);
        return dtos.First(d => d.Id == revision.Id);
    }

    public async Task<IReadOnlyList<RevisionFileDto>> GetRevisionFilesAsync(
        ClaimsPrincipal actorPrincipal,
        Guid revisionId,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        await ResolveAuthorizedRevisionAsync(
            actorPrincipal,
            revisionId,
            DocumentPermissionCatalog.View,
            "REVISION_FILES_READ_DENIED",
            requireAssignedDeveloper: false,
            cancellationToken: cancellationToken);

        return await LoadRevisionFileDtosAsync(revisionId, traceId, cancellationToken);
    }

    public async Task<(int StatusCode, RevisionFileDto File)> UploadRevisionFileAsync(
        ClaimsPrincipal actorPrincipal,
        Guid revisionId,
        Guid fileTypeId,
        IFormFile uploadedFile,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var (access, integration, _, revision) = await ResolveAuthorizedRevisionAsync(
            actorPrincipal,
            revisionId,
            DocumentPermissionCatalog.FilesManage,
            "REVISION_FILE_UPLOAD_DENIED",
            requireAssignedDeveloper: true,
            cancellationToken: cancellationToken);

        if (!string.Equals(revision.Status, "DRAFT", StringComparison.OrdinalIgnoreCase))
        {
            throw ApiContractException.UnprocessableEntity(
                messageAr: "لا يمكن رفع ملفات لمراجعة غير موجودة في حالة مسودة (DRAFT).",
                messageEn: "Files can only be uploaded to a documentation revision in DRAFT status.");
        }

        if (string.Equals(access.Document.State, "ARCHIVED", StringComparison.OrdinalIgnoreCase))
        {
            throw ApiContractException.UnprocessableEntity(
                messageAr: "لا يمكن رفع ملفات لوثيقة مؤرشفة.",
                messageEn: "Cannot upload files to an archived API document.");
        }

        var fileType = await dbContext.FileTypes.AsNoTracking()
            .FirstOrDefaultAsync(ft => ft.Id == fileTypeId && ft.IsActive, cancellationToken);
        if (fileType is null)
        {
            throw ApiContractException.UnprocessableEntity(
                messageAr: "نوع الملف المحدد غير موجود أو غير مفعّل في النظام.",
                messageEn: "The selected file type does not exist or is inactive.");
        }

        // Verify FileTypeId belongs to the revision requirement snapshot (or template fallback if legacy)
        var reqRule = await dbContext.RevisionFileRequirements.AsNoTracking()
            .FirstOrDefaultAsync(r => r.RevisionId == revision.Id && r.FileTypeId == fileTypeId, cancellationToken);

        var maxAllowedCount = reqRule?.MaxCount ?? 0;
        if (reqRule is null)
        {
            var docRule = await dbContext.FileRequirementRules.AsNoTracking()
                .FirstOrDefaultAsync(r => r.TemplateId == access.Document.RequirementTemplateId && r.FileTypeId == fileTypeId, cancellationToken);
            if (docRule is null)
            {
                throw ApiContractException.UnprocessableEntity(
                    messageAr: "نوع الملف المختار غير مدرج ضمن قالب متطلبات هذه الوثيقة.",
                    messageEn: "The selected file type is not part of this document's file requirement template.");
            }

            maxAllowedCount = docRule.MaxCount;
        }

        if (maxAllowedCount > 0)
        {
            var existingCount = await dbContext.RevisionFiles.AsNoTracking()
                .CountAsync(rf => rf.RevisionId == revision.Id && rf.FileTypeId == fileTypeId, cancellationToken);

            if (existingCount >= maxAllowedCount)
            {
                throw ApiContractException.UnprocessableEntity(
                    messageAr: $"تم بلوغ الحد الأقصى المسموح به لعدد الملفات ({maxAllowedCount}) لنوع الملف ({fileType.DisplayName}).",
                    messageEn: $"Maximum allowed file count ({maxAllowedCount}) has already been reached for file type '{fileType.DisplayName}'.");
            }
        }

        var allowedExtensions = await dbContext.FileTypeExtensions.AsNoTracking()
            .Where(e => e.FileTypeId == fileType.Id)
            .Select(e => e.Extension)
            .ToListAsync(cancellationToken);

        var nameCheck = contentValidator.ValidateFilenameAndExtension(uploadedFile.FileName, allowedExtensions);
        if (!nameCheck.IsValid)
        {
            await serverAuthorization.WriteAuditEventAsync(
                access.Actor.Id,
                "FILE_VALIDATION_REJECTED",
                "DOCUMENT_REVISION",
                revision.Id,
                "DENIED",
                new { fileTypeId, originalName = uploadedFile.FileName, reason = nameCheck.ErrorCode },
                cancellationToken);

            throw ApiContractException.UnprocessableEntity(
                messageAr: nameCheck.MessageAr!,
                messageEn: nameCheck.MessageEn!,
                details: new { validationCode = nameCheck.ErrorCode });
        }

        var effectiveMaxBytes = Math.Min(fileType.MaxSizeBytes, Math.Max(1L, _storageOptions.MaxUploadBytesCeiling));
        if (uploadedFile.Length > effectiveMaxBytes)
        {
            throw ApiContractException.PayloadTooLarge(
                messageAr: $"حجم الملف المرفوع ({uploadedFile.Length:N0} بايت) يتجاوز الحد الأقصى المعتمد لهذا النوع ({effectiveMaxBytes:N0} بايت).",
                messageEn: $"Uploaded file size ({uploadedFile.Length:N0} bytes) exceeds the allowed limit ({effectiveMaxBytes:N0} bytes).");
        }

        StagedFileWriteResult staged;
        try
        {
            await using var uploadStream = uploadedFile.OpenReadStream();
            staged = await privateFileStore.StageStreamAsync(uploadStream, effectiveMaxBytes, cancellationToken);
        }
        catch (IOException ioEx) when (ioEx.HResult is unchecked((int)0x800700E1) or unchecked((int)0x800700E2))
        {
            await serverAuthorization.WriteAuditEventAsync(
                access.Actor.Id,
                "FILE_QUARANTINED_INFECTED",
                "DOCUMENT_REVISION",
                revision.Id,
                "DENIED",
                new { integrationId = integration.Id, revisionId = revision.Id, fileTypeId, originalName = nameCheck.NormalizedFileName, reason = "OS_REALTIME_AV_BLOCKED" },
                cancellationToken);

            throw ApiContractException.UnprocessableEntity(
                messageAr: "تم حظر الملف فورًا من حماية مكافحة الفيروسات على الخادم للاشتباه باحتوائه على برمجية ضارة.",
                messageEn: "File blocked immediately by host antivirus protection due to malware detection.",
                details: new { scanStatus = FileScanStatusValues.Infected, threatName = "HOST_AV_REALTIME_BLOCK" });
        }

        var fileObjectId = Guid.NewGuid();
        FileContentValidationResult contentResult;
        FileScanVerdict scanVerdict;
        try
        {
            contentResult = await contentValidator.ValidateStagedContentAsync(
                staged.StagedFilePath,
                nameCheck.NormalizedFileName!,
                nameCheck.Extension!,
                fileType.CatalogCode,
                cancellationToken);

            if (!contentResult.IsValid)
            {
                var quarantineKey = await privateFileStore.MoveToQuarantineAsync(staged.StagedFilePath, fileObjectId, cancellationToken);
                var invalidObject = new FileObjectEntity
                {
                    Id = fileObjectId,
                    StorageKey = quarantineKey,
                    Sha256 = staged.Sha256,
                    SizeBytes = staged.SizeBytes,
                    MediaType = "application/octet-stream",
                    ScanStatus = FileScanStatusValues.Invalid,
                    CreatedAt = DateTimeOffset.UtcNow
                };

                dbContext.FileObjects.Add(invalidObject);
                await dbContext.SaveChangesAsync(cancellationToken);

                await serverAuthorization.WriteAuditEventAsync(
                    access.Actor.Id,
                    "FILE_QUARANTINED_INVALID",
                    "FILE_OBJECT",
                    invalidObject.Id,
                    "DENIED",
                    new { revisionId = revision.Id, fileTypeId, originalName = nameCheck.NormalizedFileName, staged.Sha256, reason = contentResult.ErrorCode },
                    cancellationToken);

                throw ApiContractException.UnprocessableEntity(
                    messageAr: contentResult.MessageAr!,
                    messageEn: contentResult.MessageEn!,
                    details: new
                    {
                        validationCode = contentResult.ErrorCode,
                        quarantinedObjectId = invalidObject.Id,
                        sha256 = invalidObject.Sha256,
                        scanStatus = FileScanStatusValues.Invalid
                    });
            }

            // V03: Security Scan (fail-closed -> PENDING_SCAN if engine unavailable, never mocked CLEAN)
            scanVerdict = await fileScanner.ScanStagedFileAsync(
                staged.StagedFilePath,
                nameCheck.NormalizedFileName!,
                cancellationToken);
        }
        catch (IOException ioEx) when (ioEx.HResult is unchecked((int)0x800700E1) or unchecked((int)0x800700E2))
        {
            await privateFileStore.DeleteFileIfExistsAsync(staged.StagedFilePath, CancellationToken.None);
            await serverAuthorization.WriteAuditEventAsync(
                access.Actor.Id,
                "FILE_QUARANTINED_INFECTED",
                "DOCUMENT_REVISION",
                revision.Id,
                "DENIED",
                new { integrationId = integration.Id, revisionId = revision.Id, fileTypeId, originalName = nameCheck.NormalizedFileName, staged.Sha256, reason = "OS_REALTIME_AV_BLOCKED" },
                cancellationToken);

            throw ApiContractException.UnprocessableEntity(
                messageAr: "تم حظر الملف وعزله بواسطة حماية مكافحة الفيروسات على الخادم.",
                messageEn: "File blocked and quarantined by host antivirus protection.",
                details: new { scanStatus = FileScanStatusValues.Infected, threatName = "HOST_AV_REALTIME_BLOCK", sha256 = staged.Sha256 });
        }

        if (string.Equals(scanVerdict.ScanStatus, FileScanStatusValues.Infected, StringComparison.OrdinalIgnoreCase))
        {
            var quarantineKey = await privateFileStore.MoveToQuarantineAsync(staged.StagedFilePath, fileObjectId, cancellationToken);
            var infectedObject = new FileObjectEntity
            {
                Id = fileObjectId,
                StorageKey = quarantineKey,
                Sha256 = staged.Sha256,
                SizeBytes = staged.SizeBytes,
                MediaType = contentResult.CanonicalMediaType,
                ScanStatus = FileScanStatusValues.Infected,
                CreatedAt = DateTimeOffset.UtcNow
            };

            dbContext.FileObjects.Add(infectedObject);
            await dbContext.SaveChangesAsync(cancellationToken);

            await serverAuthorization.WriteAuditEventAsync(
                access.Actor.Id,
                "FILE_QUARANTINED_INFECTED",
                "FILE_OBJECT",
                infectedObject.Id,
                "DENIED",
                new { revisionId = revision.Id, fileTypeId, originalName = nameCheck.NormalizedFileName, staged.Sha256, scanVerdict.ThreatName },
                cancellationToken);

            throw ApiContractException.UnprocessableEntity(
                messageAr: scanVerdict.MessageAr,
                messageEn: scanVerdict.MessageEn,
                details: new
                {
                    scanStatus = FileScanStatusValues.Infected,
                    threatName = scanVerdict.ThreatName,
                    quarantinedObjectId = infectedObject.Id,
                    sha256 = infectedObject.Sha256
                });
        }

        var isClean = string.Equals(scanVerdict.ScanStatus, FileScanStatusValues.Clean, StringComparison.OrdinalIgnoreCase);
        var finalStorageKey = isClean
            ? await privateFileStore.CommitImmutableObjectAsync(staged.StagedFilePath, fileObjectId, staged.Sha256, cancellationToken)
            : await privateFileStore.MoveToQuarantineAsync(staged.StagedFilePath, fileObjectId, cancellationToken);

        var persistedScanStatus = isClean ? FileScanStatusValues.Clean : FileScanStatusValues.PendingScan;

        var fileObject = new FileObjectEntity
        {
            Id = fileObjectId,
            StorageKey = finalStorageKey,
            Sha256 = staged.Sha256,
            SizeBytes = staged.SizeBytes,
            MediaType = contentResult.CanonicalMediaType,
            ScanStatus = persistedScanStatus,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var revisionFile = new RevisionFileEntity
        {
            Id = Guid.NewGuid(),
            RevisionId = revision.Id,
            FileTypeId = fileType.Id,
            ObjectId = fileObject.Id,
            OriginalName = nameCheck.NormalizedFileName!,
            SourceRevisionFileId = null,
            UploadedBy = access.Actor.Id,
            UploadedAt = DateTimeOffset.UtcNow
        };

        await using var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        dbContext.FileObjects.Add(fileObject);
        dbContext.RevisionFiles.Add(revisionFile);
        await dbContext.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        await serverAuthorization.WriteAuditEventAsync(
            access.Actor.Id,
            isClean ? "FILE_UPLOADED_CLEAN" : "FILE_QUARANTINED_PENDING_SCAN",
            "REVISION_FILE",
            revisionFile.Id,
            isClean ? "SUCCESS" : "PENDING",
            new
            {
                integrationId = integration.Id,
                revisionId = revision.Id,
                objectId = fileObject.Id,
                fileTypeId = fileType.Id,
                revisionFile.OriginalName,
                fileObject.Sha256,
                fileObject.SizeBytes,
                fileObject.ScanStatus,
                scanVerdict.ReasonCode
            },
            cancellationToken);

        var dto = new RevisionFileDto(
            revisionFile.Id,
            revisionFile.RevisionId,
            fileType.Id,
            fileType.DisplayName,
            fileType.CatalogCode,
            fileObject.Id,
            revisionFile.OriginalName,
            fileObject.Sha256,
            fileObject.SizeBytes,
            fileObject.MediaType,
            fileObject.ScanStatus,
            IsAvailableForUse: isClean,
            IsQuarantined: !isClean,
            SourceRevisionFileId: revisionFile.SourceRevisionFileId,
            UploadedBy: access.Actor.Id,
            UploadedByName: access.Actor.FullName,
            UploadedAt: revisionFile.UploadedAt,
            TraceId: traceId);

        return (isClean ? StatusCodes.Status201Created : StatusCodes.Status202Accepted, dto);
    }

    public async Task<(int StatusCode, RevisionFileDto File)> RescanRevisionFileAsync(
        ClaimsPrincipal actorPrincipal,
        Guid revisionId,
        Guid revisionFileId,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var (access, integration, _, revision) = await ResolveAuthorizedRevisionAsync(
            actorPrincipal,
            revisionId,
            DocumentPermissionCatalog.FilesManage,
            "REVISION_FILE_RESCAN_DENIED",
            requireAssignedDeveloper: true,
            cancellationToken: cancellationToken);

        if (!string.Equals(revision.Status, "DRAFT", StringComparison.OrdinalIgnoreCase))
        {
            throw ApiContractException.UnprocessableEntity(
                messageAr: "إعادة الفحص متاحة فقط للملفات داخل مراجعة مسودة (DRAFT).",
                messageEn: "Re-scanning is only allowed on files inside a DRAFT revision.");
        }

        var revFile = await dbContext.RevisionFiles
            .FirstOrDefaultAsync(rf => rf.Id == revisionFileId && rf.RevisionId == revision.Id, cancellationToken);
        if (revFile is null)
        {
            throw ApiContractException.NotFound(
                messageAr: "ملف المراجعة المطلوب غير موجود.",
                messageEn: "Revision file was not found.");
        }

        var fileObject = await dbContext.FileObjects
            .FirstOrDefaultAsync(fo => fo.Id == revFile.ObjectId, cancellationToken);
        if (fileObject is null)
        {
            throw ApiContractException.NotFound(
                messageAr: "كائن الملف المرتبط غير موجود.",
                messageEn: "Linked file object was not found.");
        }

        if (string.Equals(fileObject.ScanStatus, FileScanStatusValues.Clean, StringComparison.OrdinalIgnoreCase))
        {
            var existingDtos = await LoadRevisionFileDtosAsync(revision.Id, traceId, cancellationToken);
            return (StatusCodes.Status200OK, existingDtos.First(f => f.Id == revFile.Id));
        }

        if (!string.Equals(fileObject.ScanStatus, FileScanStatusValues.PendingScan, StringComparison.OrdinalIgnoreCase))
        {
            throw ApiContractException.UnprocessableEntity(
                messageAr: "لا يمكن إعادة فحص ملف مرفوض مسبقًا كملف مشبوه أو غير صالح.",
                messageEn: "Cannot re-scan a file object previously rejected as INFECTED or INVALID.");
        }

        var quarantinedPath = privateFileStore.ResolveQuarantineFullPath(fileObject.StorageKey);
        if (!File.Exists(quarantinedPath))
        {
            throw ApiContractException.NotFound(
                messageAr: "تعذر العثور على الملف المحجور في مخزن الحجر الأمني.",
                messageEn: "Quarantined file content was not found on disk.");
        }

        var scanVerdict = await fileScanner.ScanStagedFileAsync(quarantinedPath, revFile.OriginalName, cancellationToken);

        if (string.Equals(scanVerdict.ScanStatus, FileScanStatusValues.Infected, StringComparison.OrdinalIgnoreCase))
        {
            fileObject.ScanStatus = FileScanStatusValues.Infected;
            dbContext.RevisionFiles.Remove(revFile);
            await dbContext.SaveChangesAsync(cancellationToken);

            await serverAuthorization.WriteAuditEventAsync(
                access.Actor.Id,
                "FILE_RESCAN_INFECTED",
                "FILE_OBJECT",
                fileObject.Id,
                "DENIED",
                new { integrationId = integration.Id, revisionId = revision.Id, revFile.OriginalName, fileObject.Sha256, scanVerdict.ThreatName },
                cancellationToken);

            throw ApiContractException.UnprocessableEntity(
                messageAr: scanVerdict.MessageAr,
                messageEn: scanVerdict.MessageEn,
                details: new { scanStatus = FileScanStatusValues.Infected, threatName = scanVerdict.ThreatName, sha256 = fileObject.Sha256 });
        }

        if (string.Equals(scanVerdict.ScanStatus, FileScanStatusValues.Clean, StringComparison.OrdinalIgnoreCase))
        {
            var permanentKey = await privateFileStore.CommitImmutableObjectAsync(
                quarantinedPath, fileObject.Id, fileObject.Sha256, cancellationToken);

            fileObject.StorageKey = permanentKey;
            fileObject.ScanStatus = FileScanStatusValues.Clean;
            await dbContext.SaveChangesAsync(cancellationToken);

            await serverAuthorization.WriteAuditEventAsync(
                access.Actor.Id,
                "FILE_RESCAN_CLEAN",
                "REVISION_FILE",
                revFile.Id,
                "SUCCESS",
                new { integrationId = integration.Id, revisionId = revision.Id, objectId = fileObject.Id, fileObject.Sha256 },
                cancellationToken);

            var cleanDtos = await LoadRevisionFileDtosAsync(revision.Id, traceId, cancellationToken);
            return (StatusCodes.Status200OK, cleanDtos.First(f => f.Id == revFile.Id));
        }

        await serverAuthorization.WriteAuditEventAsync(
            access.Actor.Id,
            "FILE_RESCAN_PENDING",
            "REVISION_FILE",
            revFile.Id,
            "PENDING",
            new { integrationId = integration.Id, revisionId = revision.Id, objectId = fileObject.Id, scanVerdict.ReasonCode },
            cancellationToken);

        var stillPendingDtos = await LoadRevisionFileDtosAsync(revision.Id, traceId, cancellationToken);
        return (StatusCodes.Status202Accepted, stillPendingDtos.First(f => f.Id == revFile.Id));
    }

    public async Task DeleteDraftRevisionFileAsync(
        ClaimsPrincipal actorPrincipal,
        Guid revisionId,
        Guid revisionFileId,
        CancellationToken cancellationToken = default)
    {
        var (access, integration, _, revision) = await ResolveAuthorizedRevisionAsync(
            actorPrincipal,
            revisionId,
            DocumentPermissionCatalog.FilesManage,
            "REVISION_FILE_DELETE_DENIED",
            requireAssignedDeveloper: true,
            cancellationToken: cancellationToken);

        if (!string.Equals(revision.Status, "DRAFT", StringComparison.OrdinalIgnoreCase))
        {
            throw ApiContractException.UnprocessableEntity(
                messageAr: "يُمنع حذف ملفات من مراجعة معتمدة أو مغلقة.",
                messageEn: "Cannot remove files from a non-draft documentation revision.");
        }

        var revFile = await dbContext.RevisionFiles
            .FirstOrDefaultAsync(rf => rf.Id == revisionFileId && rf.RevisionId == revision.Id, cancellationToken);
        if (revFile is null)
        {
            throw ApiContractException.NotFound(
                messageAr: "ملف المراجعة المطلوب غير موجود.",
                messageEn: "Revision file was not found.");
        }

        dbContext.RevisionFiles.Remove(revFile);
        await dbContext.SaveChangesAsync(cancellationToken);

        await serverAuthorization.WriteAuditEventAsync(
            access.Actor.Id,
            "REVISION_FILE_REMOVED",
            "REVISION_FILE",
            revFile.Id,
            "SUCCESS",
            new { integrationId = integration.Id, revisionId = revision.Id, revFile.ObjectId, revFile.OriginalName },
            cancellationToken);
    }

    public async Task<FileScanStatusDto> GetFileScanStatusAsync(
        ClaimsPrincipal actorPrincipal,
        Guid revisionFileId,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var revFile = await dbContext.RevisionFiles.AsNoTracking()
            .FirstOrDefaultAsync(rf => rf.Id == revisionFileId, cancellationToken);

        if (revFile is null)
        {
            throw ApiContractException.NotFound(
                messageAr: "الملف المطلوب غير موجود أو غير متاح ضمن صلاحياتك.",
                messageEn: "File was not found or access is restricted.");
        }

        await ResolveAuthorizedRevisionAsync(
            actorPrincipal,
            revFile.RevisionId,
            DocumentPermissionCatalog.View,
            "FILE_SCAN_STATUS_DENIED",
            requireAssignedDeveloper: false,
            cancellationToken: cancellationToken);

        var fileObject = await dbContext.FileObjects.AsNoTracking()
            .FirstOrDefaultAsync(fo => fo.Id == revFile.ObjectId, cancellationToken);

        if (fileObject is null)
        {
            throw ApiContractException.NotFound(
                messageAr: "بيانات كائن الملف غير موجودة.",
                messageEn: "File object metadata was not found.");
        }

        var isClean = string.Equals(fileObject.ScanStatus, FileScanStatusValues.Clean, StringComparison.OrdinalIgnoreCase);
        var tier = fileObject.StorageKey.StartsWith("objects/", StringComparison.Ordinal)
            ? "IMMUTABLE_PRIVATE"
            : "QUARANTINE";

        return new FileScanStatusDto(
            fileObject.Id,
            fileObject.ScanStatus,
            IsAvailableForUse: isClean,
            IsQuarantined: !isClean,
            fileObject.Sha256,
            fileObject.SizeBytes,
            fileObject.MediaType,
            StorageTier: tier,
            fileObject.CreatedAt,
            traceId);
    }

    public async Task<(Stream ContentStream, string FileName, string ContentType, long SizeBytes, string Sha256)> DownloadCleanRevisionFileAsync(
        ClaimsPrincipal actorPrincipal,
        Guid revisionId,
        Guid revisionFileId,
        CancellationToken cancellationToken = default)
    {
        var (access, integration, _, _) = await ResolveAuthorizedRevisionAsync(
            actorPrincipal,
            revisionId,
            DocumentPermissionCatalog.View,
            "FILE_DOWNLOAD_DENIED",
            requireAssignedDeveloper: false,
            cancellationToken: cancellationToken);

        var revFile = await dbContext.RevisionFiles.AsNoTracking()
            .FirstOrDefaultAsync(rf => rf.Id == revisionFileId && rf.RevisionId == revisionId, cancellationToken);
        if (revFile is null)
        {
            throw ApiContractException.NotFound(
                messageAr: "الملف المطلوب غير موجود ضمن هذه المراجعة.",
                messageEn: "Revision file was not found in the specified revision.");
        }

        var fileObject = await dbContext.FileObjects.AsNoTracking()
            .FirstOrDefaultAsync(fo => fo.Id == revFile.ObjectId, cancellationToken);
        if (fileObject is null)
        {
            throw ApiContractException.NotFound(
                messageAr: "تعذر العثور على بيانات كائن الملف.",
                messageEn: "File object record not found.");
        }

        if (!string.Equals(fileObject.ScanStatus, FileScanStatusValues.Clean, StringComparison.OrdinalIgnoreCase))
        {
            await serverAuthorization.WriteAuditEventAsync(
                access.Actor.Id,
                "FILE_DOWNLOAD_BLOCKED",
                "REVISION_FILE",
                revFile.Id,
                "DENIED",
                new { integrationId = integration.Id, revisionId, objectId = fileObject.Id, fileObject.ScanStatus },
                cancellationToken);

            throw ApiContractException.UnprocessableEntity(
                messageAr: $"ممنوع: هذا الملف في حالة ({fileObject.ScanStatus}) ولم يجتز الفحص الأمني بعد؛ لا يُتاح للتنزيل أو الاستخدام.",
                messageEn: $"Blocked: file is in '{fileObject.ScanStatus}' status and cannot be downloaded or exposed prior to passing security scan.",
                details: new { scanStatus = fileObject.ScanStatus, sha256 = fileObject.Sha256 });
        }

        var (stream, sizeBytes) = await privateFileStore.OpenVerifiedImmutableObjectReadStreamAsync(
            fileObject.StorageKey,
            fileObject.Sha256,
            cancellationToken);

        await serverAuthorization.WriteAuditEventAsync(
            access.Actor.Id,
            "FILE_DOWNLOADED",
            "REVISION_FILE",
            revFile.Id,
            "SUCCESS",
            new { integrationId = integration.Id, revisionId, objectId = fileObject.Id, revFile.OriginalName, fileObject.Sha256 },
            cancellationToken);

        return (stream, revFile.OriginalName, fileObject.MediaType ?? "application/octet-stream", sizeBytes, fileObject.Sha256);
    }

    private async Task<(AuthorizedDocumentAccess Access, IntegrationEntity Integration, ApiVersionEntity Version, DocumentRevisionEntity Revision)>
        ResolveAuthorizedRevisionAsync(
            ClaimsPrincipal actorPrincipal,
            Guid revisionId,
            string requiredDocPermissionCode,
            string auditOperationOnDeny,
            bool requireAssignedDeveloper,
            CancellationToken cancellationToken)
    {
        var revision = await dbContext.DocumentRevisions.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == revisionId, cancellationToken);

        if (revision is null)
        {
            throw ApiContractException.NotFound(
                messageAr: "مراجعة التوثيق المطلوبة غير موجودة أو غير متاحة ضمن صلاحياتك.",
                messageEn: "Documentation revision was not found or access is restricted.");
        }

        var version = await dbContext.ApiVersions.AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == revision.ApiVersionId, cancellationToken);

        if (version is null)
        {
            throw ApiContractException.NotFound(
                messageAr: "إصدار API المرتبط غير موجود أو غير متاح.",
                messageEn: "Associated API version was not found or access is restricted.");
        }

        var (access, integration) = await documentAuthorization.RequireIntegrationDocumentPermissionAsync(
            actorPrincipal,
            version.IntegrationId,
            requiredDocPermissionCode,
            auditOperationOnDeny,
            cancellationToken);

        if (requireAssignedDeveloper)
        {
            await RequireAssignedDeveloperAsync(access.Actor.Id, integration, auditOperationOnDeny, cancellationToken);
        }

        return (access, integration, version, revision);
    }

    private async Task RequireAssignedDeveloperAsync(
        Guid actorId,
        IntegrationEntity integration,
        string auditOp,
        CancellationToken cancellationToken)
    {
        var isAssigned = await documentAuthorization.IsUserAssignedDeveloperAsync(
            actorId, integration.DocumentId, integration.Id, cancellationToken);

        if (!isAssigned)
        {
            await serverAuthorization.WriteAuditEventAsync(
                actorId,
                auditOp,
                "INTEGRATION",
                integration.Id,
                "DENIED",
                new { reason = "NOT_ASSIGNED_DEVELOPER", integrationId = integration.Id, integration.DocumentId },
                cancellationToken);

            throw ApiContractException.Forbidden(
                messageAr: "يتطلب هذا الإجراء أن يكون المستخدم معيّنًا مطوّرًا مسؤولًا على التكامل أو الوثيقة (تعيين المطور مستقل عن الصلاحيات العامة).",
                messageEn: "This operation requires the user to be explicitly assigned as a developer on the integration or API document.");
        }
    }

    private async Task<IReadOnlyList<ApiVersionDto>> LoadApiVersionDtosAsync(
        Guid integrationId,
        string traceId,
        CancellationToken cancellationToken)
    {
        var rows = await dbContext.ApiVersions.AsNoTracking()
            .Where(v => v.IntegrationId == integrationId)
            .OrderByDescending(v => v.CreatedAt)
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return [];
        }

        var versionIds = rows.Select(v => v.Id).ToList();
        var revisions = await dbContext.DocumentRevisions.AsNoTracking()
            .Where(r => versionIds.Contains(r.ApiVersionId))
            .OrderByDescending(r => r.RevisionNumber)
            .Select(r => new { r.ApiVersionId, r.Status })
            .ToListAsync(cancellationToken);

        var revByVer = revisions.GroupBy(r => r.ApiVersionId).ToDictionary(g => g.Key, g => g.ToList());

        return rows.Select(v =>
        {
            revByVer.TryGetValue(v.Id, out var list);
            return new ApiVersionDto(
                v.Id,
                v.IntegrationId,
                v.VersionLabel,
                v.IsArchived,
                v.CreatedAt,
                RevisionsCount: list?.Count ?? 0,
                LatestRevisionStatus: list?.FirstOrDefault()?.Status,
                TraceId: traceId);
        }).ToList();
    }

    private async Task<IReadOnlyList<DocumentRevisionDto>> LoadRevisionDtosAsync(
        Guid versionId,
        Guid fallbackTemplateId,
        string traceId,
        CancellationToken cancellationToken)
    {
        var version = await dbContext.ApiVersions.AsNoTracking()
            .FirstAsync(v => v.Id == versionId, cancellationToken);

        var revisions = await dbContext.DocumentRevisions.AsNoTracking()
            .Where(r => r.ApiVersionId == versionId)
            .OrderByDescending(r => r.RevisionNumber)
            .ToListAsync(cancellationToken);

        if (revisions.Count == 0)
        {
            return [];
        }

        var revIds = revisions.Select(r => r.Id).ToList();
        var creatorIds = revisions.Select(r => r.CreatedBy).Distinct().ToList();
        var creators = await dbContext.Users.AsNoTracking()
            .Where(u => creatorIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, cancellationToken);

        var reqRows = await dbContext.RevisionFileRequirements.AsNoTracking()
            .Where(rr => revIds.Contains(rr.RevisionId))
            .ToListAsync(cancellationToken);

        var revFiles = await (
            from rf in dbContext.RevisionFiles.AsNoTracking()
            join fo in dbContext.FileObjects.AsNoTracking() on rf.ObjectId equals fo.Id
            where revIds.Contains(rf.RevisionId)
            select new { rf.RevisionId, rf.FileTypeId, fo.ScanStatus })
            .ToListAsync(cancellationToken);

        var ftIds = reqRows.Select(r => r.FileTypeId).Distinct().ToList();
        var fileTypes = ftIds.Count == 0
            ? new Dictionary<Guid, FileTypeEntity>()
            : await dbContext.FileTypes.AsNoTracking()
                .Where(ft => ftIds.Contains(ft.Id))
                .ToDictionaryAsync(ft => ft.Id, cancellationToken);

        var exts = ftIds.Count == 0
            ? new Dictionary<Guid, IReadOnlyList<string>>()
            : (await dbContext.FileTypeExtensions.AsNoTracking()
                .Where(e => ftIds.Contains(e.FileTypeId))
                .OrderBy(e => e.Extension)
                .ToListAsync(cancellationToken))
                .GroupBy(e => e.FileTypeId)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(x => x.Extension).ToList());

        return revisions.Select(rev =>
        {
            var myReqs = reqRows.Where(r => r.RevisionId == rev.Id).ToList();
            var myFiles = revFiles.Where(f => f.RevisionId == rev.Id).ToList();

            var reqProgress = myReqs
                .OrderBy(r => fileTypes.TryGetValue(r.FileTypeId, out var ft) ? ft.DisplayName : string.Empty)
                .Select(r =>
                {
                    fileTypes.TryGetValue(r.FileTypeId, out var ft);
                    var matchingFiles = myFiles.Where(f => f.FileTypeId == r.FileTypeId).ToList();
                    return new RevisionRequirementProgressDto(
                        r.FileTypeId,
                        ft?.DisplayName ?? string.Empty,
                        ft?.CatalogCode ?? string.Empty,
                        exts.GetValueOrDefault(r.FileTypeId, []),
                        ft?.MaxSizeBytes ?? 0,
                        r.RequirementMode,
                        r.MinCount,
                        r.MaxCount,
                        UploadedCleanCount: matchingFiles.Count(f => string.Equals(f.ScanStatus, FileScanStatusValues.Clean, StringComparison.OrdinalIgnoreCase)),
                        UploadedPendingScanCount: matchingFiles.Count(f => string.Equals(f.ScanStatus, FileScanStatusValues.PendingScan, StringComparison.OrdinalIgnoreCase)));
                })
                .ToList();

            return new DocumentRevisionDto(
                rev.Id,
                rev.ApiVersionId,
                version.VersionLabel,
                rev.RevisionNumber,
                rev.BaseRevisionId,
                rev.ChangeRequestId,
                rev.Status,
                rev.CreatedBy,
                creators.GetValueOrDefault(rev.CreatedBy, string.Empty),
                rev.CreatedAt,
                rev.ApprovedAt,
                FilesCount: myFiles.Count,
                PendingScanCount: myFiles.Count(f => string.Equals(f.ScanStatus, FileScanStatusValues.PendingScan, StringComparison.OrdinalIgnoreCase)),
                Requirements: reqProgress,
                TraceId: traceId);
        }).ToList();
    }

    private async Task<IReadOnlyList<RevisionFileDto>> LoadRevisionFileDtosAsync(
        Guid revisionId,
        string traceId,
        CancellationToken cancellationToken)
    {
        var rows = await (
            from rf in dbContext.RevisionFiles.AsNoTracking()
            join fo in dbContext.FileObjects.AsNoTracking() on rf.ObjectId equals fo.Id
            join ft in dbContext.FileTypes.AsNoTracking() on rf.FileTypeId equals ft.Id
            join u in dbContext.Users.AsNoTracking() on rf.UploadedBy equals u.Id
            where rf.RevisionId == revisionId
            orderby rf.UploadedAt descending
            select new
            {
                rf,
                fo,
                ft.DisplayName,
                ft.CatalogCode,
                u.FullName
            })
            .ToListAsync(cancellationToken);

        return rows.Select(x =>
        {
            var isClean = string.Equals(x.fo.ScanStatus, FileScanStatusValues.Clean, StringComparison.OrdinalIgnoreCase);
            return new RevisionFileDto(
                x.rf.Id,
                x.rf.RevisionId,
                x.rf.FileTypeId,
                x.DisplayName,
                x.CatalogCode,
                x.fo.Id,
                x.rf.OriginalName,
                x.fo.Sha256,
                x.fo.SizeBytes,
                x.fo.MediaType,
                x.fo.ScanStatus,
                IsAvailableForUse: isClean,
                IsQuarantined: !isClean,
                x.rf.SourceRevisionFileId,
                x.rf.UploadedBy,
                x.FullName,
                x.rf.UploadedAt,
                traceId);
        }).ToList();
    }

    private async Task<(string DerivedTitle, string ProviderName, string ConsumerName, string ApiGroupName)>
        LoadDocumentPartyLabelsAsync(ApiDocumentEntity doc, CancellationToken cancellationToken)
    {
        var compIds = new[] { doc.ProviderInternalSystemId, doc.ConsumerCompanySystemId }
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        var compMap = compIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await dbContext.CompanySystems.AsNoTracking()
                .Where(c => compIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);

        var providerName = doc.ProviderInternalSystemId.HasValue
            ? compMap.GetValueOrDefault(doc.ProviderInternalSystemId.Value, string.Empty)
            : await dbContext.ExternalProviderSystems.AsNoTracking()
                .Where(e => e.Id == doc.ProviderExternalSystemId!.Value)
                .Select(e => e.Name)
                .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

        var consumerName = doc.ConsumerCompanySystemId.HasValue
            ? compMap.GetValueOrDefault(doc.ConsumerCompanySystemId.Value, string.Empty)
            : await dbContext.ConsumerEntities.AsNoTracking()
                .Where(c => c.Id == doc.ConsumerEntityId!.Value)
                .Select(c => c.Name)
                .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

        var apiGroupName = await dbContext.ApiGroups.AsNoTracking()
            .Where(g => g.Id == doc.ApiGroupId)
            .Select(g => g.Name)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

        return ($"{providerName} ← {consumerName}", providerName, consumerName, apiGroupName);
    }
}
