using System.Security.Claims;
using ApiGovernance.Modules.PublishingPortal.Contracts;
using ApiGovernance.Shared.Contracts;
using ApiGovernance.Shared.Persistence;
using ApiGovernance.Shared.Persistence.Entities;
using ApiGovernance.Shared.Security;
using Microsoft.EntityFrameworkCore;

namespace ApiGovernance.Modules.PublishingPortal.Services;

public interface IPublishingPortalService
{
    Task<IntegrationPublicationsResponse> GetIntegrationPublicationsAsync(
        ClaimsPrincipal actorPrincipal,
        Guid integrationId,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<PublicationDto> PublishRevisionAsync(
        ClaimsPrincipal actorPrincipal,
        Guid revisionId,
        CreatePublicationRequest request,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<PublicationDto> WithdrawPublicationAsync(
        ClaimsPrincipal actorPrincipal,
        Guid publicationId,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<ConsumerVersionResponse> GetConsumerVersionAsync(
        ClaimsPrincipal actorPrincipal,
        Guid integrationId,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<ConsumerVersionResponse> SetConsumerVersionAsync(
        ClaimsPrincipal actorPrincipal,
        Guid integrationId,
        SetConsumerVersionRequest request,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<PortalIntegrationsResponse> GetPortalIntegrationsAsync(
        ClaimsPrincipal actorPrincipal,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<PortalPublicationFilesResponse> GetPortalPublicationFilesAsync(
        ClaimsPrincipal actorPrincipal,
        Guid publicationId,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<(Stream ContentStream, string FileName, string ContentType, long SizeBytes, string Sha256)>
        DownloadPortalPublicationFileAsync(
            ClaimsPrincipal actorPrincipal,
            Guid publicationId,
            Guid revisionFileId,
            CancellationToken cancellationToken = default);
}

public sealed class PublishingPortalService(
    ApiGovernanceDbContext dbContext,
    IServerAuthorizationService serverAuthorization,
    IDocumentAuthorizationService documentAuthorization,
    IPublishedObjectReadAccessor objectReadAccessor) : IPublishingPortalService
{
    public async Task<IntegrationPublicationsResponse> GetIntegrationPublicationsAsync(
        ClaimsPrincipal actorPrincipal,
        Guid integrationId,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var actor = await serverAuthorization.RequireGeneralPermissionAsync(
            actorPrincipal,
            PublishingPermissionCodes.PublicationsView,
            "PUBLICATIONS_LIST_DENIED",
            integrationId,
            "INTEGRATION",
            cancellationToken);

        var (integration, document) = await LoadIntegrationAndDocumentAsync(integrationId, cancellationToken);

        var docAccess = await documentAuthorization.RequireDocumentPermissionAsync(
            actorPrincipal,
            document.Id,
            DocumentPermissionCatalog.View,
            "PUBLICATIONS_LIST_DENIED",
            cancellationToken);

        bool DocAllows(string code) => docAccess.EffectivePermissions.TryGetValue(code, out var decision) && decision.Allowed;

        var canCreatePub = DocAllows(DocumentPermissionCatalog.PublicationCreate) && await serverAuthorization.HasGeneralPermissionAsync(
            actorPrincipal, PublishingPermissionCodes.PublicationsCreate, cancellationToken);
        var canWithdrawPub = DocAllows(DocumentPermissionCatalog.PublicationWithdraw) && await serverAuthorization.HasGeneralPermissionAsync(
            actorPrincipal, PublishingPermissionCodes.PublicationsWithdraw, cancellationToken);
        var canManageUsage = DocAllows(DocumentPermissionCatalog.VersionUsage) && await serverAuthorization.HasGeneralPermissionAsync(
            actorPrincipal, PublishingPermissionCodes.VersionsUsageManage, cancellationToken);

        var (providerName, consumerName, apiGroupName) = await ResolveDocumentNamesAsync(document, cancellationToken);
        var documentTitle = BuildDocumentTitle(providerName, consumerName, apiGroupName);

        var publications = await LoadPublicationsForIntegrationAsync(integrationId, cancellationToken);
        var activeUsage = await LoadActiveConsumerVersionDtoAsync(integrationId, cancellationToken);

        return new IntegrationPublicationsResponse(
            IntegrationId: integration.Id,
            DocumentId: document.Id,
            DocumentTitle: documentTitle,
            PublicationOption: document.PublicationOption,
            Publications: publications,
            ActiveConsumerVersion: activeUsage,
            CanCreatePublication: canCreatePub,
            CanWithdrawPublication: canWithdrawPub,
            CanManageConsumerVersion: canManageUsage,
            TraceId: traceId);
    }

    public async Task<PublicationDto> PublishRevisionAsync(
        ClaimsPrincipal actorPrincipal,
        Guid revisionId,
        CreatePublicationRequest request,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var actor = await serverAuthorization.RequireGeneralPermissionAsync(
            actorPrincipal,
            PublishingPermissionCodes.PublicationsCreate,
            "REVISION_PUBLISH_DENIED",
            revisionId,
            "REVISION",
            cancellationToken);

        var revision = await dbContext.DocumentRevisions
            .FirstOrDefaultAsync(r => r.Id == revisionId, cancellationToken)
            ?? throw ApiContractException.NotFound(
                messageAr: "لم يتم العثور على مراجعة التوثيق المطلوبة.",
                messageEn: "Document revision was not found.");

        var version = await dbContext.ApiVersions
            .FirstOrDefaultAsync(v => v.Id == revision.ApiVersionId, cancellationToken)
            ?? throw ApiContractException.NotFound(
                messageAr: "لم يتم العثور على إصدار API المرتبط.",
                messageEn: "Associated API version was not found.");

        var (integration, document) = await LoadIntegrationAndDocumentAsync(version.IntegrationId, cancellationToken);

        await documentAuthorization.RequireDocumentPermissionAsync(
            actorPrincipal,
            document.Id,
            DocumentPermissionCatalog.PublicationCreate,
            "REVISION_PUBLISH_DENIED",
            cancellationToken);

        if (!string.Equals(revision.Status, "APPROVED", StringComparison.OrdinalIgnoreCase))
        {
            throw ApiContractException.UnprocessableEntity(
                messageAr: $"لا يمكن نشر المراجعة #{revision.RevisionNumber} لأنها بحالة ({revision.Status})؛ يجب أن تكون معتمدة (APPROVED) أولًا.",
                messageEn: $"Cannot publish revision #{revision.RevisionNumber} because its status is '{revision.Status}'; only APPROVED revisions can be published.");
        }

        var selectedIds = (request.RevisionFileIds ?? [])
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        if (selectedIds.Count == 0)
        {
            throw ApiContractException.UnprocessableEntity(
                messageAr: "يجب اختيار ملف واحد سليم على الأقل من المراجعة المعتمدة للنشر.",
                messageEn: "At least one CLEAN file from the approved revision must be selected for publication.");
        }

        var revisionFiles = await dbContext.RevisionFiles
            .AsNoTracking()
            .Where(rf => rf.RevisionId == revisionId && selectedIds.Contains(rf.Id))
            .ToListAsync(cancellationToken);

        if (revisionFiles.Count != selectedIds.Count)
        {
            throw ApiContractException.UnprocessableEntity(
                messageAr: "أحد الملفات المختارة لا ينتمي إلى المراجعة المعتمدة المحددة.",
                messageEn: "One or more selected file IDs do not belong to the specified approved revision.");
        }

        var objectIds = revisionFiles.Select(rf => rf.ObjectId).Distinct().ToList();
        var objectsById = await dbContext.FileObjects
            .AsNoTracking()
            .Where(fo => objectIds.Contains(fo.Id))
            .ToDictionaryAsync(fo => fo.Id, cancellationToken);

        foreach (var rf in revisionFiles)
        {
            if (!objectsById.TryGetValue(rf.ObjectId, out var obj) ||
                !string.Equals(obj.ScanStatus, "CLEAN", StringComparison.OrdinalIgnoreCase))
            {
                var status = obj?.ScanStatus ?? "MISSING";
                throw ApiContractException.UnprocessableEntity(
                    messageAr: $"ممنوع: الملف ({rf.OriginalName}) في حالة ({status}) ولا يمكن نشره قبل اجتياز الفحص الأمني.",
                    messageEn: $"Blocked: file '{rf.OriginalName}' is in '{status}' status and cannot be published before passing security scan.");
            }
        }

        var now = DateTimeOffset.UtcNow;
        var publication = new PublicationEntity
        {
            Id = Guid.NewGuid(),
            RevisionId = revisionId,
            State = PublicationStateValues.Published,
            PublishedBy = actor.User.Id,
            PublishedAt = now,
            WithdrawnAt = null,
        };

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        dbContext.Publications.Add(publication);

        foreach (var fileId in selectedIds)
        {
            dbContext.PublicationFiles.Add(new PublicationFileEntity
            {
                Id = Guid.NewGuid(),
                PublicationId = publication.Id,
                RevisionFileId = fileId,
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        await serverAuthorization.WriteAuditEventAsync(
            actor.User.Id,
            "REVISION_PUBLISHED",
            "PUBLICATION",
            publication.Id,
            "SUCCESS",
            new
            {
                integrationId = integration.Id,
                apiVersionId = version.Id,
                versionLabel = version.VersionLabel,
                revisionId,
                revisionNumber = revision.RevisionNumber,
                publicationOption = document.PublicationOption,
                selectedFileIds = selectedIds,
            },
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return (await LoadSinglePublicationDtoAsync(publication.Id, cancellationToken))!;
    }

    public async Task<PublicationDto> WithdrawPublicationAsync(
        ClaimsPrincipal actorPrincipal,
        Guid publicationId,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var actor = await serverAuthorization.RequireGeneralPermissionAsync(
            actorPrincipal,
            PublishingPermissionCodes.PublicationsWithdraw,
            "PUBLICATION_WITHDRAW_DENIED",
            publicationId,
            "PUBLICATION",
            cancellationToken);

        var publication = await dbContext.Publications
            .FirstOrDefaultAsync(p => p.Id == publicationId, cancellationToken)
            ?? throw ApiContractException.NotFound(
                messageAr: "لم يتم العثور على سجل النشر المطلوب.",
                messageEn: "Publication record was not found.");

        var revision = await dbContext.DocumentRevisions
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == publication.RevisionId, cancellationToken)
            ?? throw ApiContractException.NotFound(
                messageAr: "لم يتم العثور على مراجعة التوثيق المرتبطة.",
                messageEn: "Associated revision was not found.");

        var version = await dbContext.ApiVersions
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == revision.ApiVersionId, cancellationToken)
            ?? throw ApiContractException.NotFound(
                messageAr: "لم يتم العثور على إصدار API المرتبط.",
                messageEn: "Associated API version was not found.");

        var (_, document) = await LoadIntegrationAndDocumentAsync(version.IntegrationId, cancellationToken);

        await documentAuthorization.RequireDocumentPermissionAsync(
            actorPrincipal,
            document.Id,
            DocumentPermissionCatalog.PublicationWithdraw,
            "PUBLICATION_WITHDRAW_DENIED",
            cancellationToken);

        if (string.Equals(publication.State, PublicationStateValues.Withdrawn, StringComparison.OrdinalIgnoreCase))
        {
            throw ApiContractException.Conflict(
                messageAr: "تم إيقاف نشر هذه الحزمة مسبقًا مع بقاء سجلها التاريخي محفوظًا.",
                messageEn: "This publication has already been withdrawn.");
        }

        publication.State = PublicationStateValues.Withdrawn;
        publication.WithdrawnAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        await serverAuthorization.WriteAuditEventAsync(
            actor.User.Id,
            "PUBLICATION_WITHDRAWN",
            "PUBLICATION",
            publication.Id,
            "SUCCESS",
            new
            {
                integrationId = version.IntegrationId,
                apiVersionId = version.Id,
                versionLabel = version.VersionLabel,
                revisionId = revision.Id,
                withdrawnAt = publication.WithdrawnAt,
            },
            cancellationToken);

        return (await LoadSinglePublicationDtoAsync(publication.Id, cancellationToken))!;
    }

    public async Task<ConsumerVersionResponse> GetConsumerVersionAsync(
        ClaimsPrincipal actorPrincipal,
        Guid integrationId,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var actor = await serverAuthorization.RequireGeneralPermissionAsync(
            actorPrincipal,
            PublishingPermissionCodes.VersionsUsageView,
            "CONSUMER_VERSION_VIEW_DENIED",
            integrationId,
            "INTEGRATION",
            cancellationToken);

        var (_, document) = await LoadIntegrationAndDocumentAsync(integrationId, cancellationToken);

        var docAccess = await documentAuthorization.RequireDocumentPermissionAsync(
            actorPrincipal,
            document.Id,
            DocumentPermissionCatalog.View,
            "CONSUMER_VERSION_VIEW_DENIED",
            cancellationToken);

        var canManage =
            (docAccess.EffectivePermissions.TryGetValue(DocumentPermissionCatalog.VersionUsage, out var pubRes) && pubRes.Allowed) &&
            await serverAuthorization.HasGeneralPermissionAsync(
                actorPrincipal, PublishingPermissionCodes.VersionsUsageManage, cancellationToken);

        var history = await LoadConsumerVersionHistoryAsync(integrationId, cancellationToken);
        var active = history.FirstOrDefault(h => h.ValidTo is null);

        return new ConsumerVersionResponse(
            IntegrationId: integrationId,
            ActiveUsage: active,
            History: history,
            CanManage: canManage,
            TraceId: traceId);
    }

    public async Task<ConsumerVersionResponse> SetConsumerVersionAsync(
        ClaimsPrincipal actorPrincipal,
        Guid integrationId,
        SetConsumerVersionRequest request,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var actor = await serverAuthorization.RequireGeneralPermissionAsync(
            actorPrincipal,
            PublishingPermissionCodes.VersionsUsageManage,
            "CONSUMER_VERSION_UPDATE_DENIED",
            integrationId,
            "INTEGRATION",
            cancellationToken);

        var (integration, document) = await LoadIntegrationAndDocumentAsync(integrationId, cancellationToken);

        await documentAuthorization.RequireDocumentPermissionAsync(
            actorPrincipal,
            document.Id,
            DocumentPermissionCatalog.VersionUsage,
            "CONSUMER_VERSION_UPDATE_DENIED",
            cancellationToken);

        var version = await dbContext.ApiVersions
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == request.ApiVersionId && v.IntegrationId == integration.Id, cancellationToken);

        if (version is null)
        {
            throw ApiContractException.UnprocessableEntity(
                messageAr: "إصدار API المحدد غير موجود ضمن هذا التكامل.",
                messageEn: "The selected API version does not belong to this integration.");
        }

        var revisionIds = await dbContext.DocumentRevisions
            .AsNoTracking()
            .Where(r => r.ApiVersionId == version.Id)
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        var hasActivePublication = revisionIds.Count > 0 && await dbContext.Publications
            .AsNoTracking()
            .AnyAsync(p => revisionIds.Contains(p.RevisionId) && p.State == PublicationStateValues.Published, cancellationToken);

        if (!hasActivePublication)
        {
            throw ApiContractException.UnprocessableEntity(
                messageAr: $"لا يمكن اعتماد الإصدار ({version.VersionLabel}) كإصدار قيد الاستخدام حاليًا لأنه لا يملك حزمة منشورة نشطة (PUBLISHED).",
                messageEn: $"API version '{version.VersionLabel}' cannot be set as active consumer version without an active PUBLISHED publication.");
        }

        var now = DateTimeOffset.UtcNow;

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // Close any active row first so partial unique index ux_consumer_version_usage_active_integration_id remains satisfied.
        await dbContext.ConsumerVersionUsage
            .Where(u => u.IntegrationId == integration.Id && u.ValidTo == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(u => u.ValidTo, now), cancellationToken);

        var usage = new ConsumerVersionUsageEntity
        {
            Id = Guid.NewGuid(),
            IntegrationId = integration.Id,
            ApiVersionId = version.Id,
            ValidFrom = now,
            ValidTo = null,
            AssignedBy = actor.User.Id,
        };

        dbContext.ConsumerVersionUsage.Add(usage);
        await dbContext.SaveChangesAsync(cancellationToken);

        await serverAuthorization.WriteAuditEventAsync(
            actor.User.Id,
            "CONSUMER_VERSION_UPDATED",
            "INTEGRATION",
            integration.Id,
            "SUCCESS",
            new
            {
                integrationId = integration.Id,
                apiVersionId = version.Id,
                versionLabel = version.VersionLabel,
                usageId = usage.Id,
                validFrom = now,
            },
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return await GetConsumerVersionAsync(actorPrincipal, integrationId, traceId, cancellationToken);
    }

    public async Task<PortalIntegrationsResponse> GetPortalIntegrationsAsync(
        ClaimsPrincipal actorPrincipal,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var (actor, consumer) = await RequirePortalConsumerActorAsync(actorPrincipal, null, "PORTAL", cancellationToken);

        var consumerDocs = await dbContext.ApiDocuments
            .AsNoTracking()
            .Where(d => d.ConsumerEntityId == consumer.Id)
            .ToListAsync(cancellationToken);

        if (consumerDocs.Count == 0)
        {
            return new PortalIntegrationsResponse(consumer.Id, consumer.Name, [], traceId);
        }

        var docIds = consumerDocs.Select(d => d.Id).ToList();
        var integrations = await dbContext.Integrations
            .AsNoTracking()
            .Where(i => docIds.Contains(i.DocumentId))
            .OrderBy(i => i.ReferenceCode)
            .ToListAsync(cancellationToken);

        if (integrations.Count == 0)
        {
            return new PortalIntegrationsResponse(consumer.Id, consumer.Name, [], traceId);
        }

        var integrationIds = integrations.Select(i => i.Id).ToList();
        var versions = await dbContext.ApiVersions
            .AsNoTracking()
            .Where(v => integrationIds.Contains(v.IntegrationId))
            .ToListAsync(cancellationToken);
        var versionById = versions.ToDictionary(v => v.Id);
        var versionIds = versions.Select(v => v.Id).ToList();

        var revisions = versionIds.Count == 0
            ? []
            : await dbContext.DocumentRevisions
                .AsNoTracking()
                .Where(r => versionIds.Contains(r.ApiVersionId))
                .ToListAsync(cancellationToken);
        var revisionById = revisions.ToDictionary(r => r.Id);
        var revisionIds = revisions.Select(r => r.Id).ToList();

        var publishedRows = revisionIds.Count == 0
            ? []
            : await dbContext.Publications
                .AsNoTracking()
                .Where(p => revisionIds.Contains(p.RevisionId) && p.State == PublicationStateValues.Published)
                .OrderByDescending(p => p.PublishedAt)
                .ToListAsync(cancellationToken);

        if (publishedRows.Count == 0)
        {
            return new PortalIntegrationsResponse(consumer.Id, consumer.Name, [], traceId);
        }

        var pubIds = publishedRows.Select(p => p.Id).ToList();
        var fileCountsByPub = await dbContext.PublicationFiles
            .AsNoTracking()
            .Where(pf => pubIds.Contains(pf.PublicationId))
            .GroupBy(pf => pf.PublicationId)
            .Select(g => new { PublicationId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.PublicationId, x => x.Count, cancellationToken);

        var activeUsageRows = await dbContext.ConsumerVersionUsage
            .AsNoTracking()
            .Where(u => integrationIds.Contains(u.IntegrationId) && u.ValidTo == null)
            .ToListAsync(cancellationToken);
        var activeVersionByIntegration = activeUsageRows.ToDictionary(u => u.IntegrationId, u => u.ApiVersionId);

        var docById = consumerDocs.ToDictionary(d => d.Id);
        var items = new List<PortalIntegrationItemDto>();

        foreach (var integration in integrations)
        {
            if (!docById.TryGetValue(integration.DocumentId, out var doc))
            {
                continue;
            }

            var integrationPubs = publishedRows
                .Where(p => revisionById.TryGetValue(p.RevisionId, out var rev) &&
                            versionById.TryGetValue(rev.ApiVersionId, out var ver) &&
                            ver.IntegrationId == integration.Id)
                .ToList();

            if (integrationPubs.Count == 0)
            {
                continue;
            }

            activeVersionByIntegration.TryGetValue(integration.Id, out var activeVerId);
            var activeVerLabel = activeVerId != Guid.Empty && versionById.TryGetValue(activeVerId, out var activeVer)
                ? activeVer.VersionLabel
                : null;

            // Prefer the newest active publication on the active consumer version if present, else newest active publication.
            var selectedPub = integrationPubs.FirstOrDefault(p =>
                activeVerId != Guid.Empty &&
                revisionById.TryGetValue(p.RevisionId, out var rev) &&
                rev.ApiVersionId == activeVerId) ?? integrationPubs[0];

            var selectedRev = revisionById[selectedPub.RevisionId];
            var selectedVer = versionById[selectedRev.ApiVersionId];
            var (providerName, _, apiGroupName) = await ResolveDocumentNamesAsync(doc, cancellationToken);

            items.Add(new PortalIntegrationItemDto(
                IntegrationId: integration.Id,
                ReferenceCode: integration.ReferenceCode,
                DocumentId: doc.Id,
                DocumentTitle: BuildDocumentTitle(providerName, consumer.Name, apiGroupName),
                ProviderName: providerName,
                ApiGroupName: apiGroupName,
                ActiveConsumerVersionLabel: activeVerLabel,
                LatestPublicationId: selectedPub.Id,
                LatestPublicationVersionLabel: selectedVer.VersionLabel,
                LatestPublicationRevisionNumber: selectedRev.RevisionNumber,
                LatestPublishedAt: selectedPub.PublishedAt,
                PublishedFilesCount: fileCountsByPub.GetValueOrDefault(selectedPub.Id, 0)));
        }

        await serverAuthorization.WriteAuditEventAsync(
            actor.Id,
            "PORTAL_INTEGRATIONS_VIEWED",
            "PORTAL",
            consumer.Id,
            "SUCCESS",
            new { consumerEntityId = consumer.Id, count = items.Count },
            cancellationToken);

        return new PortalIntegrationsResponse(consumer.Id, consumer.Name, items, traceId);
    }

    public async Task<PortalPublicationFilesResponse> GetPortalPublicationFilesAsync(
        ClaimsPrincipal actorPrincipal,
        Guid publicationId,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var (actor, consumer) = await RequirePortalConsumerActorAsync(
            actorPrincipal, publicationId, "PUBLICATION", cancellationToken);

        var (publication, revision, version, integration, document) =
            await LoadPortalScopedPublicationAsync(actor, consumer, publicationId, cancellationToken);

        var files = await LoadPublicationFilesDtoAsync([publication.Id], cleanOnly: true, cancellationToken);
        var (providerName, _, apiGroupName) = await ResolveDocumentNamesAsync(document, cancellationToken);

        return new PortalPublicationFilesResponse(
            PublicationId: publication.Id,
            IntegrationId: integration.Id,
            ReferenceCode: integration.ReferenceCode,
            DocumentTitle: BuildDocumentTitle(providerName, consumer.Name, apiGroupName),
            VersionLabel: version.VersionLabel,
            RevisionNumber: revision.RevisionNumber,
            PublishedAt: publication.PublishedAt,
            Files: files.GetValueOrDefault(publication.Id, []),
            TraceId: traceId);
    }

    public async Task<(Stream ContentStream, string FileName, string ContentType, long SizeBytes, string Sha256)>
        DownloadPortalPublicationFileAsync(
            ClaimsPrincipal actorPrincipal,
            Guid publicationId,
            Guid revisionFileId,
            CancellationToken cancellationToken = default)
    {
        var (actor, consumer) = await RequirePortalConsumerActorAsync(
            actorPrincipal, publicationId, "PUBLICATION", cancellationToken);

        var (publication, _, _, integration, _) =
            await LoadPortalScopedPublicationAsync(actor, consumer, publicationId, cancellationToken);

        var pubFile = await dbContext.PublicationFiles
            .AsNoTracking()
            .FirstOrDefaultAsync(pf => pf.PublicationId == publication.Id && pf.RevisionFileId == revisionFileId, cancellationToken);

        if (pubFile is null)
        {
            await serverAuthorization.WriteAuditEventAsync(
                actor.Id,
                "PORTAL_FILE_DOWNLOAD_DENIED",
                "PUBLICATION_FILE",
                revisionFileId,
                "DENIED",
                new { consumerEntityId = consumer.Id, publicationId, revisionFileId, reason = "NOT_IN_PUBLICATION" },
                cancellationToken);

            throw ApiContractException.NotFound(
                messageAr: "الملف المطلوب غير مدرج ضمن حزمة النشر المعتمدة.",
                messageEn: "The requested file is not part of this published package.");
        }

        var revFile = await dbContext.RevisionFiles
            .AsNoTracking()
            .FirstOrDefaultAsync(rf => rf.Id == revisionFileId, cancellationToken)
            ?? throw ApiContractException.NotFound(
                messageAr: "بيانات الملف غير موجودة.",
                messageEn: "Revision file metadata was not found.");

        var fileObject = await dbContext.FileObjects
            .AsNoTracking()
            .FirstOrDefaultAsync(fo => fo.Id == revFile.ObjectId, cancellationToken)
            ?? throw ApiContractException.NotFound(
                messageAr: "كائن الملف غير موجود.",
                messageEn: "File object was not found.");

        if (!string.Equals(fileObject.ScanStatus, "CLEAN", StringComparison.OrdinalIgnoreCase))
        {
            await serverAuthorization.WriteAuditEventAsync(
                actor.Id,
                "PORTAL_FILE_DOWNLOAD_BLOCKED",
                "PUBLICATION_FILE",
                revisionFileId,
                "DENIED",
                new { consumerEntityId = consumer.Id, publicationId, fileObject.ScanStatus },
                cancellationToken);

            throw ApiContractException.UnprocessableEntity(
                messageAr: "هذا الملف غير متاح للتنزيل لأن حالته الأمنية ليست CLEAN.",
                messageEn: $"Blocked: file is in '{fileObject.ScanStatus}' status and cannot be downloaded.");
        }

        var (stream, sizeBytes) = await objectReadAccessor.OpenVerifiedObjectStreamAsync(
            fileObject.StorageKey,
            fileObject.Sha256,
            cancellationToken);

        await serverAuthorization.WriteAuditEventAsync(
            actor.Id,
            "PORTAL_FILE_DOWNLOADED",
            "PUBLICATION_FILE",
            revisionFileId,
            "SUCCESS",
            new
            {
                consumerEntityId = consumer.Id,
                integrationId = integration.Id,
                publicationId = publication.Id,
                revisionFileId,
                fileName = revFile.OriginalName,
                sha256 = fileObject.Sha256,
                sizeBytes,
            },
            cancellationToken);

        var contentType = string.IsNullOrWhiteSpace(fileObject.MediaType)
            ? "application/octet-stream"
            : fileObject.MediaType;

        return (stream, revFile.OriginalName, contentType, sizeBytes, fileObject.Sha256);
    }

    private async Task<(UserEntity Actor, ConsumerEntityRecord Consumer)> RequirePortalConsumerActorAsync(
        ClaimsPrincipal principal,
        Guid? targetResourceId,
        string resourceKind,
        CancellationToken cancellationToken)
    {
        var user = await serverAuthorization.RequireActiveAuthenticatedUserAsync(principal, cancellationToken);

        if (!user.ConsumerEntityId.HasValue)
        {
            await serverAuthorization.WriteAuditEventAsync(
                user.Id,
                "PORTAL_ACCESS_DENIED",
                resourceKind,
                targetResourceId,
                "DENIED",
                new { reason = "NO_CONSUMER_ENTITY_SCOPE" },
                cancellationToken);

            throw ApiContractException.Forbidden(
                messageAr: "بوابة المستهلك متاحة فقط لحسابات الجهات المستهلكة المرتبطة بجهة محددة.",
                messageEn: "The external consumer portal is restricted to users scoped to a consumer entity.");
        }

        var consumer = await dbContext.ConsumerEntities
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == user.ConsumerEntityId.Value, cancellationToken);

        if (consumer is null || !consumer.IsActive)
        {
            await serverAuthorization.WriteAuditEventAsync(
                user.Id,
                "PORTAL_ACCESS_DENIED",
                resourceKind,
                targetResourceId,
                "DENIED",
                new { consumerEntityId = user.ConsumerEntityId.Value, reason = "CONSUMER_INACTIVE_OR_MISSING" },
                cancellationToken);

            throw ApiContractException.Forbidden(
                messageAr: "الجهة المستهلكة المرتبطة بهذا الحساب غير فعالة أو غير موجودة.",
                messageEn: "The associated consumer entity is inactive or does not exist.");
        }

        return (user, consumer);
    }

    private async Task<(
        PublicationEntity Publication,
        DocumentRevisionEntity Revision,
        ApiVersionEntity Version,
        IntegrationEntity Integration,
        ApiDocumentEntity Document)> LoadPortalScopedPublicationAsync(
        UserEntity actor,
        ConsumerEntityRecord consumer,
        Guid publicationId,
        CancellationToken cancellationToken)
    {
        var publication = await dbContext.Publications
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == publicationId, cancellationToken)
            ?? throw ApiContractException.NotFound(
                messageAr: "حزمة النشر المطلوبة غير موجودة.",
                messageEn: "Requested publication was not found.");

        var revision = await dbContext.DocumentRevisions
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == publication.RevisionId, cancellationToken)
            ?? throw ApiContractException.NotFound(
                messageAr: "المراجعة المرتبطة بحزمة النشر غير موجودة.",
                messageEn: "Revision associated with the publication was not found.");

        var version = await dbContext.ApiVersions
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == revision.ApiVersionId, cancellationToken)
            ?? throw ApiContractException.NotFound(
                messageAr: "إصدار API المرتبط غير موجود.",
                messageEn: "API version associated with the publication was not found.");

        var (integration, document) = await LoadIntegrationAndDocumentAsync(version.IntegrationId, cancellationToken);

        if (document.ConsumerEntityId != consumer.Id)
        {
            await serverAuthorization.WriteAuditEventAsync(
                actor.Id,
                "PORTAL_ACCESS_DENIED",
                "PUBLICATION",
                publicationId,
                "DENIED",
                new
                {
                    actorConsumerEntityId = consumer.Id,
                    documentConsumerEntityId = document.ConsumerEntityId,
                    reason = "CROSS_CONSUMER_IDOR_BLOCKED",
                },
                cancellationToken);

            throw ApiContractException.Forbidden(
                messageAr: "غير مصرح لك بالوصول إلى حزمة نشر تابعة لجهة مستهلكة أخرى.",
                messageEn: "Access denied: publication belongs to another consumer entity.");
        }

        if (!string.Equals(publication.State, PublicationStateValues.Published, StringComparison.OrdinalIgnoreCase))
        {
            await serverAuthorization.WriteAuditEventAsync(
                actor.Id,
                "PORTAL_WITHDRAWN_BLOCKED",
                "PUBLICATION",
                publicationId,
                "DENIED",
                new { consumerEntityId = consumer.Id, state = publication.State },
                cancellationToken);

            throw ApiContractException.Forbidden(
                messageAr: "تم إيقاف نشر هذه الحزمة (WITHDRAWN) ولم تعد متاحة عبر بوابة المستهلك.",
                messageEn: "This publication has been withdrawn and is no longer accessible on the consumer portal.");
        }

        return (publication, revision, version, integration, document);
    }

    private async Task<(IntegrationEntity Integration, ApiDocumentEntity Document)> LoadIntegrationAndDocumentAsync(
        Guid integrationId,
        CancellationToken cancellationToken)
    {
        var integration = await dbContext.Integrations
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == integrationId, cancellationToken)
            ?? throw ApiContractException.NotFound(
                messageAr: "لم يتم العثور على التكامل المطلوب.",
                messageEn: "Integration was not found.");

        var document = await dbContext.ApiDocuments
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == integration.DocumentId, cancellationToken)
            ?? throw ApiContractException.NotFound(
                messageAr: "لم يتم العثور على وثيقة API المرتبطة بالتكامل.",
                messageEn: "API document associated with this integration was not found.");

        return (integration, document);
    }

    private async Task<IReadOnlyList<PublicationDto>> LoadPublicationsForIntegrationAsync(
        Guid integrationId,
        CancellationToken cancellationToken)
    {
        var versions = await dbContext.ApiVersions
            .AsNoTracking()
            .Where(v => v.IntegrationId == integrationId)
            .ToListAsync(cancellationToken);

        if (versions.Count == 0)
        {
            return [];
        }

        var versionById = versions.ToDictionary(v => v.Id);
        var versionIds = versions.Select(v => v.Id).ToList();

        var revisions = await dbContext.DocumentRevisions
            .AsNoTracking()
            .Where(r => versionIds.Contains(r.ApiVersionId))
            .ToListAsync(cancellationToken);

        if (revisions.Count == 0)
        {
            return [];
        }

        var revisionById = revisions.ToDictionary(r => r.Id);
        var revisionIds = revisions.Select(r => r.Id).ToList();

        var publications = await dbContext.Publications
            .AsNoTracking()
            .Where(p => revisionIds.Contains(p.RevisionId))
            .OrderByDescending(p => p.PublishedAt)
            .ToListAsync(cancellationToken);

        if (publications.Count == 0)
        {
            return [];
        }

        return await HydratePublicationDtosAsync(publications, revisionById, versionById, cancellationToken);
    }

    private async Task<PublicationDto?> LoadSinglePublicationDtoAsync(
        Guid publicationId,
        CancellationToken cancellationToken)
    {
        var publication = await dbContext.Publications
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == publicationId, cancellationToken);

        if (publication is null)
        {
            return null;
        }

        var revision = await dbContext.DocumentRevisions
            .AsNoTracking()
            .FirstAsync(r => r.Id == publication.RevisionId, cancellationToken);
        var version = await dbContext.ApiVersions
            .AsNoTracking()
            .FirstAsync(v => v.Id == revision.ApiVersionId, cancellationToken);

        var dtos = await HydratePublicationDtosAsync(
            [publication],
            new Dictionary<Guid, DocumentRevisionEntity> { [revision.Id] = revision },
            new Dictionary<Guid, ApiVersionEntity> { [version.Id] = version },
            cancellationToken);

        return dtos.FirstOrDefault();
    }

    private async Task<IReadOnlyList<PublicationDto>> HydratePublicationDtosAsync(
        IReadOnlyList<PublicationEntity> publications,
        IReadOnlyDictionary<Guid, DocumentRevisionEntity> revisionById,
        IReadOnlyDictionary<Guid, ApiVersionEntity> versionById,
        CancellationToken cancellationToken)
    {
        var pubIds = publications.Select(p => p.Id).ToList();
        var publisherIds = publications.Select(p => p.PublishedBy).Distinct().ToList();
        var userNames = await dbContext.Users
            .AsNoTracking()
            .Where(u => publisherIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, cancellationToken);

        var filesByPub = await LoadPublicationFilesDtoAsync(pubIds, cleanOnly: false, cancellationToken);

        return publications.Select(pub =>
        {
            var rev = revisionById[pub.RevisionId];
            var ver = versionById[rev.ApiVersionId];
            return new PublicationDto(
                Id: pub.Id,
                IntegrationId: ver.IntegrationId,
                ApiVersionId: ver.Id,
                VersionLabel: ver.VersionLabel,
                RevisionId: rev.Id,
                RevisionNumber: rev.RevisionNumber,
                State: pub.State,
                PublishedBy: pub.PublishedBy,
                PublishedByName: userNames.GetValueOrDefault(pub.PublishedBy, "—"),
                PublishedAt: pub.PublishedAt,
                WithdrawnAt: pub.WithdrawnAt,
                Files: filesByPub.GetValueOrDefault(pub.Id, []));
        }).ToList();
    }

    private async Task<Dictionary<Guid, IReadOnlyList<PublicationFileDto>>> LoadPublicationFilesDtoAsync(
        IReadOnlyCollection<Guid> publicationIds,
        bool cleanOnly,
        CancellationToken cancellationToken)
    {
        if (publicationIds.Count == 0)
        {
            return [];
        }

        var pubFiles = await dbContext.PublicationFiles
            .AsNoTracking()
            .Where(pf => publicationIds.Contains(pf.PublicationId))
            .ToListAsync(cancellationToken);

        if (pubFiles.Count == 0)
        {
            return [];
        }

        var revFileIds = pubFiles.Select(pf => pf.RevisionFileId).Distinct().ToList();
        var revFiles = await dbContext.RevisionFiles
            .AsNoTracking()
            .Where(rf => revFileIds.Contains(rf.Id))
            .ToDictionaryAsync(rf => rf.Id, cancellationToken);

        var objectIds = revFiles.Values.Select(rf => rf.ObjectId).Distinct().ToList();
        var objectsById = await dbContext.FileObjects
            .AsNoTracking()
            .Where(fo => objectIds.Contains(fo.Id))
            .ToDictionaryAsync(fo => fo.Id, cancellationToken);

        var fileTypeIds = revFiles.Values.Select(rf => rf.FileTypeId).Distinct().ToList();
        var fileTypesById = await dbContext.FileTypes
            .AsNoTracking()
            .Where(ft => fileTypeIds.Contains(ft.Id))
            .ToDictionaryAsync(ft => ft.Id, cancellationToken);

        return pubFiles
            .GroupBy(pf => pf.PublicationId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<PublicationFileDto>)g
                    .Select(pf =>
                    {
                        if (!revFiles.TryGetValue(pf.RevisionFileId, out var rf) ||
                            !objectsById.TryGetValue(rf.ObjectId, out var obj))
                        {
                            return null;
                        }

                        if (cleanOnly && !string.Equals(obj.ScanStatus, "CLEAN", StringComparison.OrdinalIgnoreCase))
                        {
                            return null;
                        }

                        fileTypesById.TryGetValue(rf.FileTypeId, out var ft);

                        return new PublicationFileDto(
                            Id: pf.Id,
                            RevisionFileId: rf.Id,
                            FileTypeId: rf.FileTypeId,
                            FileTypeCode: ft?.CatalogCode ?? "FILE",
                            FileTypeName: ft?.DisplayName ?? "ملف",
                            OriginalName: rf.OriginalName,
                            SizeBytes: obj.SizeBytes,
                            MediaType: obj.MediaType ?? "application/octet-stream",
                            Sha256: obj.Sha256,
                            ScanStatus: obj.ScanStatus);
                    })
                    .Where(dto => dto is not null)
                    .Select(dto => dto!)
                    .ToList());
    }

    private async Task<ConsumerVersionUsageDto?> LoadActiveConsumerVersionDtoAsync(
        Guid integrationId,
        CancellationToken cancellationToken)
    {
        var active = await dbContext.ConsumerVersionUsage
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.IntegrationId == integrationId && u.ValidTo == null, cancellationToken);

        if (active is null)
        {
            return null;
        }

        var version = await dbContext.ApiVersions
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == active.ApiVersionId, cancellationToken);
        var user = await dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == active.AssignedBy, cancellationToken);

        return new ConsumerVersionUsageDto(
            Id: active.Id,
            IntegrationId: active.IntegrationId,
            ApiVersionId: active.ApiVersionId,
            VersionLabel: version?.VersionLabel ?? "—",
            ValidFrom: active.ValidFrom,
            ValidTo: active.ValidTo,
            AssignedBy: active.AssignedBy,
            AssignedByName: user?.FullName ?? "—");
    }

    private async Task<IReadOnlyList<ConsumerVersionUsageDto>> LoadConsumerVersionHistoryAsync(
        Guid integrationId,
        CancellationToken cancellationToken)
    {
        var usages = await dbContext.ConsumerVersionUsage
            .AsNoTracking()
            .Where(u => u.IntegrationId == integrationId)
            .OrderByDescending(u => u.ValidFrom)
            .ToListAsync(cancellationToken);

        if (usages.Count == 0)
        {
            return [];
        }

        var verIds = usages.Select(u => u.ApiVersionId).Distinct().ToList();
        var userIds = usages.Select(u => u.AssignedBy).Distinct().ToList();

        var versions = await dbContext.ApiVersions
            .AsNoTracking()
            .Where(v => verIds.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, v => v.VersionLabel, cancellationToken);

        var users = await dbContext.Users
            .AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, cancellationToken);

        return usages.Select(u => new ConsumerVersionUsageDto(
            Id: u.Id,
            IntegrationId: u.IntegrationId,
            ApiVersionId: u.ApiVersionId,
            VersionLabel: versions.GetValueOrDefault(u.ApiVersionId, "—"),
            ValidFrom: u.ValidFrom,
            ValidTo: u.ValidTo,
            AssignedBy: u.AssignedBy,
            AssignedByName: users.GetValueOrDefault(u.AssignedBy, "—"))).ToList();
    }

    private async Task<(string ProviderName, string ConsumerName, string ApiGroupName)> ResolveDocumentNamesAsync(
        ApiDocumentEntity document,
        CancellationToken cancellationToken)
    {
        string providerName = "—";
        if (document.ProviderInternalSystemId.HasValue)
        {
            providerName = await dbContext.CompanySystems
                .AsNoTracking()
                .Where(s => s.Id == document.ProviderInternalSystemId.Value)
                .Select(s => s.Name)
                .FirstOrDefaultAsync(cancellationToken) ?? "—";

            if (providerName == "—")
            {
                var internalProvider = await dbContext.InternalProviderSystems
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == document.ProviderInternalSystemId.Value, cancellationToken);
                if (internalProvider is not null)
                {
                    providerName = await dbContext.CompanySystems
                        .AsNoTracking()
                        .Where(s => s.Id == internalProvider.CompanySystemId)
                        .Select(s => s.Name)
                        .FirstOrDefaultAsync(cancellationToken) ?? "—";
                }
            }
        }
        else if (document.ProviderExternalSystemId.HasValue)
        {
            providerName = await dbContext.ExternalProviderSystems
                .AsNoTracking()
                .Where(p => p.Id == document.ProviderExternalSystemId.Value)
                .Select(p => p.Name)
                .FirstOrDefaultAsync(cancellationToken) ?? "—";
        }

        string consumerName = "—";
        if (document.ConsumerCompanySystemId.HasValue)
        {
            consumerName = await dbContext.CompanySystems
                .AsNoTracking()
                .Where(s => s.Id == document.ConsumerCompanySystemId.Value)
                .Select(s => s.Name)
                .FirstOrDefaultAsync(cancellationToken) ?? "—";
        }
        else if (document.ConsumerEntityId.HasValue)
        {
            consumerName = await dbContext.ConsumerEntities
                .AsNoTracking()
                .Where(c => c.Id == document.ConsumerEntityId.Value)
                .Select(c => c.Name)
                .FirstOrDefaultAsync(cancellationToken) ?? "—";
        }

        var apiGroupName = await dbContext.ApiGroups
            .AsNoTracking()
            .Where(g => g.Id == document.ApiGroupId)
            .Select(g => g.Name)
            .FirstOrDefaultAsync(cancellationToken) ?? "—";

        return (providerName, consumerName, apiGroupName);
    }

    private static string BuildDocumentTitle(string providerName, string consumerName, string apiGroupName)
        => $"{providerName} → {consumerName} · {apiGroupName}";
}
