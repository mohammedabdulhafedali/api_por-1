using System.Security.Claims;
using ApiGovernance.Modules.Changes.Contracts;
using ApiGovernance.Shared.Contracts;
using ApiGovernance.Shared.Persistence;
using ApiGovernance.Shared.Persistence.Entities;
using ApiGovernance.Shared.Security;
using Microsoft.EntityFrameworkCore;

namespace ApiGovernance.Modules.Changes.Services;

public interface IChangeRequestsService
{
    Task<IntegrationChangeRequestsWorkspaceResponse> GetIntegrationChangeRequestsAsync(
        ClaimsPrincipal actorPrincipal,
        Guid integrationId,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<ChangeRequestDto> CreateChangeRequestAsync(
        ClaimsPrincipal actorPrincipal,
        Guid integrationId,
        CreateChangeRequestInput request,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<ChangeRequestDto> GetChangeRequestDetailsAsync(
        ClaimsPrincipal actorPrincipal,
        Guid requestId,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<ChangeRequestDto> UpdateChangeRequestAsync(
        ClaimsPrincipal actorPrincipal,
        Guid requestId,
        UpdateChangeRequestInput request,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<SubmitChangeRequestResponse> SubmitChangeRequestAsync(
        ClaimsPrincipal actorPrincipal,
        Guid requestId,
        string traceId,
        CancellationToken cancellationToken = default);
}

public sealed class ChangeRequestsService(
    ApiGovernanceDbContext dbContext,
    IServerAuthorizationService serverAuthorization,
    IDocumentAuthorizationService documentAuthorization,
    IWorkflowRunStarter workflowRunStarter) : IChangeRequestsService
{
    private static bool IsEditableStatus(string status) =>
        string.Equals(status, ChangeRequestStatusValues.Draft, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, ChangeRequestStatusValues.Returned, StringComparison.OrdinalIgnoreCase);

    public async Task<IntegrationChangeRequestsWorkspaceResponse> GetIntegrationChangeRequestsAsync(
        ClaimsPrincipal actorPrincipal,
        Guid integrationId,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var (access, integration) = await documentAuthorization.RequireIntegrationDocumentPermissionAsync(
            actorPrincipal,
            integrationId,
            DocumentPermissionCatalog.View,
            "CHANGE_REQUESTS_LIST_DENIED",
            cancellationToken);

        var isAssignedDev = await documentAuthorization.IsUserAssignedDeveloperAsync(
            access.Actor.Id, integration.DocumentId, integration.Id, cancellationToken);
        var hasDocChange = access.EffectivePermissions.TryGetValue(DocumentPermissionCatalog.Change, out var chRes) && chRes.Allowed;
        var canActOnChanges = isAssignedDev && hasDocChange && !string.Equals(access.Document.State, "ARCHIVED", StringComparison.OrdinalIgnoreCase);

        var canCreate = canActOnChanges && await serverAuthorization.HasGeneralPermissionAsync(actorPrincipal, "changes.create", cancellationToken);
        var canEdit = canActOnChanges && await serverAuthorization.HasGeneralPermissionAsync(actorPrincipal, "changes.edit", cancellationToken);
        var canSubmit = canActOnChanges && await serverAuthorization.HasGeneralPermissionAsync(actorPrincipal, "changes.submit", cancellationToken);

        var allowedChangeTypes = await (
            from cpt in dbContext.ChangePolicyTypes.AsNoTracking()
            join ct in dbContext.ChangeTypes.AsNoTracking() on cpt.ChangeTypeId equals ct.Id
            join route in dbContext.ApprovalRoutes.AsNoTracking() on ct.RouteId equals route.Id
            where cpt.PolicyId == access.Document.ChangePolicyId && ct.IsActive && route.IsActive
            orderby ct.Name
            select new AllowedChangeTypeOptionDto(ct.Id, ct.Name, ct.RouteId, route.Name))
            .ToListAsync(cancellationToken);

        var approvedRevisions = await (
            from rev in dbContext.DocumentRevisions.AsNoTracking()
            join ver in dbContext.ApiVersions.AsNoTracking() on rev.ApiVersionId equals ver.Id
            where ver.IntegrationId == integration.Id && rev.Status == "APPROVED"
            orderby ver.CreatedAt descending, rev.RevisionNumber descending
            select new { rev, ver.VersionLabel })
            .ToListAsync(cancellationToken);

        var appRevIds = approvedRevisions.Select(r => r.rev.Id).ToList();
        var revFilesMap = appRevIds.Count == 0
            ? new Dictionary<Guid, List<AffectedSourceFileDto>>()
            : (await (
                from rf in dbContext.RevisionFiles.AsNoTracking()
                join fo in dbContext.FileObjects.AsNoTracking() on rf.ObjectId equals fo.Id
                join ft in dbContext.FileTypes.AsNoTracking() on rf.FileTypeId equals ft.Id
                where appRevIds.Contains(rf.RevisionId) && fo.ScanStatus == "CLEAN"
                orderby rf.OriginalName
                select new
                {
                    rf.RevisionId,
                    Dto = new AffectedSourceFileDto(rf.Id, rf.OriginalName, ft.DisplayName, fo.Sha256, fo.SizeBytes)
                })
                .ToListAsync(cancellationToken))
                .GroupBy(x => x.RevisionId)
                .ToDictionary(g => g.Key, g => g.Select(x => x.Dto).ToList());

        var eligibleRevs = approvedRevisions
            .Select(r => new EligibleApprovedRevisionForChangeDto(
                r.rev.Id,
                r.rev.ApiVersionId,
                r.VersionLabel,
                r.rev.RevisionNumber,
                revFilesMap.GetValueOrDefault(r.rev.Id, [])))
            .Where(r => r.AvailableFiles.Count > 0)
            .ToList();

        var crEntities = await dbContext.ChangeRequests.AsNoTracking()
            .Where(c => c.IntegrationId == integration.Id)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(cancellationToken);

        var dtos = await BuildChangeRequestDtosAsync(crEntities, integration, traceId, cancellationToken);
        var derivedTitle = await BuildDerivedTitleAsync(access.Document, cancellationToken);

        return new IntegrationChangeRequestsWorkspaceResponse(
            integration.Id,
            access.Document.Id,
            integration.ReferenceCode,
            derivedTitle,
            canCreate,
            canEdit,
            canSubmit,
            eligibleRevs,
            allowedChangeTypes,
            dtos,
            traceId);
    }

    public async Task<ChangeRequestDto> CreateChangeRequestAsync(
        ClaimsPrincipal actorPrincipal,
        Guid integrationId,
        CreateChangeRequestInput request,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var (access, integration) = await documentAuthorization.RequireIntegrationDocumentPermissionAsync(
            actorPrincipal,
            integrationId,
            DocumentPermissionCatalog.Change,
            "CHANGE_REQUEST_CREATE_DENIED",
            cancellationToken);

        await EnsureAssignedDeveloperAndNotArchivedAsync(access, integration, "CHANGE_REQUEST_CREATE_DENIED", cancellationToken);

        var sourceRevision = await (
            from rev in dbContext.DocumentRevisions.AsNoTracking()
            join ver in dbContext.ApiVersions.AsNoTracking() on rev.ApiVersionId equals ver.Id
            where rev.Id == request.SourceRevisionId && ver.IntegrationId == integration.Id
            select rev)
            .FirstOrDefaultAsync(cancellationToken);

        if (sourceRevision is null)
        {
            throw ApiContractException.NotFound(
                messageAr: "المراجعة المرجعية المطلوبة غير موجودة ضمن هذا التكامل.",
                messageEn: "Specified source revision was not found for this integration.");
        }

        if (!string.Equals(sourceRevision.Status, "APPROVED", StringComparison.OrdinalIgnoreCase))
        {
            throw ApiContractException.UnprocessableEntity(
                messageAr: $"لا يمكن إنشاء طلب تغيير إلا انطلاقًا من مراجعة معتمدة (APPROVED)، الحالة الحالية: {sourceRevision.Status}.",
                messageEn: $"Change requests can only target an APPROVED revision (current status: {sourceRevision.Status}).");
        }

        var distinctFileIds = request.AffectedSourceFileIds.Distinct().ToList();
        await ValidateChangeTypeAndFileScopeAsync(
            access.Document.ChangePolicyId,
            request.ChangeTypeId,
            sourceRevision.Id,
            distinctFileIds,
            cancellationToken);

        var entity = new ChangeRequestEntity
        {
            Id = Guid.NewGuid(),
            IntegrationId = integration.Id,
            SourceRevisionId = sourceRevision.Id,
            ChangeTypeId = request.ChangeTypeId,
            Reason = request.Reason.Trim(),
            ConsumerImpact = string.IsNullOrWhiteSpace(request.ConsumerImpact) ? null : request.ConsumerImpact.Trim(),
            Status = ChangeRequestStatusValues.Draft,
            RequestedBy = access.Actor.Id,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var fileLinks = distinctFileIds.Select(fileId => new ChangeRequestFileEntity
        {
            Id = Guid.NewGuid(),
            ChangeRequestId = entity.Id,
            SourceRevisionFileId = fileId
        }).ToList();

        await using (var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            dbContext.ChangeRequests.Add(entity);
            dbContext.ChangeRequestFiles.AddRange(fileLinks);
            await dbContext.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }

        await serverAuthorization.WriteAuditEventAsync(
            access.Actor.Id,
            "CHANGE_REQUEST_CREATED",
            "CHANGE_REQUEST",
            entity.Id,
            "SUCCESS",
            new
            {
                integrationId = integration.Id,
                sourceRevisionId = sourceRevision.Id,
                changeTypeId = request.ChangeTypeId,
                affectedFilesCount = distinctFileIds.Count
            },
            cancellationToken);

        var dtos = await BuildChangeRequestDtosAsync([entity], integration, traceId, cancellationToken);
        return dtos[0];
    }

    public async Task<ChangeRequestDto> GetChangeRequestDetailsAsync(
        ClaimsPrincipal actorPrincipal,
        Guid requestId,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var cr = await dbContext.ChangeRequests.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == requestId, cancellationToken);
        if (cr is null)
        {
            throw ApiContractException.NotFound(
                messageAr: "طلب التغيير المطلوب غير موجود.",
                messageEn: "Change request was not found.");
        }

        var (_, integration) = await documentAuthorization.RequireIntegrationDocumentPermissionAsync(
            actorPrincipal,
            cr.IntegrationId,
            DocumentPermissionCatalog.View,
            "CHANGE_REQUEST_READ_DENIED",
            cancellationToken);

        var dtos = await BuildChangeRequestDtosAsync([cr], integration, traceId, cancellationToken);
        return dtos[0];
    }

    public async Task<ChangeRequestDto> UpdateChangeRequestAsync(
        ClaimsPrincipal actorPrincipal,
        Guid requestId,
        UpdateChangeRequestInput request,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var cr = await dbContext.ChangeRequests
            .FirstOrDefaultAsync(c => c.Id == requestId, cancellationToken);
        if (cr is null)
        {
            throw ApiContractException.NotFound(
                messageAr: "طلب التغيير المطلوب غير موجود.",
                messageEn: "Change request was not found.");
        }

        var (access, integration) = await documentAuthorization.RequireIntegrationDocumentPermissionAsync(
            actorPrincipal,
            cr.IntegrationId,
            DocumentPermissionCatalog.Change,
            "CHANGE_REQUEST_EDIT_DENIED",
            cancellationToken);

        await EnsureAssignedDeveloperAndNotArchivedAsync(access, integration, "CHANGE_REQUEST_EDIT_DENIED", cancellationToken);

        if (!IsEditableStatus(cr.Status))
        {
            throw ApiContractException.UnprocessableEntity(
                messageAr: $"لا يمكن تعديل طلب التغيير في حالة ({cr.Status}). التعديل متاح فقط في حالة DRAFT أو RETURNED.",
                messageEn: $"Cannot edit change request in '{cr.Status}' status (only DRAFT or RETURNED is editable).");
        }

        var distinctFileIds = request.AffectedSourceFileIds.Distinct().ToList();
        await ValidateChangeTypeAndFileScopeAsync(
            access.Document.ChangePolicyId,
            request.ChangeTypeId,
            cr.SourceRevisionId,
            distinctFileIds,
            cancellationToken);

        await using (var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            cr.ChangeTypeId = request.ChangeTypeId;
            cr.Reason = request.Reason.Trim();
            cr.ConsumerImpact = string.IsNullOrWhiteSpace(request.ConsumerImpact) ? null : request.ConsumerImpact.Trim();

            var existingLinks = await dbContext.ChangeRequestFiles
                .Where(f => f.ChangeRequestId == cr.Id)
                .ToListAsync(cancellationToken);
            dbContext.ChangeRequestFiles.RemoveRange(existingLinks);

            dbContext.ChangeRequestFiles.AddRange(distinctFileIds.Select(fileId => new ChangeRequestFileEntity
            {
                Id = Guid.NewGuid(),
                ChangeRequestId = cr.Id,
                SourceRevisionFileId = fileId
            }));

            await dbContext.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }

        await serverAuthorization.WriteAuditEventAsync(
            access.Actor.Id,
            "CHANGE_REQUEST_UPDATED",
            "CHANGE_REQUEST",
            cr.Id,
            "SUCCESS",
            new { integrationId = integration.Id, changeTypeId = request.ChangeTypeId, affectedFilesCount = distinctFileIds.Count },
            cancellationToken);

        var dtos = await BuildChangeRequestDtosAsync([cr], integration, traceId, cancellationToken);
        return dtos[0];
    }

    public async Task<SubmitChangeRequestResponse> SubmitChangeRequestAsync(
        ClaimsPrincipal actorPrincipal,
        Guid requestId,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var cr = await dbContext.ChangeRequests.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == requestId, cancellationToken);
        if (cr is null)
        {
            throw ApiContractException.NotFound(
                messageAr: "طلب التغيير المطلوب غير موجود.",
                messageEn: "Change request was not found.");
        }

        var (access, integration) = await documentAuthorization.RequireIntegrationDocumentPermissionAsync(
            actorPrincipal,
            cr.IntegrationId,
            DocumentPermissionCatalog.Change,
            "CHANGE_REQUEST_SUBMIT_DENIED",
            cancellationToken);

        await EnsureAssignedDeveloperAndNotArchivedAsync(access, integration, "CHANGE_REQUEST_SUBMIT_DENIED", cancellationToken);

        if (!IsEditableStatus(cr.Status))
        {
            throw ApiContractException.UnprocessableEntity(
                messageAr: $"لا يمكن إرسال طلب التغيير للاعتماد من الحالة الحالية ({cr.Status}).",
                messageEn: $"Change request cannot be submitted from status '{cr.Status}'.");
        }

        var hasAffectedFiles = await dbContext.ChangeRequestFiles.AsNoTracking()
            .AnyAsync(f => f.ChangeRequestId == cr.Id, cancellationToken);
        if (!hasAffectedFiles)
        {
            throw ApiContractException.UnprocessableEntity(
                messageAr: "يلزم ارتباط ملف متأثر واحد على الأقل قبل إرسال طلب التغيير (W05).",
                messageEn: "At least one affected source file is required before submitting a change request (W05).");
        }

        var changeType = await dbContext.ChangeTypes.AsNoTracking()
            .FirstOrDefaultAsync(ct => ct.Id == cr.ChangeTypeId, cancellationToken);
        if (changeType is null || !changeType.IsActive)
        {
            throw ApiContractException.UnprocessableEntity(
                messageAr: "نوع التغيير المرتبط بالطلب غير فعال.",
                messageEn: "Assigned change type is inactive or missing.");
        }

        Guid workflowRunId;
        await using (var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            var updated = await dbContext.ChangeRequests
                .Where(c => c.Id == cr.Id &&
                            (c.Status == ChangeRequestStatusValues.Draft || c.Status == ChangeRequestStatusValues.Returned))
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, ChangeRequestStatusValues.Submitted), cancellationToken);

            if (updated == 0)
            {
                throw ApiContractException.Conflict(
                    messageAr: "تم إرسال طلب التغيير بالتزامن بالفعل.",
                    messageEn: "Change request was submitted concurrently.");
            }

            // W06: Launch independent approval workflow run on ChangeType.RouteId
            workflowRunId = await workflowRunStarter.StartChangeRequestWorkflowRunAsync(
                cr.Id,
                changeType.RouteId,
                access.Actor.Id,
                integration.Id,
                cancellationToken);

            await tx.CommitAsync(cancellationToken);
        }

        await serverAuthorization.WriteAuditEventAsync(
            access.Actor.Id,
            "CHANGE_REQUEST_SUBMITTED",
            "CHANGE_REQUEST",
            cr.Id,
            "SUCCESS",
            new { integrationId = integration.Id, workflowRunId, routeId = changeType.RouteId },
            cancellationToken);

        var refreshed = await dbContext.ChangeRequests.AsNoTracking().FirstAsync(c => c.Id == cr.Id, cancellationToken);
        var dtos = await BuildChangeRequestDtosAsync([refreshed], integration, traceId, cancellationToken);
        return new SubmitChangeRequestResponse(dtos[0], workflowRunId, traceId);
    }

    private async Task EnsureAssignedDeveloperAndNotArchivedAsync(
        AuthorizedDocumentAccess access,
        IntegrationEntity integration,
        string auditOp,
        CancellationToken cancellationToken)
    {
        if (string.Equals(access.Document.State, "ARCHIVED", StringComparison.OrdinalIgnoreCase))
        {
            throw ApiContractException.UnprocessableEntity(
                messageAr: "الوثيقة مؤرشفة؛ لا يُسمح بإنشاء أو تعديل طلبات تغيير.",
                messageEn: "API document is ARCHIVED; change requests cannot be modified.");
        }

        var isAssigned = await documentAuthorization.IsUserAssignedDeveloperAsync(
            access.Actor.Id, integration.DocumentId, integration.Id, cancellationToken);
        if (!isAssigned)
        {
            await serverAuthorization.WriteAuditEventAsync(
                access.Actor.Id,
                auditOp,
                "INTEGRATION",
                integration.Id,
                "DENIED",
                new { reason = "NOT_ASSIGNED_DEVELOPER" },
                cancellationToken);

            throw ApiContractException.Forbidden(
                messageAr: "هذا الإجراء مقتصر على المطوّرين المكلّفين على هذا التكامل.",
                messageEn: "This action requires being an assigned developer on this integration.");
        }
    }

    private async Task ValidateChangeTypeAndFileScopeAsync(
        Guid policyId,
        Guid changeTypeId,
        Guid sourceRevisionId,
        IReadOnlyList<Guid> distinctFileIds,
        CancellationToken cancellationToken)
    {
        var typeAllowed = await (
            from cpt in dbContext.ChangePolicyTypes.AsNoTracking()
            join ct in dbContext.ChangeTypes.AsNoTracking() on cpt.ChangeTypeId equals ct.Id
            where cpt.PolicyId == policyId && cpt.ChangeTypeId == changeTypeId && ct.IsActive
            select ct.Id)
            .AnyAsync(cancellationToken);

        if (!typeAllowed)
        {
            throw ApiContractException.UnprocessableEntity(
                messageAr: "نوع التغيير المحدد غير مسموح به ضمن سياسة التغيير المعتمدة لهذه الوثيقة.",
                messageEn: "Selected change type is not allowed by the API document's change policy.");
        }

        var validCount = await dbContext.RevisionFiles.AsNoTracking()
            .CountAsync(rf => rf.RevisionId == sourceRevisionId && distinctFileIds.Contains(rf.Id), cancellationToken);

        if (validCount != distinctFileIds.Count)
        {
            throw ApiContractException.UnprocessableEntity(
                messageAr: "أحد الملفات المتأثرة المحددة لا ينتمي إلى المراجعة المعتمدة المستهدفة (W05).",
                messageEn: "One or more AffectedSourceFileIds do not belong to the target APPROVED source revision (W05).");
        }
    }

    private async Task<IReadOnlyList<ChangeRequestDto>> BuildChangeRequestDtosAsync(
        IReadOnlyList<ChangeRequestEntity> crs,
        IntegrationEntity integration,
        string traceId,
        CancellationToken cancellationToken)
    {
        if (crs.Count == 0)
        {
            return [];
        }

        var crIds = crs.Select(c => c.Id).ToList();
        var revIds = crs.Select(c => c.SourceRevisionId).Distinct().ToList();
        var typeIds = crs.Select(c => c.ChangeTypeId).Distinct().ToList();
        var userIds = crs.Select(c => c.RequestedBy).Distinct().ToList();

        var revisions = await (
            from rev in dbContext.DocumentRevisions.AsNoTracking()
            join ver in dbContext.ApiVersions.AsNoTracking() on rev.ApiVersionId equals ver.Id
            where revIds.Contains(rev.Id)
            select new { rev.Id, rev.RevisionNumber, ver.VersionLabel })
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var types = await (
            from ct in dbContext.ChangeTypes.AsNoTracking()
            join route in dbContext.ApprovalRoutes.AsNoTracking() on ct.RouteId equals route.Id
            where typeIds.Contains(ct.Id)
            select new { ct.Id, ct.Name, ct.RouteId, RouteName = route.Name })
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var users = await dbContext.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, cancellationToken);

        var filesByCr = (await (
            from crf in dbContext.ChangeRequestFiles.AsNoTracking()
            join rf in dbContext.RevisionFiles.AsNoTracking() on crf.SourceRevisionFileId equals rf.Id
            join fo in dbContext.FileObjects.AsNoTracking() on rf.ObjectId equals fo.Id
            join ft in dbContext.FileTypes.AsNoTracking() on rf.FileTypeId equals ft.Id
            where crIds.Contains(crf.ChangeRequestId)
            select new
            {
                crf.ChangeRequestId,
                Dto = new AffectedSourceFileDto(rf.Id, rf.OriginalName, ft.DisplayName, fo.Sha256, fo.SizeBytes)
            })
            .ToListAsync(cancellationToken))
            .GroupBy(x => x.ChangeRequestId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<AffectedSourceFileDto>)g.Select(x => x.Dto).ToList());

        var latestRunsByCr = (await dbContext.WorkflowRuns.AsNoTracking()
            .Where(r => r.ChangeRequestId.HasValue && crIds.Contains(r.ChangeRequestId.Value))
            .OrderByDescending(r => r.StartedAt)
            .ToListAsync(cancellationToken))
            .GroupBy(r => r.ChangeRequestId!.Value)
            .ToDictionary(g => g.Key, g => g.First());

        var runIds = latestRunsByCr.Values.Select(r => r.Id).ToList();
        var latestNotes = runIds.Count == 0
            ? new Dictionary<Guid, string>()
            : (await (
                from st in dbContext.WorkflowRunStages.AsNoTracking()
                join dec in dbContext.WorkflowDecisions.AsNoTracking() on st.Id equals dec.RunStageId
                where runIds.Contains(st.RunId) && dec.Notes != null
                orderby dec.DecidedAt descending
                select new { st.RunId, dec.Notes })
                .ToListAsync(cancellationToken))
                .GroupBy(x => x.RunId)
                .ToDictionary(g => g.Key, g => g.First().Notes!);

        return crs.Select(cr =>
        {
            revisions.TryGetValue(cr.SourceRevisionId, out var revInfo);
            types.TryGetValue(cr.ChangeTypeId, out var typeInfo);
            latestRunsByCr.TryGetValue(cr.Id, out var run);

            return new ChangeRequestDto(
                cr.Id,
                cr.IntegrationId,
                integration.DocumentId,
                integration.ReferenceCode,
                cr.SourceRevisionId,
                revInfo?.RevisionNumber ?? 0,
                revInfo?.VersionLabel ?? string.Empty,
                cr.ChangeTypeId,
                typeInfo?.Name ?? string.Empty,
                typeInfo?.RouteId ?? Guid.Empty,
                typeInfo?.RouteName ?? string.Empty,
                cr.Reason,
                cr.ConsumerImpact,
                cr.Status,
                IsEditableStatus(cr.Status),
                cr.RequestedBy,
                users.GetValueOrDefault(cr.RequestedBy, string.Empty),
                cr.CreatedAt,
                run?.Id,
                run?.Status,
                run is not null && latestNotes.TryGetValue(run.Id, out var note) ? note : null,
                filesByCr.GetValueOrDefault(cr.Id, []),
                traceId);
        }).ToList();
    }

    private async Task<string> BuildDerivedTitleAsync(ApiDocumentEntity doc, CancellationToken cancellationToken)
    {
        var provider = doc.ProviderInternalSystemId.HasValue
            ? await (
                from ips in dbContext.InternalProviderSystems.AsNoTracking()
                join cs in dbContext.CompanySystems.AsNoTracking() on ips.CompanySystemId equals cs.Id
                where ips.Id == doc.ProviderInternalSystemId.Value
                select cs.Name).FirstOrDefaultAsync(cancellationToken) ?? "Provider"
            : doc.ProviderExternalSystemId.HasValue
                ? await dbContext.ExternalProviderSystems.AsNoTracking()
                    .Where(e => e.Id == doc.ProviderExternalSystemId.Value)
                    .Select(e => e.Name).FirstOrDefaultAsync(cancellationToken) ?? "External Provider"
                : "Provider";

        var consumer = doc.ConsumerCompanySystemId.HasValue
            ? await dbContext.CompanySystems.AsNoTracking()
                .Where(c => c.Id == doc.ConsumerCompanySystemId.Value)
                .Select(c => c.Name).FirstOrDefaultAsync(cancellationToken) ?? "Consumer"
            : doc.ConsumerEntityId.HasValue
                ? await dbContext.ConsumerEntities.AsNoTracking()
                    .Where(c => c.Id == doc.ConsumerEntityId.Value)
                    .Select(c => c.Name).FirstOrDefaultAsync(cancellationToken) ?? "Consumer"
                : "Consumer";

        return $"{provider} ← {consumer}";
    }
}
