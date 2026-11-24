using System.Security.Claims;
using System.Text.Json;
using ApiGovernance.Modules.VersionsFiles.Configuration;
using ApiGovernance.Modules.VersionsFiles.Contracts;
using ApiGovernance.Modules.VersionsFiles.OpenXml;
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
using Npgsql;

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

    Task<IReadOnlyList<DocumentRevisionDto>> GetVersionRevisionsAsync(
        ClaimsPrincipal actorPrincipal,
        Guid versionId,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<DocumentRevisionDto> CreateRevisionAsync(
        ClaimsPrincipal actorPrincipal,
        Guid versionId,
        CreateDocumentRevisionRequest? request,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<RevisionDetailsResponse> GetRevisionDetailsAsync(
        ClaimsPrincipal actorPrincipal,
        Guid revisionId,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<SubmitRevisionResponse> SubmitRevisionAsync(
        ClaimsPrincipal actorPrincipal,
        Guid revisionId,
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

    Task<WordDerivativeRecordDto> GenerateWordDerivativeAsync(
        ClaimsPrincipal actorPrincipal,
        Guid revisionId,
        Guid revisionFileId,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<WordDerivativeRecordDto?> GetLatestWordDerivativeAsync(
        ClaimsPrincipal actorPrincipal,
        Guid revisionId,
        Guid revisionFileId,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<(Stream ContentStream, string FileName, string ContentType, long SizeBytes, string Sha256)> DownloadVerifiedWordDerivativeAsync(
        ClaimsPrincipal actorPrincipal,
        Guid revisionId,
        Guid revisionFileId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Implements Sprint S08 (V01–V04) and Sprint S09 (V05–V08):
/// - V01: Private non-web immutable object storage with SHA-256 verification.
/// - V02: Strict extension, quota, magic-byte, and archive safety checks + quarantine isolation.
/// - V03: Fail-closed AV scanning (PENDING_SCAN when engine absent; INFECTED rejected/quarantined).
/// - V04: Manual free-text API version labels cleanly decoupled from document revisions.
/// - V05: Documentation package revisions, mandatory file completeness validation, and guarded submission.
/// - V06: Protection of approved/submitted revisions, working copy creation from approved base + change request, OP-05 concurrency.
/// - V07: Server-authorized, SHA-256 verified, fully audited file downloads.
/// - V08: Open XML SDK PoC for updating recognized Word governance fields into a verified derived copy without altering the original (OP-03).
/// </summary>
public sealed class VersionsFilesService(
    ApiGovernanceDbContext dbContext,
    IServerAuthorizationService serverAuthorization,
    IDocumentAuthorizationService documentAuthorization,
    IPrivateFileStore privateFileStore,
    IFileSecurityScanner fileScanner,
    FileUploadContentValidator contentValidator,
    IWordGovernanceDerivativeService wordDerivativeService,
    IOptions<FileStorageOptions> storageOptions) : IVersionsFilesService
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
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
        IReadOnlyList<WordDerivativeRecordDto> wordDerivatives = [];

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
            revisions = await LoadRevisionDtosAsync(effectiveVersionId.Value, traceId, cancellationToken);
            selectedRevision = revisionId.HasValue
                ? revisions.FirstOrDefault(r => r.Id == revisionId.Value) ?? revisions.FirstOrDefault()
                : revisions.FirstOrDefault();

            if (selectedRevision is not null)
            {
                files = await LoadRevisionFileDtosAsync(selectedRevision.Id, traceId, cancellationToken);
                wordDerivatives = await LoadWordDerivativeDtosForRevisionAsync(selectedRevision.Id, traceId, cancellationToken);
            }
        }

        var approvedChangeRequests = await LoadApprovedChangeRequestsAsync(integration.Id, cancellationToken);

        var hasFilesUpload = await serverAuthorization.HasGeneralPermissionAsync(actorPrincipal, "files.upload", cancellationToken);
        var hasFilesDelete = await serverAuthorization.HasGeneralPermissionAsync(actorPrincipal, "files.delete", cancellationToken);
        var hasFilesDownload = await serverAuthorization.HasGeneralPermissionAsync(actorPrincipal, "files.download", cancellationToken);
        var hasRevisionsCreate = await serverAuthorization.HasGeneralPermissionAsync(actorPrincipal, "revisions.create", cancellationToken);
        var hasRevisionsSubmit = await serverAuthorization.HasGeneralPermissionAsync(actorPrincipal, "revisions.submit", cancellationToken);

        var hasDocFilesManage = access.EffectivePermissions.TryGetValue(DocumentPermissionCatalog.FilesManage, out var fm) && fm.Allowed;
        var hasDocEdit = access.EffectivePermissions.TryGetValue(DocumentPermissionCatalog.Edit, out var de) && de.Allowed;
        var scannerInfo = fileScanner.GetRuntimeInfo();
        var notArchived = !string.Equals(access.Document.State, "ARCHIVED", StringComparison.OrdinalIgnoreCase);

        return new IntegrationFilesWorkspaceResponse(
            integration.Id,
            integration.DocumentId,
            integration.ReferenceCode,
            derivedTitle,
            CanCreateDraftRevision: hasRevisionsCreate && hasDocEdit && isAssignedDev && notArchived,
            CanUploadFiles: hasFilesUpload && hasDocFilesManage && isAssignedDev && notArchived,
            CanDeleteFiles: hasFilesDelete && hasDocFilesManage && isAssignedDev && notArchived,
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
            TraceId: traceId,
            CanSubmitRevision: hasRevisionsSubmit && hasDocEdit && isAssignedDev && notArchived,
            CanCreateWorkingCopy: hasRevisionsCreate && hasDocEdit && isAssignedDev && notArchived,
            ApprovedChangeRequests: approvedChangeRequests,
            WordDerivatives: wordDerivatives);
    }

    public async Task<IReadOnlyList<DocumentRevisionDto>> GetVersionRevisionsAsync(
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

        await documentAuthorization.RequireIntegrationDocumentPermissionAsync(
            actorPrincipal,
            version.IntegrationId,
            DocumentPermissionCatalog.View,
            "REVISION_LIST_DENIED",
            cancellationToken);

        return await LoadRevisionDtosAsync(version.Id, traceId, cancellationToken);
    }

    public async Task<DocumentRevisionDto> CreateRevisionAsync(
        ClaimsPrincipal actorPrincipal,
        Guid versionId,
        CreateDocumentRevisionRequest? request,
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

        var isWorkingCopyMode = request is not null && (request.BaseRevisionId.HasValue || request.ChangeRequestId.HasValue);

        if (!isWorkingCopyMode)
        {
            // Mode A: Initial Draft — blocked if an APPROVED revision already exists (V06 / AC-07)
            var hasApproved = await dbContext.DocumentRevisions.AsNoTracking()
                .AnyAsync(r => r.ApiVersionId == version.Id && r.Status == RevisionStatusValues.Approved, cancellationToken);

            if (hasApproved)
            {
                await serverAuthorization.WriteAuditEventAsync(
                    access.Actor.Id,
                    "REVISION_CREATE_BLOCKED_NEEDS_CHANGE_REQUEST",
                    "API_VERSION",
                    version.Id,
                    "DENIED",
                    new { integrationId = integration.Id, versionId = version.Id },
                    cancellationToken);

                throw ApiContractException.UnprocessableEntity(
                    messageAr: "توجد مراجعة معتمدة لهذا الإصدار؛ لا يمكن إنشاء نسخة عمل جديدة إلا بناءً على طلب تغيير معتمد (V06).",
                    messageEn: "An approved revision already exists for this API version; creating a new working copy requires an approved change request (V06).");
            }
        }
        else
        {
            // Mode B: Working Copy from Approved Base Revision + Approved Change Request (V06 / OP-05)
            if (!request!.BaseRevisionId.HasValue || !request.ChangeRequestId.HasValue)
            {
                throw ApiContractException.BadRequest(
                    messageAr: "لإنشاء نسخة عمل من مراجعة معتمدة يلزم تحديد كلٍّ من معرف المراجعة المرجعية (BaseRevisionId) ومعرف طلب التغيير المعتمد (ChangeRequestId).",
                    messageEn: "Creating a working copy requires both BaseRevisionId and ChangeRequestId.");
            }
        }

        var hasActiveDraft = await dbContext.DocumentRevisions.AsNoTracking()
            .AnyAsync(r => r.ApiVersionId == version.Id && r.Status == RevisionStatusValues.Draft, cancellationToken);
        if (hasActiveDraft)
        {
            throw ApiContractException.Conflict(
                messageAr: "توجد مسودة مراجعة نشطة بالفعل لهذا الإصدار؛ لا يُسمح بأكثر من نسخة عمل نشطة في وقت واحد (OP-05).",
                messageEn: "An active DRAFT working copy already exists for this API version (OP-05 single active working copy rule).");
        }

        DocumentRevisionEntity? baseRevision = null;
        ChangeRequestEntity? changeRequest = null;

        if (isWorkingCopyMode)
        {
            baseRevision = await dbContext.DocumentRevisions.AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == request!.BaseRevisionId!.Value && r.ApiVersionId == version.Id, cancellationToken);

            if (baseRevision is null)
            {
                throw ApiContractException.NotFound(
                    messageAr: "المراجعة الأساسية المطلوبة غير موجودة ضمن هذا الإصدار.",
                    messageEn: "Specified base revision was not found in this API version.");
            }

            if (!string.Equals(baseRevision.Status, RevisionStatusValues.Approved, StringComparison.OrdinalIgnoreCase))
            {
                throw ApiContractException.UnprocessableEntity(
                    messageAr: $"لا يمكن إنشاء نسخة عمل إلا انطلاقًا من مراجعة معتمدة (APPROVED)، الحالة الحالية: {baseRevision.Status}.",
                    messageEn: $"Working copies can only branch from an APPROVED revision (current status: {baseRevision.Status}).");
            }

            var changeRequestId = request!.ChangeRequestId!.Value;
            changeRequest = await dbContext.ChangeRequests.AsNoTracking()
                .FirstOrDefaultAsync(cr => cr.Id == changeRequestId && cr.IntegrationId == integration.Id, cancellationToken);

            if (changeRequest is null)
            {
                throw ApiContractException.NotFound(
                    messageAr: "طلب التغيير المحدد غير موجود لهذا التكامل.",
                    messageEn: "Specified change request was not found for this integration.");
            }

            if (changeRequest.SourceRevisionId != baseRevision.Id)
            {
                throw ApiContractException.UnprocessableEntity(
                    messageAr: "طلب التغيير المحدد لا يرتبط بالمراجعة المرجعية المختارة.",
                    messageEn: "The specified change request does not target the selected base revision.");
            }

            if (!string.Equals(changeRequest.Status, "APPROVED", StringComparison.OrdinalIgnoreCase))
            {
                throw ApiContractException.UnprocessableEntity(
                    messageAr: $"لا يمكن فتح نسخة عمل قبل اعتماد طلب التغيير؛ الحالة الحالية للطلب: {changeRequest.Status}.",
                    messageEn: $"Change request must be APPROVED before creating a working copy (current status: {changeRequest.Status}).");
            }
        }

        var maxNum = await dbContext.DocumentRevisions.AsNoTracking()
            .Where(r => r.ApiVersionId == version.Id)
            .Select(r => (int?)r.RevisionNumber)
            .MaxAsync(cancellationToken) ?? 0;

        var now = DateTimeOffset.UtcNow;
        var revision = new DocumentRevisionEntity
        {
            Id = Guid.NewGuid(),
            ApiVersionId = version.Id,
            RevisionNumber = maxNum + 1,
            BaseRevisionId = baseRevision?.Id,
            ChangeRequestId = changeRequest?.Id,
            Status = RevisionStatusValues.Draft,
            CreatedBy = access.Actor.Id,
            CreatedAt = now,
            ApprovedAt = null
        };

        List<RevisionFileRequirementEntity> targetRequirements;
        List<RevisionFileEntity> carriedFiles = [];

        if (baseRevision is not null)
        {
            var baseReqs = await dbContext.RevisionFileRequirements.AsNoTracking()
                .Where(rr => rr.RevisionId == baseRevision.Id)
                .ToListAsync(cancellationToken);

            if (baseReqs.Count > 0)
            {
                targetRequirements = baseReqs.Select(rr => new RevisionFileRequirementEntity
                {
                    Id = Guid.NewGuid(),
                    RevisionId = revision.Id,
                    FileTypeId = rr.FileTypeId,
                    RequirementMode = rr.RequirementMode,
                    MinCount = rr.MinCount,
                    MaxCount = rr.MaxCount
                }).ToList();
            }
            else
            {
                targetRequirements = await BuildRequirementSnapshotFromTemplateAsync(
                    revision.Id, access.Document.RequirementTemplateId, cancellationToken);
            }

            var baseFiles = await dbContext.RevisionFiles.AsNoTracking()
                .Where(rf => rf.RevisionId == baseRevision.Id)
                .ToListAsync(cancellationToken);

            carriedFiles = baseFiles.Select(bf => new RevisionFileEntity
            {
                Id = Guid.NewGuid(),
                RevisionId = revision.Id,
                FileTypeId = bf.FileTypeId,
                ObjectId = bf.ObjectId,
                OriginalName = bf.OriginalName,
                SourceRevisionFileId = bf.Id,
                UploadedBy = access.Actor.Id,
                UploadedAt = now
            }).ToList();
        }
        else
        {
            targetRequirements = await BuildRequirementSnapshotFromTemplateAsync(
                revision.Id, access.Document.RequirementTemplateId, cancellationToken);
        }

        try
        {
            await using var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            dbContext.DocumentRevisions.Add(revision);
            dbContext.RevisionFileRequirements.AddRange(targetRequirements);
            if (carriedFiles.Count > 0)
            {
                dbContext.RevisionFiles.AddRange(carriedFiles);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException dbEx) when (dbEx.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            dbContext.ChangeTracker.Clear();

            await serverAuthorization.WriteAuditEventAsync(
                access.Actor.Id,
                "REVISION_WORKING_COPY_CONFLICT",
                "API_VERSION",
                version.Id,
                "CONFLICT",
                new { integrationId = integration.Id, versionId = version.Id, request?.BaseRevisionId, request?.ChangeRequestId },
                cancellationToken);

            throw ApiContractException.Conflict(
                messageAr: "تم رصد تعارض تزامن: توجد نسخة عمل نشطة بالفعل لهذا الإصدار أو طلب التغيير (OP-05).",
                messageEn: "Concurrency conflict: an active working copy already exists for this API version, base revision, or change request (OP-05).");
        }

        await serverAuthorization.WriteAuditEventAsync(
            access.Actor.Id,
            isWorkingCopyMode ? "DOCUMENT_WORKING_COPY_CREATED" : "DOCUMENT_REVISION_CREATED",
            "DOCUMENT_REVISION",
            revision.Id,
            "SUCCESS",
            new
            {
                integrationId = integration.Id,
                apiVersionId = version.Id,
                version.VersionLabel,
                revision.RevisionNumber,
                revision.BaseRevisionId,
                revision.ChangeRequestId,
                carriedForwardFilesCount = carriedFiles.Count
            },
            cancellationToken);

        var dtos = await LoadRevisionDtosAsync(version.Id, traceId, cancellationToken);
        return dtos.First(d => d.Id == revision.Id);
    }

    public async Task<RevisionDetailsResponse> GetRevisionDetailsAsync(
        ClaimsPrincipal actorPrincipal,
        Guid revisionId,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var (access, integration, version, _) = await ResolveAuthorizedRevisionAsync(
            actorPrincipal,
            revisionId,
            DocumentPermissionCatalog.View,
            "REVISION_DETAILS_DENIED",
            requireAssignedDeveloper: false,
            cancellationToken: cancellationToken);

        var (derivedTitle, _, _, _) = await LoadDocumentPartyLabelsAsync(access.Document, cancellationToken);
        var dtos = await LoadRevisionDtosAsync(version.Id, traceId, cancellationToken);
        var revDto = dtos.First(r => r.Id == revisionId);
        var files = await LoadRevisionFileDtosAsync(revisionId, traceId, cancellationToken);
        var wordDerivatives = await LoadWordDerivativeDtosForRevisionAsync(revisionId, traceId, cancellationToken);

        var isAssignedDev = await documentAuthorization.IsUserAssignedDeveloperAsync(
            access.Actor.Id, integration.DocumentId, integration.Id, cancellationToken);
        var notArchived = !string.Equals(access.Document.State, "ARCHIVED", StringComparison.OrdinalIgnoreCase);

        var hasFilesUpload = await serverAuthorization.HasGeneralPermissionAsync(actorPrincipal, "files.upload", cancellationToken);
        var hasFilesDelete = await serverAuthorization.HasGeneralPermissionAsync(actorPrincipal, "files.delete", cancellationToken);
        var hasFilesDownload = await serverAuthorization.HasGeneralPermissionAsync(actorPrincipal, "files.download", cancellationToken);
        var hasRevisionsCreate = await serverAuthorization.HasGeneralPermissionAsync(actorPrincipal, "revisions.create", cancellationToken);
        var hasRevisionsSubmit = await serverAuthorization.HasGeneralPermissionAsync(actorPrincipal, "revisions.submit", cancellationToken);

        var hasDocFilesManage = access.EffectivePermissions.TryGetValue(DocumentPermissionCatalog.FilesManage, out var fm) && fm.Allowed;
        var hasDocEdit = access.EffectivePermissions.TryGetValue(DocumentPermissionCatalog.Edit, out var de) && de.Allowed;

        return new RevisionDetailsResponse(
            Revision: revDto,
            IntegrationId: integration.Id,
            DocumentId: integration.DocumentId,
            ReferenceCode: integration.ReferenceCode,
            DerivedTitle: derivedTitle,
            CanSubmit: hasRevisionsSubmit && hasDocEdit && isAssignedDev && notArchived && !revDto.IsProtected,
            CanCreateWorkingCopy: hasRevisionsCreate && hasDocEdit && isAssignedDev && notArchived,
            CanUploadFiles: hasFilesUpload && hasDocFilesManage && isAssignedDev && notArchived && !revDto.IsProtected,
            CanDeleteFiles: hasFilesDelete && hasDocFilesManage && isAssignedDev && notArchived && !revDto.IsProtected,
            CanDownloadFiles: hasFilesDownload,
            Files: files,
            WordDerivatives: wordDerivatives,
            TraceId: traceId);
    }

    public async Task<SubmitRevisionResponse> SubmitRevisionAsync(
        ClaimsPrincipal actorPrincipal,
        Guid revisionId,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var (access, integration, version, revision) = await ResolveAuthorizedRevisionAsync(
            actorPrincipal,
            revisionId,
            DocumentPermissionCatalog.Edit,
            "REVISION_SUBMIT_DENIED",
            requireAssignedDeveloper: true,
            cancellationToken: cancellationToken);

        if (string.Equals(access.Document.State, "ARCHIVED", StringComparison.OrdinalIgnoreCase))
        {
            throw ApiContractException.UnprocessableEntity(
                messageAr: "لا يمكن تقديم مراجعة لوثيقة مؤرشفة.",
                messageEn: "Cannot submit a documentation revision for an archived API document.");
        }

        if (!string.Equals(revision.Status, RevisionStatusValues.Draft, StringComparison.OrdinalIgnoreCase))
        {
            await serverAuthorization.WriteAuditEventAsync(
                access.Actor.Id,
                "REVISION_IMMUTABILITY_VIOLATION",
                "DOCUMENT_REVISION",
                revision.Id,
                "DENIED",
                new { integrationId = integration.Id, revisionId = revision.Id, currentStatus = revision.Status, attemptedAction = "SUBMIT" },
                cancellationToken);

            throw ApiContractException.UnprocessableEntity(
                messageAr: $"لا يمكن تقديم هذه المراجعة لأنها محمية في حالة ({revision.Status}) وليست مسودة (DRAFT).",
                messageEn: $"Cannot submit revision because it is protected in '{revision.Status}' status and not a DRAFT.");
        }

        var precheckDtos = await LoadRevisionDtosAsync(version.Id, traceId, cancellationToken);
        var revDto = precheckDtos.First(r => r.Id == revision.Id);

        if (!revDto.IsReadyForSubmission)
        {
            await serverAuthorization.WriteAuditEventAsync(
                access.Actor.Id,
                "REVISION_SUBMIT_BLOCKED",
                "DOCUMENT_REVISION",
                revision.Id,
                "DENIED",
                new
                {
                    integrationId = integration.Id,
                    revisionId = revision.Id,
                    revDto.MissingRequiredTypesCount,
                    revDto.PendingScanCount,
                    revDto.SubmissionBlockersEn
                },
                cancellationToken);

            throw ApiContractException.UnprocessableEntity(
                messageAr: string.Join(" ", revDto.SubmissionBlockersAr),
                messageEn: string.Join(" ", revDto.SubmissionBlockersEn),
                details: new
                {
                    missingRequiredTypesCount = revDto.MissingRequiredTypesCount,
                    pendingScanCount = revDto.PendingScanCount,
                    blockersAr = revDto.SubmissionBlockersAr,
                    blockersEn = revDto.SubmissionBlockersEn
                });
        }

        var updatedRows = await dbContext.DocumentRevisions
            .Where(r => r.Id == revision.Id && r.Status == RevisionStatusValues.Draft)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, RevisionStatusValues.Submitted), cancellationToken);

        if (updatedRows == 0)
        {
            throw ApiContractException.Conflict(
                messageAr: "تم تعديل حالة هذه المراجعة بالتزامن من طلب آخر؛ يرجى التحديث وإعادة التحقق.",
                messageEn: "Revision status was modified concurrently by another request.");
        }

        dbContext.ReleaseHistoryEvents.Add(new ReleaseHistoryEventEntity
        {
            Id = Guid.NewGuid(),
            RevisionId = revision.Id,
            EventType = "REVISION_SUBMITTED",
            ActorUserId = access.Actor.Id,
            ChangeRequestId = revision.ChangeRequestId,
            RecordedAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        await serverAuthorization.WriteAuditEventAsync(
            access.Actor.Id,
            "REVISION_SUBMITTED",
            "DOCUMENT_REVISION",
            revision.Id,
            "SUCCESS",
            new
            {
                integrationId = integration.Id,
                apiVersionId = version.Id,
                version.VersionLabel,
                revision.RevisionNumber,
                cleanFilesCount = revDto.FilesCount
            },
            cancellationToken);

        var updatedDtos = await LoadRevisionDtosAsync(version.Id, traceId, cancellationToken);
        var updatedDto = updatedDtos.First(r => r.Id == revision.Id);
        return new SubmitRevisionResponse(updatedDto, revDto.FilesCount, traceId);
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

        if (!string.Equals(revision.Status, RevisionStatusValues.Draft, StringComparison.OrdinalIgnoreCase))
        {
            await serverAuthorization.WriteAuditEventAsync(
                access.Actor.Id,
                "REVISION_IMMUTABILITY_VIOLATION",
                "DOCUMENT_REVISION",
                revision.Id,
                "DENIED",
                new { integrationId = integration.Id, revisionId = revision.Id, currentStatus = revision.Status, attemptedAction = "UPLOAD_FILE" },
                cancellationToken);

            throw ApiContractException.UnprocessableEntity(
                messageAr: $"ممنوع (V06): هذه المراجعة محمية في حالة ({revision.Status}) ولا يمكن رفع ملفات إليها دون طلب تغيير معتمد ونسخة عمل جديدة.",
                messageEn: $"Protected (V06): files cannot be uploaded to a revision in '{revision.Status}' status; create an approved working copy instead.");
        }

        if (string.Equals(access.Document.State, "ARCHIVED", StringComparison.OrdinalIgnoreCase))
        {
            throw ApiContractException.UnprocessableEntity(
                messageAr: "لا يمكن رفع ملفات لوثيقة مؤرشفة.",
                messageEn: "Cannot upload files to an archived API document.");
        }

        // V06 / PRD-04: If this draft is a Change Request working copy, enforce change-request file scope
        await EnforceChangeRequestFileTypeScopeAsync(
            access.Actor.Id, integration.Id, revision, fileTypeId, "UPLOAD_FILE", cancellationToken);

        var fileType = await dbContext.FileTypes.AsNoTracking()
            .FirstOrDefaultAsync(ft => ft.Id == fileTypeId && ft.IsActive, cancellationToken);
        if (fileType is null)
        {
            throw ApiContractException.UnprocessableEntity(
                messageAr: "نوع الملف المحدد غير موجود أو غير مفعّل في النظام.",
                messageEn: "The selected file type does not exist or is inactive.");
        }

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

        if (!string.Equals(revision.Status, RevisionStatusValues.Draft, StringComparison.OrdinalIgnoreCase))
        {
            await serverAuthorization.WriteAuditEventAsync(
                access.Actor.Id,
                "REVISION_IMMUTABILITY_VIOLATION",
                "DOCUMENT_REVISION",
                revision.Id,
                "DENIED",
                new { integrationId = integration.Id, revisionId = revision.Id, currentStatus = revision.Status, attemptedAction = "RESCAN_FILE" },
                cancellationToken);

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

        if (!string.Equals(revision.Status, RevisionStatusValues.Draft, StringComparison.OrdinalIgnoreCase))
        {
            await serverAuthorization.WriteAuditEventAsync(
                access.Actor.Id,
                "REVISION_IMMUTABILITY_VIOLATION",
                "DOCUMENT_REVISION",
                revision.Id,
                "DENIED",
                new { integrationId = integration.Id, revisionId = revision.Id, currentStatus = revision.Status, attemptedAction = "DELETE_FILE", revisionFileId },
                cancellationToken);

            throw ApiContractException.UnprocessableEntity(
                messageAr: $"ممنوع (V06): يُمنع حذف ملفات من مراجعة محمية في حالة ({revision.Status}).",
                messageEn: $"Protected (V06): cannot remove files from a revision in '{revision.Status}' status.");
        }

        var revFile = await dbContext.RevisionFiles
            .FirstOrDefaultAsync(rf => rf.Id == revisionFileId && rf.RevisionId == revision.Id, cancellationToken);
        if (revFile is null)
        {
            throw ApiContractException.NotFound(
                messageAr: "ملف المراجعة المطلوب غير موجود.",
                messageEn: "Revision file was not found.");
        }

        await EnforceChangeRequestFileScopeOnDeleteAsync(
            access.Actor.Id, integration.Id, revision, revFile, cancellationToken);

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

        Stream stream;
        long sizeBytes;
        try
        {
            (stream, sizeBytes) = await privateFileStore.OpenVerifiedImmutableObjectReadStreamAsync(
                fileObject.StorageKey,
                fileObject.Sha256,
                cancellationToken);
        }
        catch (Exception ex) when (ex is ApiContractException or InvalidOperationException)
        {
            await serverAuthorization.WriteAuditEventAsync(
                access.Actor.Id,
                "FILE_DOWNLOAD_INTEGRITY_FAILED",
                "REVISION_FILE",
                revFile.Id,
                "DENIED",
                new { integrationId = integration.Id, revisionId, objectId = fileObject.Id, expectedSha256 = fileObject.Sha256, reason = ex.Message },
                cancellationToken);
            throw;
        }

        await serverAuthorization.WriteAuditEventAsync(
            access.Actor.Id,
            "FILE_DOWNLOADED",
            "REVISION_FILE",
            revFile.Id,
            "SUCCESS",
            new { integrationId = integration.Id, revisionId, objectId = fileObject.Id, revFile.OriginalName, fileObject.Sha256, sizeBytes },
            cancellationToken);

        return (stream, revFile.OriginalName, fileObject.MediaType ?? "application/octet-stream", sizeBytes, fileObject.Sha256);
    }

    public async Task<WordDerivativeRecordDto> GenerateWordDerivativeAsync(
        ClaimsPrincipal actorPrincipal,
        Guid revisionId,
        Guid revisionFileId,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var (access, integration, version, revision) = await ResolveAuthorizedRevisionAsync(
            actorPrincipal,
            revisionId,
            DocumentPermissionCatalog.FilesManage,
            "WORD_DERIVATIVE_GENERATE_DENIED",
            requireAssignedDeveloper: true,
            cancellationToken: cancellationToken);

        if (string.Equals(access.Document.State, "ARCHIVED", StringComparison.OrdinalIgnoreCase))
        {
            throw ApiContractException.UnprocessableEntity(
                messageAr: "لا يمكن توليد مشتق Word لوثيقة مؤرشفة.",
                messageEn: "Cannot generate Word derivatives for an archived API document.");
        }

        var revFile = await dbContext.RevisionFiles.AsNoTracking()
            .FirstOrDefaultAsync(rf => rf.Id == revisionFileId && rf.RevisionId == revision.Id, cancellationToken);
        if (revFile is null)
        {
            throw ApiContractException.NotFound(
                messageAr: "ملف المراجعة المطلوب غير موجود.",
                messageEn: "Revision file was not found.");
        }

        if (!revFile.OriginalName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
        {
            throw ApiContractException.UnprocessableEntity(
                messageAr: "إثبات جدوى تحديث الحقول ينطبق فقط على ملفات Word بصيغة (.docx).",
                messageEn: "Word governance derivative generation applies only to .docx files.");
        }

        var sourceObject = await dbContext.FileObjects.AsNoTracking()
            .FirstOrDefaultAsync(fo => fo.Id == revFile.ObjectId, cancellationToken);
        if (sourceObject is null || !string.Equals(sourceObject.ScanStatus, FileScanStatusValues.Clean, StringComparison.OrdinalIgnoreCase))
        {
            throw ApiContractException.UnprocessableEntity(
                messageAr: "يجب أن يكون ملف Word الأصلي سليمًا (CLEAN) ومتاحًا في المخزن الدائم قبل توليد النسخة المشتقة.",
                messageEn: "Original .docx file must be CLEAN in immutable storage before generating a derived copy.");
        }

        var (derivedTitle, providerName, consumerName, apiGroupName) =
            await LoadDocumentPartyLabelsAsync(access.Document, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var metadata = new WordGovernanceMetadataContext(
            DocumentTitle: derivedTitle,
            IntegrationCode: integration.ReferenceCode,
            ApiGroupName: apiGroupName,
            ProviderName: providerName,
            ConsumerName: consumerName,
            VersionLabel: version.VersionLabel,
            RevisionNumber: revision.RevisionNumber,
            RevisionStatus: revision.Status,
            SourceSha256: sourceObject.Sha256,
            GeneratedAtUtc: now);

        var stagedDerivedPath = privateFileStore.CreateStagingFilePath(".docx");
        WordDerivativeGenerationOutcome outcome;
        try
        {
            await using var sourceStream = (await privateFileStore.OpenVerifiedImmutableObjectReadStreamAsync(
                sourceObject.StorageKey, sourceObject.Sha256, cancellationToken)).Stream;

            outcome = await wordDerivativeService.TryGenerateDerivedCopyAsync(
                sourceStream, stagedDerivedPath, metadata, cancellationToken);
        }
        catch
        {
            await privateFileStore.DeleteFileIfExistsAsync(stagedDerivedPath, CancellationToken.None);
            throw;
        }

        var recordId = Guid.NewGuid();
        Guid? derivedObjectId = null;
        string finalStatus;
        bool isPublishable = false;
        string? errorMsg = outcome.ErrorMessage;
        var validationErrors = outcome.ValidationErrors.ToList();

        if (!outcome.PackageValid)
        {
            await privateFileStore.DeleteFileIfExistsAsync(stagedDerivedPath, CancellationToken.None);
            finalStatus = WordDerivativeStatusValues.Failed;
        }
        else if (outcome.MatchedFields.Count == 0)
        {
            await privateFileStore.DeleteFileIfExistsAsync(stagedDerivedPath, CancellationToken.None);
            finalStatus = WordDerivativeStatusValues.NoRecognizedFields;
            errorMsg = "لم يتم العثور على حقول أو علامات حوكمة معرّفة بثقة داخل ملف Word (المحتوى الأصلي محفوظ دون تغيير).";
        }
        else
        {
            // Verify derived .docx through V02 content validator & V03 fail-closed AV scanner before allowing publishing (OP-03)
            var contentCheck = await contentValidator.ValidateStagedContentAsync(
                stagedDerivedPath,
                $"derived-{revFile.OriginalName}",
                ".docx",
                "WORD_DOC",
                cancellationToken);

            if (!contentCheck.IsValid)
            {
                await privateFileStore.DeleteFileIfExistsAsync(stagedDerivedPath, CancellationToken.None);
                finalStatus = WordDerivativeStatusValues.Failed;
                validationErrors.Add(contentCheck.MessageEn ?? "Derived .docx failed content safety validation.");
                errorMsg = contentCheck.MessageAr;
            }
            else
            {
                var derivedSha = await privateFileStore.ComputeFileSha256Async(stagedDerivedPath, cancellationToken);
                var derivedSize = new FileInfo(stagedDerivedPath).Length;
                var scanVerdict = await fileScanner.ScanStagedFileAsync(
                    stagedDerivedPath, $"derived-{revFile.OriginalName}", cancellationToken);

                var newObjId = Guid.NewGuid();
                if (string.Equals(scanVerdict.ScanStatus, FileScanStatusValues.Clean, StringComparison.OrdinalIgnoreCase))
                {
                    var key = await privateFileStore.CommitImmutableObjectAsync(
                        stagedDerivedPath, newObjId, derivedSha, cancellationToken);

                    dbContext.FileObjects.Add(new FileObjectEntity
                    {
                        Id = newObjId,
                        StorageKey = key,
                        Sha256 = derivedSha,
                        SizeBytes = derivedSize,
                        MediaType = contentCheck.CanonicalMediaType,
                        ScanStatus = FileScanStatusValues.Clean,
                        CreatedAt = now
                    });
                    derivedObjectId = newObjId;
                    finalStatus = WordDerivativeStatusValues.Verified;
                    isPublishable = true;
                }
                else if (string.Equals(scanVerdict.ScanStatus, FileScanStatusValues.PendingScan, StringComparison.OrdinalIgnoreCase))
                {
                    var qKey = await privateFileStore.MoveToQuarantineAsync(stagedDerivedPath, newObjId, cancellationToken);
                    dbContext.FileObjects.Add(new FileObjectEntity
                    {
                        Id = newObjId,
                        StorageKey = qKey,
                        Sha256 = derivedSha,
                        SizeBytes = derivedSize,
                        MediaType = contentCheck.CanonicalMediaType,
                        ScanStatus = FileScanStatusValues.PendingScan,
                        CreatedAt = now
                    });
                    derivedObjectId = newObjId;
                    finalStatus = WordDerivativeStatusValues.PendingScan;
                    errorMsg = scanVerdict.MessageAr;
                }
                else
                {
                    var qKey = await privateFileStore.MoveToQuarantineAsync(stagedDerivedPath, newObjId, cancellationToken);
                    dbContext.FileObjects.Add(new FileObjectEntity
                    {
                        Id = newObjId,
                        StorageKey = qKey,
                        Sha256 = derivedSha,
                        SizeBytes = derivedSize,
                        MediaType = contentCheck.CanonicalMediaType,
                        ScanStatus = FileScanStatusValues.Infected,
                        CreatedAt = now
                    });
                    derivedObjectId = newObjId;
                    finalStatus = WordDerivativeStatusValues.Failed;
                    validationErrors.Add(scanVerdict.MessageEn);
                    errorMsg = scanVerdict.MessageAr;
                }
            }
        }

        var record = new WordDerivativeRecordEntity
        {
            Id = recordId,
            RevisionId = revision.Id,
            RevisionFileId = revFile.Id,
            SourceObjectId = sourceObject.Id,
            DerivedObjectId = derivedObjectId,
            Status = finalStatus,
            TemplateKindDetected = outcome.TemplateKindDetected,
            FieldsUpdatedCount = outcome.MatchedFields.Count,
            MatchedFieldsJson = JsonSerializer.Serialize(outcome.MatchedFields, JsonOpts),
            ValidationErrorsJson = JsonSerializer.Serialize(validationErrors, JsonOpts),
            ErrorMessage = errorMsg,
            IsPublishable = isPublishable,
            CreatedBy = access.Actor.Id,
            CreatedAt = now
        };

        dbContext.WordDerivativeRecords.Add(record);
        await dbContext.SaveChangesAsync(cancellationToken);

        await serverAuthorization.WriteAuditEventAsync(
            access.Actor.Id,
            isPublishable ? "WORD_DERIVATIVE_VERIFIED" : $"WORD_DERIVATIVE_{finalStatus}",
            "REVISION_FILE",
            revFile.Id,
            isPublishable ? "SUCCESS" : (finalStatus == WordDerivativeStatusValues.PendingScan ? "PENDING" : "FAILED"),
            new
            {
                integrationId = integration.Id,
                revisionId = revision.Id,
                sourceObjectId = sourceObject.Id,
                derivedObjectId,
                finalStatus,
                outcome.TemplateKindDetected,
                fieldsUpdatedCount = outcome.MatchedFields.Count,
                isPublishable,
                errorMsg
            },
            cancellationToken);

        var records = await LoadWordDerivativeDtosForRevisionAsync(revision.Id, traceId, cancellationToken);
        return records.First(r => r.Id == record.Id);
    }

    public async Task<WordDerivativeRecordDto?> GetLatestWordDerivativeAsync(
        ClaimsPrincipal actorPrincipal,
        Guid revisionId,
        Guid revisionFileId,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        await ResolveAuthorizedRevisionAsync(
            actorPrincipal,
            revisionId,
            DocumentPermissionCatalog.View,
            "WORD_DERIVATIVE_READ_DENIED",
            requireAssignedDeveloper: false,
            cancellationToken: cancellationToken);

        var revFileExists = await dbContext.RevisionFiles.AsNoTracking()
            .AnyAsync(rf => rf.Id == revisionFileId && rf.RevisionId == revisionId, cancellationToken);
        if (!revFileExists)
        {
            throw ApiContractException.NotFound(
                messageAr: "ملف المراجعة المطلوب غير موجود ضمن هذه المراجعة.",
                messageEn: "Revision file was not found in the specified revision.");
        }

        var allForRev = await LoadWordDerivativeDtosForRevisionAsync(revisionId, traceId, cancellationToken);
        return allForRev.FirstOrDefault(w => w.RevisionFileId == revisionFileId);
    }

    public async Task<(Stream ContentStream, string FileName, string ContentType, long SizeBytes, string Sha256)> DownloadVerifiedWordDerivativeAsync(
        ClaimsPrincipal actorPrincipal,
        Guid revisionId,
        Guid revisionFileId,
        CancellationToken cancellationToken = default)
    {
        var (access, integration, _, _) = await ResolveAuthorizedRevisionAsync(
            actorPrincipal,
            revisionId,
            DocumentPermissionCatalog.View,
            "WORD_DERIVATIVE_DOWNLOAD_DENIED",
            requireAssignedDeveloper: false,
            cancellationToken: cancellationToken);

        var revFile = await dbContext.RevisionFiles.AsNoTracking()
            .FirstOrDefaultAsync(rf => rf.Id == revisionFileId && rf.RevisionId == revisionId, cancellationToken);
        if (revFile is null)
        {
            throw ApiContractException.NotFound(
                messageAr: "ملف المراجعة المطلوب غير موجود ضمن هذه المراجعة.",
                messageEn: "Revision file was not found in the specified revision.");
        }

        var latestRecord = await dbContext.WordDerivativeRecords.AsNoTracking()
            .Where(w => w.RevisionId == revisionId && w.RevisionFileId == revisionFileId)
            .OrderByDescending(w => w.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (latestRecord is null)
        {
            throw ApiContractException.NotFound(
                messageAr: "لم يتم توليد نسخة مشتقة لملف Word هذا بعد.",
                messageEn: "No Word derivative record exists for this file yet.");
        }

        if (!latestRecord.IsPublishable ||
            !string.Equals(latestRecord.Status, WordDerivativeStatusValues.Verified, StringComparison.OrdinalIgnoreCase) ||
            !latestRecord.DerivedObjectId.HasValue)
        {
            await serverAuthorization.WriteAuditEventAsync(
                access.Actor.Id,
                "WORD_DERIVATIVE_DOWNLOAD_BLOCKED",
                "REVISION_FILE",
                revFile.Id,
                "DENIED",
                new { integrationId = integration.Id, revisionId, latestRecord.Status, latestRecord.IsPublishable },
                cancellationToken);

            throw ApiContractException.UnprocessableEntity(
                messageAr: $"ممنوع (OP-03): لا يمكن تنزيل أو نشر مشتق Word غير متحقق (الحالة: {latestRecord.Status}).",
                messageEn: $"Blocked (OP-03): cannot download or publish an unverified Word derivative (status: {latestRecord.Status}).");
        }

        var derivedObj = await dbContext.FileObjects.AsNoTracking()
            .FirstOrDefaultAsync(fo => fo.Id == latestRecord.DerivedObjectId.Value, cancellationToken);
        if (derivedObj is null || !string.Equals(derivedObj.ScanStatus, FileScanStatusValues.Clean, StringComparison.OrdinalIgnoreCase))
        {
            throw ApiContractException.UnprocessableEntity(
                messageAr: "كائن الملف المشتق غير متاح أو لم يجتز الفحص الأمني.",
                messageEn: "Derived Word file object is unavailable or has not passed security scanning.");
        }

        var (stream, sizeBytes) = await privateFileStore.OpenVerifiedImmutableObjectReadStreamAsync(
            derivedObj.StorageKey, derivedObj.Sha256, cancellationToken);

        var derivedName = $"derived-{revFile.OriginalName}";

        await serverAuthorization.WriteAuditEventAsync(
            access.Actor.Id,
            "WORD_DERIVATIVE_DOWNLOADED",
            "REVISION_FILE",
            revFile.Id,
            "SUCCESS",
            new { integrationId = integration.Id, revisionId, derivedObjectId = derivedObj.Id, derivedName, derivedObj.Sha256, sizeBytes },
            cancellationToken);

        return (stream, derivedName, derivedObj.MediaType ?? "application/vnd.openxmlformats-officedocument.wordprocessingml.document", sizeBytes, derivedObj.Sha256);
    }

    private async Task EnforceChangeRequestFileTypeScopeAsync(
        Guid actorId,
        Guid integrationId,
        DocumentRevisionEntity revision,
        Guid attemptedFileTypeId,
        string actionName,
        CancellationToken cancellationToken)
    {
        if (!revision.ChangeRequestId.HasValue)
        {
            return;
        }

        var allowedFileTypeIds = await (
            from crf in dbContext.ChangeRequestFiles.AsNoTracking()
            join srf in dbContext.RevisionFiles.AsNoTracking() on crf.SourceRevisionFileId equals srf.Id
            where crf.ChangeRequestId == revision.ChangeRequestId.Value
            select srf.FileTypeId)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (allowedFileTypeIds.Count > 0 && !allowedFileTypeIds.Contains(attemptedFileTypeId))
        {
            await serverAuthorization.WriteAuditEventAsync(
                actorId,
                "CHANGE_SCOPE_VIOLATION",
                "DOCUMENT_REVISION",
                revision.Id,
                "DENIED",
                new { integrationId, revisionId = revision.Id, changeRequestId = revision.ChangeRequestId, attemptedFileTypeId, actionName },
                cancellationToken);

            throw ApiContractException.UnprocessableEntity(
                messageAr: "ممنوع: مسموح في نسخة العمل هذه تعديل أو رفع الملفات المشمولة في نطاق طلب التغيير المعتمد فقط (PRD-04).",
                messageEn: "Blocked: only file types included in the approved change request scope may be modified in this working copy (PRD-04).");
        }
    }

    private async Task EnforceChangeRequestFileScopeOnDeleteAsync(
        Guid actorId,
        Guid integrationId,
        DocumentRevisionEntity revision,
        RevisionFileEntity targetFile,
        CancellationToken cancellationToken)
    {
        if (!revision.ChangeRequestId.HasValue)
        {
            return;
        }

        var affectedSourceIds = await dbContext.ChangeRequestFiles.AsNoTracking()
            .Where(crf => crf.ChangeRequestId == revision.ChangeRequestId.Value)
            .Select(crf => crf.SourceRevisionFileId)
            .ToListAsync(cancellationToken);

        if (affectedSourceIds.Count == 0)
        {
            return;
        }

        if (targetFile.SourceRevisionFileId.HasValue && !affectedSourceIds.Contains(targetFile.SourceRevisionFileId.Value))
        {
            await serverAuthorization.WriteAuditEventAsync(
                actorId,
                "CHANGE_SCOPE_VIOLATION",
                "REVISION_FILE",
                targetFile.Id,
                "DENIED",
                new { integrationId, revisionId = revision.Id, changeRequestId = revision.ChangeRequestId, targetFile.SourceRevisionFileId },
                cancellationToken);

            throw ApiContractException.UnprocessableEntity(
                messageAr: "ممنوع: لا يمكن حذف ملف لم يُعتمد تغييره ضمن نطاق طلب التغيير (PRD-04).",
                messageEn: "Blocked: cannot remove a carried-forward file that is outside the approved change request scope (PRD-04).");
        }

        await EnforceChangeRequestFileTypeScopeAsync(
            actorId, integrationId, revision, targetFile.FileTypeId, "DELETE_FILE", cancellationToken);
    }

    private async Task<List<RevisionFileRequirementEntity>> BuildRequirementSnapshotFromTemplateAsync(
        Guid revisionId,
        Guid templateId,
        CancellationToken cancellationToken)
    {
        var templateRules = await dbContext.FileRequirementRules.AsNoTracking()
            .Where(r => r.TemplateId == templateId)
            .ToListAsync(cancellationToken);

        return templateRules.Select(rule => new RevisionFileRequirementEntity
        {
            Id = Guid.NewGuid(),
            RevisionId = revisionId,
            FileTypeId = rule.FileTypeId,
            RequirementMode = rule.RequirementMode,
            MinCount = rule.MinCount,
            MaxCount = rule.MaxCount
        }).ToList();
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
            var pendingCount = myFiles.Count(f => string.Equals(f.ScanStatus, FileScanStatusValues.PendingScan, StringComparison.OrdinalIgnoreCase));
            var cleanCount = myFiles.Count(f => string.Equals(f.ScanStatus, FileScanStatusValues.Clean, StringComparison.OrdinalIgnoreCase));
            var nonCleanCount = myFiles.Count(f => !string.Equals(f.ScanStatus, FileScanStatusValues.Clean, StringComparison.OrdinalIgnoreCase));

            var blockersAr = new List<string>();
            var blockersEn = new List<string>();
            var missingRequiredTypes = 0;

            var reqProgress = myReqs
                .OrderBy(r => fileTypes.TryGetValue(r.FileTypeId, out var ft) ? ft.DisplayName : string.Empty)
                .Select(r =>
                {
                    fileTypes.TryGetValue(r.FileTypeId, out var ft);
                    var typeName = ft?.DisplayName ?? r.FileTypeId.ToString();
                    var matchingFiles = myFiles.Where(f => f.FileTypeId == r.FileTypeId).ToList();
                    var typeClean = matchingFiles.Count(f => string.Equals(f.ScanStatus, FileScanStatusValues.Clean, StringComparison.OrdinalIgnoreCase));
                    var typePending = matchingFiles.Count(f => string.Equals(f.ScanStatus, FileScanStatusValues.PendingScan, StringComparison.OrdinalIgnoreCase));

                    var isRequiredRule = string.Equals(r.RequirementMode, "REQUIRED", StringComparison.OrdinalIgnoreCase) || r.MinCount > 0;
                    var effectiveMin = isRequiredRule ? Math.Max(1, r.MinCount) : 0;
                    var missingCount = Math.Max(0, effectiveMin - typeClean);
                    var exceedsMax = r.MaxCount > 0 && typeClean > r.MaxCount;
                    var satisfied = missingCount == 0 && !exceedsMax;

                    if (!satisfied)
                    {
                        if (missingCount > 0)
                        {
                            missingRequiredTypes++;
                            blockersAr.Add($"يلزم رفع ({missingCount}) ملف سليم إضافي لنوع الملف الإلزامي ({typeName}).");
                            blockersEn.Add($"Required file type '{typeName}' still needs {missingCount} clean file(s).");
                        }
                        if (exceedsMax)
                        {
                            blockersAr.Add($"عدد ملفات ({typeName}) يتجاوز الحد الأقصى ({r.MaxCount}).");
                            blockersEn.Add($"File type '{typeName}' exceeds maximum count ({r.MaxCount}).");
                        }
                    }

                    return new RevisionRequirementProgressDto(
                        r.FileTypeId,
                        typeName,
                        ft?.CatalogCode ?? string.Empty,
                        exts.GetValueOrDefault(r.FileTypeId, []),
                        ft?.MaxSizeBytes ?? 0,
                        r.RequirementMode,
                        r.MinCount,
                        r.MaxCount,
                        UploadedCleanCount: typeClean,
                        UploadedPendingScanCount: typePending,
                        IsSatisfied: satisfied,
                        MissingRequiredCount: missingCount,
                        ExceedsMaxCount: exceedsMax);
                })
                .ToList();

            if (nonCleanCount > 0)
            {
                blockersAr.Add($"يوجد ({nonCleanCount}) ملف في الحجر الأمني أو بانتظار الفحص؛ لا يمكن التقديم قبل اجتياز الفحص الأمني للجميع.");
                blockersEn.Add($"{nonCleanCount} file(s) are quarantined or pending security scan and must pass scanning before submission.");
            }

            if (cleanCount == 0 && blockersAr.Count == 0)
            {
                blockersAr.Add("يلزم وجود ملف سليم واحد على الأقل داخل حزمة التوثيق قبل التقديم.");
                blockersEn.Add("At least one CLEAN file is required in the documentation package prior to submission.");
            }

            var isProtected = !string.Equals(rev.Status, RevisionStatusValues.Draft, StringComparison.OrdinalIgnoreCase);
            var isReady = !isProtected && blockersAr.Count == 0;

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
                PendingScanCount: pendingCount,
                Requirements: reqProgress,
                TraceId: traceId,
                IsProtected: isProtected,
                IsReadyForSubmission: isReady,
                MissingRequiredTypesCount: missingRequiredTypes,
                SubmissionBlockersAr: blockersAr,
                SubmissionBlockersEn: blockersEn);
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

    private async Task<IReadOnlyList<ApprovedChangeRequestSummaryDto>> LoadApprovedChangeRequestsAsync(
        Guid integrationId,
        CancellationToken cancellationToken)
    {
        var crs = await (
            from cr in dbContext.ChangeRequests.AsNoTracking()
            join ct in dbContext.ChangeTypes.AsNoTracking() on cr.ChangeTypeId equals ct.Id
            where cr.IntegrationId == integrationId && cr.Status == "APPROVED"
            orderby cr.CreatedAt descending
            select new { cr, ct.Name })
            .ToListAsync(cancellationToken);

        if (crs.Count == 0)
        {
            return [];
        }

        var crIds = crs.Select(x => x.cr.Id).ToList();
        var crFiles = await dbContext.ChangeRequestFiles.AsNoTracking()
            .Where(f => crIds.Contains(f.ChangeRequestId))
            .ToListAsync(cancellationToken);

        return crs.Select(x => new ApprovedChangeRequestSummaryDto(
            x.cr.Id,
            x.cr.SourceRevisionId,
            x.cr.ChangeTypeId,
            x.Name,
            x.cr.Reason,
            crFiles.Where(f => f.ChangeRequestId == x.cr.Id).Select(f => f.SourceRevisionFileId).ToList(),
            x.cr.CreatedAt)).ToList();
    }

    private async Task<IReadOnlyList<WordDerivativeRecordDto>> LoadWordDerivativeDtosForRevisionAsync(
        Guid revisionId,
        string traceId,
        CancellationToken cancellationToken)
    {
        var rows = await dbContext.WordDerivativeRecords.AsNoTracking()
            .Where(w => w.RevisionId == revisionId)
            .OrderByDescending(w => w.CreatedAt)
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return [];
        }

        var objIds = rows.SelectMany(r => new[] { (Guid?)r.SourceObjectId, r.DerivedObjectId })
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        var objMap = await dbContext.FileObjects.AsNoTracking()
            .Where(fo => objIds.Contains(fo.Id))
            .ToDictionaryAsync(fo => fo.Id, cancellationToken);

        var userIds = rows.Select(r => r.CreatedBy).Distinct().ToList();
        var userMap = await dbContext.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, cancellationToken);

        return rows.Select(r =>
        {
            objMap.TryGetValue(r.SourceObjectId, out var srcObj);
            FileObjectEntity? derObj = null;
            if (r.DerivedObjectId.HasValue)
            {
                objMap.TryGetValue(r.DerivedObjectId.Value, out derObj);
            }

            var matched = DeserializeList<WordFieldUpdateEntryDto>(r.MatchedFieldsJson);
            var errors = DeserializeList<string>(r.ValidationErrorsJson);

            return new WordDerivativeRecordDto(
                r.Id,
                r.RevisionId,
                r.RevisionFileId,
                r.SourceObjectId,
                srcObj?.Sha256 ?? string.Empty,
                r.DerivedObjectId,
                derObj?.Sha256,
                derObj?.SizeBytes,
                r.Status,
                r.TemplateKindDetected,
                r.FieldsUpdatedCount,
                matched,
                errors,
                r.ErrorMessage,
                r.IsPublishable,
                r.CreatedBy,
                userMap.GetValueOrDefault(r.CreatedBy, string.Empty),
                r.CreatedAt,
                traceId);
        }).ToList();
    }

    private static IReadOnlyList<T> DeserializeList<T>(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<T>>(json, JsonOpts) ?? [];
        }
        catch
        {
            return [];
        }
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
