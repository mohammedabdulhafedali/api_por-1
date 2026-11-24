using System.Security.Claims;
using System.Security.Cryptography;
using ApiGovernance.Modules.Integrations.Contracts;
using ApiGovernance.Shared.Contracts;
using ApiGovernance.Shared.Persistence;
using ApiGovernance.Shared.Persistence.Entities;
using ApiGovernance.Shared.Security;
using Microsoft.EntityFrameworkCore;

namespace ApiGovernance.Modules.Integrations.Services;

public interface IIntegrationManagementService
{
    Task<IReadOnlyList<EligibleIntegrationDocumentDto>> GetEligibleDocumentsAsync(
        ClaimsPrincipal actorPrincipal,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<IntegrationPartyOptionDto>> GetAssignableUsersAsync(
        CancellationToken cancellationToken = default);

    Task<PagedResult<IntegrationDto>> GetIntegrationsAsync(
        ClaimsPrincipal actorPrincipal,
        int? page,
        int? pageSize,
        string? search,
        bool? assignedToMe,
        string? state,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<IntegrationDetailsDto> GetIntegrationDetailsAsync(
        ClaimsPrincipal actorPrincipal,
        Guid integrationId,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<IntegrationDevelopersResponse> GetIntegrationDevelopersAsync(
        ClaimsPrincipal actorPrincipal,
        Guid integrationId,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<IntegrationDevelopersResponse> ReplaceIntegrationDevelopersAsync(
        ClaimsPrincipal actorPrincipal,
        Guid integrationId,
        ReplaceIntegrationDevelopersRequest request,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<IntegrationDto> CreateIntegrationAsync(
        ClaimsPrincipal actorPrincipal,
        CreateIntegrationRequest request,
        string traceId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Implements Tasks D04 (1:1 Integration Creation), D05 (Developer Responsibility & Scope Management
/// strictly decoupled from Document ACL permissions), and D06 (Scoped Directory & Multi-Tab Details).
/// </summary>
public sealed class IntegrationManagementService(
    ApiGovernanceDbContext dbContext,
    IServerAuthorizationService serverAuthorization,
    IDocumentAuthorizationService documentAuthorization) : IIntegrationManagementService
{
    private const string ArchivedState = "ARCHIVED";

    public async Task<IReadOnlyList<EligibleIntegrationDocumentDto>> GetEligibleDocumentsAsync(
        ClaimsPrincipal actorPrincipal,
        CancellationToken cancellationToken = default)
    {
        var actor = await serverAuthorization.RequireActiveAuthenticatedUserAsync(actorPrincipal, cancellationToken);

        var linkedDocIds = await dbContext.Integrations.AsNoTracking()
            .Select(i => i.DocumentId)
            .ToHashSetAsync(cancellationToken);

        var candidateDocs = await dbContext.ApiDocuments.AsNoTracking()
            .Where(d => !linkedDocIds.Contains(d.Id) && d.State != ArchivedState)
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync(cancellationToken);

        var docIds = candidateDocs.Select(d => d.Id).ToList();
        var accessMap = await documentAuthorization.EvaluateDocumentPermissionsBatchAsync(actor.Id, docIds, cancellationToken);

        var authorizedDocs = candidateDocs.Where(d =>
            accessMap.TryGetValue(d.Id, out var map) &&
            map.TryGetValue(DocumentPermissionCatalog.View, out var v) && v.Allowed &&
            map.TryGetValue(DocumentPermissionCatalog.ManageIntegration, out var m) && m.Allowed)
            .ToList();

        if (authorizedDocs.Count == 0)
        {
            return [];
        }

        var (compMap, extMap, consMap, groupMap) = await LoadPartyLookupsAsync(authorizedDocs, cancellationToken);

        return authorizedDocs.Select(d =>
        {
            var providerName = d.ProviderInternalSystemId.HasValue
                ? compMap.GetValueOrDefault(d.ProviderInternalSystemId.Value, string.Empty)
                : extMap.GetValueOrDefault(d.ProviderExternalSystemId!.Value, string.Empty);

            var consumerName = d.ConsumerCompanySystemId.HasValue
                ? compMap.GetValueOrDefault(d.ConsumerCompanySystemId.Value, string.Empty)
                : consMap.GetValueOrDefault(d.ConsumerEntityId!.Value, string.Empty);

            return new EligibleIntegrationDocumentDto(
                d.Id,
                $"{providerName} ← {consumerName}",
                d.ProviderInternalSystemId.HasValue ? "INTERNAL" : "EXTERNAL",
                providerName,
                d.ConsumerCompanySystemId.HasValue ? "COMPANY_SYSTEM" : "CONSUMER_ENTITY",
                consumerName,
                d.ApiGroupId,
                groupMap.GetValueOrDefault(d.ApiGroupId, string.Empty),
                d.State);
        }).ToList();
    }

    public async Task<IReadOnlyList<IntegrationPartyOptionDto>> GetAssignableUsersAsync(
        CancellationToken cancellationToken = default)
    {
        return await dbContext.Users.AsNoTracking()
            .Where(u => u.IsActive)
            .OrderBy(u => u.FullName)
            .Select(u => new IntegrationPartyOptionDto(u.Id, u.FullName, u.Email))
            .ToListAsync(cancellationToken);
    }

    public async Task<PagedResult<IntegrationDto>> GetIntegrationsAsync(
        ClaimsPrincipal actorPrincipal,
        int? page,
        int? pageSize,
        string? search,
        bool? assignedToMe,
        string? state,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var actor = await serverAuthorization.RequireActiveAuthenticatedUserAsync(actorPrincipal, cancellationToken);

        var integrations = await dbContext.Integrations.AsNoTracking()
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(cancellationToken);

        var docIds = integrations.Select(i => i.DocumentId).Distinct().ToList();
        var accessMap = await documentAuthorization.EvaluateDocumentPermissionsBatchAsync(actor.Id, docIds, cancellationToken);

        // Enforce strict Document ACL visibility (anti-IDOR): return only integrations where actor has doc.view
        var visibleIntegrations = integrations
            .Where(i => accessMap.TryGetValue(i.DocumentId, out var map) &&
                        map.TryGetValue(DocumentPermissionCatalog.View, out var res) &&
                        res.Allowed)
            .ToList();

        var dtos = await BuildIntegrationDtosAsync(actor.Id, visibleIntegrations, accessMap, traceId, cancellationToken);

        if (assignedToMe == true)
        {
            dtos = dtos.Where(i => i.IsCurrentUserAssignedDeveloper).ToList();
        }

        if (!string.IsNullOrWhiteSpace(state))
        {
            var normalizedState = state.Trim().ToUpperInvariant();
            dtos = dtos.Where(i => string.Equals(i.DocumentState, normalizedState, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            dtos = dtos.Where(i =>
                i.ReferenceCode.ToLowerInvariant().Contains(term) ||
                i.DerivedTitle.ToLowerInvariant().Contains(term) ||
                i.ApiGroupName.ToLowerInvariant().Contains(term) ||
                i.ProviderName.ToLowerInvariant().Contains(term) ||
                i.ConsumerName.ToLowerInvariant().Contains(term)).ToList();
        }

        var totalCount = dtos.Count;
        var (_, safePageSize, totalPages, normalizedPage) = PaginationHelper.Compute(page, pageSize, totalCount);
        var pagedItems = dtos.Skip((normalizedPage - 1) * safePageSize).Take(safePageSize).ToList();

        return new PagedResult<IntegrationDto>(pagedItems, normalizedPage, safePageSize, totalCount, totalPages);
    }

    public async Task<IntegrationDetailsDto> GetIntegrationDetailsAsync(
        ClaimsPrincipal actorPrincipal,
        Guid integrationId,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var (access, integration) = await documentAuthorization.RequireIntegrationDocumentPermissionAsync(
            actorPrincipal,
            integrationId,
            DocumentPermissionCatalog.View,
            "INTEGRATION_READ_DENIED",
            cancellationToken);

        var summaryList = await BuildIntegrationDtosAsync(
            access.Actor.Id,
            [integration],
            new Dictionary<Guid, IReadOnlyDictionary<string, DocumentPermissionResolution>>
            {
                [integration.DocumentId] = access.EffectivePermissions
            },
            traceId,
            cancellationToken);
        var summary = summaryList[0];

        var doc = access.Document;

        // 1. Document Developers (with effective ACL readiness for UI-09 Developers & Scope tab)
        var docDevUsers = await (
            from dd in dbContext.DocumentDevelopers.AsNoTracking()
            join u in dbContext.Users.AsNoTracking() on dd.UserId equals u.Id
            where dd.DocumentId == doc.Id
            orderby u.FullName
            select u)
            .ToListAsync(cancellationToken);

        var docDevelopers = await ProjectDeveloperDtosWithAclAsync(docDevUsers, doc.Id, cancellationToken);

        // 2. Linked Requirement Template Rules (for UI-09 Files tab)
        var reqRuleRows = await (
            from r in dbContext.FileRequirementRules.AsNoTracking()
            join ft in dbContext.FileTypes.AsNoTracking() on r.FileTypeId equals ft.Id
            where r.TemplateId == doc.RequirementTemplateId
            orderby ft.DisplayName
            select new { r.FileTypeId, ft.DisplayName, ft.CatalogCode, r.RequirementMode, r.MinCount, r.MaxCount })
            .ToListAsync(cancellationToken);

        var ftIds = reqRuleRows.Select(x => x.FileTypeId).Distinct().ToList();
        var ftExts = ftIds.Count == 0
            ? []
            : await dbContext.FileTypeExtensions.AsNoTracking()
                .Where(e => ftIds.Contains(e.FileTypeId))
                .OrderBy(e => e.Extension)
                .ToListAsync(cancellationToken);
        var extMapByFt = ftExts.GroupBy(e => e.FileTypeId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(x => x.Extension).ToList());

        var requirementRules = reqRuleRows.Select(x => new IntegrationRequirementRuleSummaryDto(
            x.FileTypeId,
            x.DisplayName,
            x.CatalogCode,
            extMapByFt.GetValueOrDefault(x.FileTypeId, []),
            x.RequirementMode,
            x.MinCount,
            x.MaxCount)).ToList();

        // 3. Linked Approval Route Stages (for UI-09 Reviews/Workflow tab)
        var approvalStages = await (
            from s in dbContext.ApprovalRouteStages.AsNoTracking()
            join role in dbContext.Roles.AsNoTracking() on s.ResponsibleRoleId equals role.Id
            where s.RouteId == doc.ApprovalRouteId
            orderby s.Position
            select new IntegrationApprovalStageSummaryDto(s.Position, s.StageName, role.Id, role.Name))
            .ToListAsync(cancellationToken);

        // 4. Linked Change Policy Allowed Types (for UI-09 Change Requests tab)
        var allowedChangeTypes = await (
            from cpt in dbContext.ChangePolicyTypes.AsNoTracking()
            join ct in dbContext.ChangeTypes.AsNoTracking() on cpt.ChangeTypeId equals ct.Id
            join route in dbContext.ApprovalRoutes.AsNoTracking() on ct.RouteId equals route.Id
            where cpt.PolicyId == doc.ChangePolicyId && ct.IsActive
            orderby ct.Name
            select new IntegrationChangeTypeSummaryDto(ct.Id, ct.Name, route.Id, route.Name))
            .ToListAsync(cancellationToken);

        // 5. Counts & Recent Audit Events (for UI-09 History & Publishing tab)
        var versionIds = await dbContext.ApiVersions.AsNoTracking()
            .Where(v => v.IntegrationId == integration.Id)
            .Select(v => v.Id)
            .ToListAsync(cancellationToken);

        var revisionsCount = versionIds.Count == 0
            ? 0
            : await dbContext.DocumentRevisions.AsNoTracking()
                .CountAsync(r => versionIds.Contains(r.ApiVersionId), cancellationToken);

        var changeRequestsCount = await dbContext.ChangeRequests.AsNoTracking()
            .CountAsync(cr => cr.IntegrationId == integration.Id, cancellationToken);

        var publicationsCount = versionIds.Count == 0
            ? 0
            : await (
                from p in dbContext.Publications.AsNoTracking()
                join rev in dbContext.DocumentRevisions.AsNoTracking() on p.RevisionId equals rev.Id
                where versionIds.Contains(rev.ApiVersionId)
                select p.Id)
                .CountAsync(cancellationToken);

        var auditRows = await dbContext.AuditEvents.AsNoTracking()
            .Where(a => a.ResourceId == integration.Id || a.ResourceId == doc.Id)
            .OrderByDescending(a => a.RecordedAt)
            .Take(15)
            .ToListAsync(cancellationToken);

        var actorIds = auditRows.Where(a => a.ActorUserId.HasValue).Select(a => a.ActorUserId!.Value).Distinct().ToList();
        var actorNames = actorIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await dbContext.Users.AsNoTracking()
                .Where(u => actorIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.FullName, cancellationToken);

        var recentAudit = auditRows.Select(a => new IntegrationAuditEventSummaryDto(
            a.Id,
            a.Operation,
            a.ResourceKind,
            a.Result,
            a.ActorUserId,
            a.ActorUserId.HasValue ? actorNames.GetValueOrDefault(a.ActorUserId.Value, "System") : "System",
            a.RecordedAt)).ToList();

        return new IntegrationDetailsDto(
            summary,
            docDevelopers,
            requirementRules,
            approvalStages,
            allowedChangeTypes,
            revisionsCount,
            changeRequestsCount,
            publicationsCount,
            recentAudit,
            traceId);
    }

    public async Task<IntegrationDevelopersResponse> GetIntegrationDevelopersAsync(
        ClaimsPrincipal actorPrincipal,
        Guid integrationId,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var (access, integration) = await documentAuthorization.RequireIntegrationDocumentPermissionAsync(
            actorPrincipal,
            integrationId,
            DocumentPermissionCatalog.View,
            "INTEGRATION_DEVELOPERS_READ_DENIED",
            cancellationToken);

        var summaryList = await BuildIntegrationDtosAsync(
            access.Actor.Id,
            [integration],
            new Dictionary<Guid, IReadOnlyDictionary<string, DocumentPermissionResolution>>
            {
                [integration.DocumentId] = access.EffectivePermissions
            },
            traceId,
            cancellationToken);

        var intDevs = summaryList[0].AssignedDevelopers;

        var docDevUsers = await (
            from dd in dbContext.DocumentDevelopers.AsNoTracking()
            join u in dbContext.Users.AsNoTracking() on dd.UserId equals u.Id
            where dd.DocumentId == integration.DocumentId
            orderby u.FullName
            select u)
            .ToListAsync(cancellationToken);

        var docDevs = await ProjectDeveloperDtosWithAclAsync(docDevUsers, integration.DocumentId, cancellationToken);

        return new IntegrationDevelopersResponse(
            integration.Id,
            integration.DocumentId,
            integration.ReferenceCode,
            summaryList[0].DerivedTitle,
            intDevs.Any(d => d.UserId == access.Actor.Id),
            docDevs.Any(d => d.UserId == access.Actor.Id),
            intDevs,
            docDevs,
            traceId);
    }

    public async Task<IntegrationDevelopersResponse> ReplaceIntegrationDevelopersAsync(
        ClaimsPrincipal actorPrincipal,
        Guid integrationId,
        ReplaceIntegrationDevelopersRequest request,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var (access, integration) = await documentAuthorization.RequireIntegrationDocumentPermissionAsync(
            actorPrincipal,
            integrationId,
            DocumentPermissionCatalog.Assign,
            "INTEGRATION_DEVELOPERS_DENIED",
            cancellationToken);

        var developerIds = await ValidateActiveUserIdsAsync(request.DeveloperUserIds ?? [], cancellationToken);

        await using var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var existing = await dbContext.IntegrationDevelopers
            .Where(idv => idv.IntegrationId == integrationId)
            .ToListAsync(cancellationToken);

        dbContext.IntegrationDevelopers.RemoveRange(existing);
        await dbContext.SaveChangesAsync(cancellationToken);

        foreach (var uid in developerIds)
        {
            dbContext.IntegrationDevelopers.Add(new IntegrationDeveloperEntity
            {
                Id = Guid.NewGuid(),
                IntegrationId = integrationId,
                UserId = uid
            });
        }
        await dbContext.SaveChangesAsync(cancellationToken);

        // Developer assignment strictly updates integration_developers ONLY; Document ACL permissions remain separate per ADR-01.
        await serverAuthorization.WriteAuditEventAsync(
            access.Actor.Id,
            "INTEGRATION_DEVELOPERS_REPLACED",
            "INTEGRATION",
            integration.Id,
            "SUCCESS",
            new { integration.DocumentId, integration.ReferenceCode, developerUserIds = developerIds },
            cancellationToken);

        await tx.CommitAsync(cancellationToken);

        return await GetIntegrationDevelopersAsync(actorPrincipal, integrationId, traceId, cancellationToken);
    }

    public async Task<IntegrationDto> CreateIntegrationAsync(
        ClaimsPrincipal actorPrincipal,
        CreateIntegrationRequest request,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var access = await documentAuthorization.RequireDocumentPermissionAsync(
            actorPrincipal,
            request.DocumentId,
            DocumentPermissionCatalog.ManageIntegration,
            "INTEGRATION_CREATE_DENIED",
            cancellationToken);

        if (string.Equals(access.Document.State, ArchivedState, StringComparison.OrdinalIgnoreCase))
        {
            throw ApiContractException.UnprocessableEntity(
                messageAr: "لا يمكن إنشاء تكامل لوثيقة API مؤرشفة.",
                messageEn: "Cannot create an integration for an archived API document.");
        }

        var existingForDocument = await dbContext.Integrations.AsNoTracking()
            .AnyAsync(i => i.DocumentId == request.DocumentId, cancellationToken);
        if (existingForDocument)
        {
            throw ApiContractException.Conflict(
                messageAr: "هذه الوثيقة مرتبطة مسبقًا بتكامل (يُسمح بتكامل واحد فقط لكل وثيقة 1:1).",
                messageEn: "An integration already exists for this API document (1:1 limit).");
        }

        var developerIds = (request.DeveloperUserIds ?? []).Where(id => id != Guid.Empty).Distinct().ToList();
        if (developerIds.Count > 0)
        {
            if (!access.EffectivePermissions.TryGetValue(DocumentPermissionCatalog.Assign, out var assignRes) || !assignRes.Allowed)
            {
                throw ApiContractException.Forbidden(
                    messageAr: "تعيين مطورين أثناء إنشاء التكامل يتطلب صلاحية الوثيقة doc.assign.",
                    messageEn: "Assigning developers during integration creation requires document permission 'doc.assign'.");
            }

            developerIds = await ValidateActiveUserIdsAsync(developerIds, cancellationToken);
        }

        string referenceCode;
        if (!string.IsNullOrWhiteSpace(request.ReferenceCode))
        {
            referenceCode = request.ReferenceCode.Trim().ToUpperInvariant();
            var dupRef = await dbContext.Integrations.AsNoTracking()
                .AnyAsync(i => i.ReferenceCode.ToLower() == referenceCode.ToLower(), cancellationToken);
            if (dupRef)
            {
                throw ApiContractException.Conflict(
                    messageAr: $"الرمز المرجعي للتكامل ({referenceCode}) مستخدم مسبقًا.",
                    messageEn: $"Integration reference code '{referenceCode}' already exists.");
            }
        }
        else
        {
            referenceCode = await GenerateUniqueReferenceCodeAsync(cancellationToken);
        }

        var entity = new IntegrationEntity
        {
            Id = Guid.NewGuid(),
            DocumentId = request.DocumentId,
            ReferenceCode = referenceCode,
            CreatedBy = access.Actor.Id,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await using var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        dbContext.Integrations.Add(entity);
        foreach (var devId in developerIds)
        {
            dbContext.IntegrationDevelopers.Add(new IntegrationDeveloperEntity
            {
                Id = Guid.NewGuid(),
                IntegrationId = entity.Id,
                UserId = devId
            });
        }
        await dbContext.SaveChangesAsync(cancellationToken);

        await serverAuthorization.WriteAuditEventAsync(
            access.Actor.Id,
            "INTEGRATION_CREATED",
            "INTEGRATION",
            entity.Id,
            "SUCCESS",
            new
            {
                entity.DocumentId,
                entity.ReferenceCode,
                developerUserIds = developerIds
            },
            cancellationToken);

        await tx.CommitAsync(cancellationToken);

        var details = await GetIntegrationDetailsAsync(actorPrincipal, entity.Id, traceId, cancellationToken);
        return details.Summary;
    }

    private async Task<List<Guid>> ValidateActiveUserIdsAsync(
        IReadOnlyList<Guid> userIds,
        CancellationToken cancellationToken)
    {
        var distinct = userIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (distinct.Count == 0)
        {
            return [];
        }

        var users = await dbContext.Users.AsNoTracking()
            .Where(u => distinct.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, cancellationToken);

        foreach (var uid in distinct)
        {
            if (!users.TryGetValue(uid, out var user) || !user.IsActive)
            {
                throw ApiContractException.UnprocessableEntity(
                    messageAr: "أحد المطورين المحددين غير موجود أو معطّل.",
                    messageEn: "One of the selected developers was not found or is inactive.");
            }
        }

        return distinct;
    }

    private async Task<string> GenerateUniqueReferenceCodeAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var datePart = DateTimeOffset.UtcNow.ToString("yyyyMMdd");
            var randomSuffix = Convert.ToHexString(RandomNumberGenerator.GetBytes(3));
            var candidate = $"INT-{datePart}-{randomSuffix}";
            var exists = await dbContext.Integrations.AsNoTracking()
                .AnyAsync(i => i.ReferenceCode == candidate, cancellationToken);
            if (!exists)
            {
                return candidate;
            }
        }

        return $"INT-{Guid.NewGuid():N}"[..24].ToUpperInvariant();
    }

    private async Task<IReadOnlyList<IntegrationDeveloperDto>> ProjectDeveloperDtosWithAclAsync(
        IReadOnlyList<UserEntity> users,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        if (users.Count == 0)
        {
            return [];
        }

        var result = new List<IntegrationDeveloperDto>(users.Count);
        foreach (var u in users)
        {
            var batch = await documentAuthorization.EvaluateDocumentPermissionsBatchAsync(u.Id, [documentId], cancellationToken);
            var map = batch[documentId];

            bool Has(string code) => map.TryGetValue(code, out var res) && res.Allowed;

            result.Add(new IntegrationDeveloperDto(
                u.Id,
                u.FullName,
                u.Email,
                u.IsActive,
                HasDocView: Has(DocumentPermissionCatalog.View),
                HasDocVersionManage: Has(DocumentPermissionCatalog.VersionManage),
                HasDocFilesManage: Has(DocumentPermissionCatalog.FilesManage),
                HasDocChange: Has(DocumentPermissionCatalog.Change),
                HasDocAssign: Has(DocumentPermissionCatalog.Assign)));
        }

        return result;
    }

    private async Task<(
        Dictionary<Guid, string> CompMap,
        Dictionary<Guid, string> ExtMap,
        Dictionary<Guid, string> ConsMap,
        Dictionary<Guid, string> GroupMap)> LoadPartyLookupsAsync(
        IReadOnlyCollection<ApiDocumentEntity> documents,
        CancellationToken cancellationToken)
    {
        var compIds = documents.SelectMany(d => new[] { d.ProviderInternalSystemId, d.ConsumerCompanySystemId })
            .Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();
        var extIds = documents.Where(d => d.ProviderExternalSystemId.HasValue).Select(d => d.ProviderExternalSystemId!.Value).Distinct().ToList();
        var consIds = documents.Where(d => d.ConsumerEntityId.HasValue).Select(d => d.ConsumerEntityId!.Value).Distinct().ToList();
        var groupIds = documents.Select(d => d.ApiGroupId).Distinct().ToList();

        var compMap = compIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await dbContext.CompanySystems.AsNoTracking().Where(c => compIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);
        var extMap = extIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await dbContext.ExternalProviderSystems.AsNoTracking().Where(e => extIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.Name, cancellationToken);
        var consMap = consIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await dbContext.ConsumerEntities.AsNoTracking().Where(c => consIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);
        var groupMap = groupIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await dbContext.ApiGroups.AsNoTracking().Where(g => groupIds.Contains(g.Id)).ToDictionaryAsync(g => g.Id, g => g.Name, cancellationToken);

        return (compMap, extMap, consMap, groupMap);
    }

    private async Task<List<IntegrationDto>> BuildIntegrationDtosAsync(
        Guid currentUserId,
        IReadOnlyList<IntegrationEntity> integrations,
        IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, DocumentPermissionResolution>> accessMap,
        string traceId,
        CancellationToken cancellationToken)
    {
        if (integrations.Count == 0)
        {
            return [];
        }

        var intIds = integrations.Select(i => i.Id).ToList();
        var docIds = integrations.Select(i => i.DocumentId).Distinct().ToList();

        var docsMap = await dbContext.ApiDocuments.AsNoTracking()
            .Where(d => docIds.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, cancellationToken);

        var docs = docsMap.Values.ToList();
        var (compMap, extMap, consMap, groupMap) = await LoadPartyLookupsAsync(docs, cancellationToken);

        var tplIds = docs.Select(d => d.RequirementTemplateId).Distinct().ToList();
        var routeIds = docs.Select(d => d.ApprovalRouteId).Distinct().ToList();
        var policyIds = docs.Select(d => d.ChangePolicyId).Distinct().ToList();
        var creatorIds = integrations.Select(i => i.CreatedBy).Distinct().ToList();

        var tplMap = await dbContext.FileRequirementTemplates.AsNoTracking().Where(t => tplIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Name, cancellationToken);
        var routeMap = await dbContext.ApprovalRoutes.AsNoTracking().Where(r => routeIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Name, cancellationToken);
        var policyMap = await dbContext.ChangePolicies.AsNoTracking().Where(p => policyIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name, cancellationToken);
        var creatorMap = await dbContext.Users.AsNoTracking().Where(u => creatorIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, cancellationToken);

        var versionCounts = await dbContext.ApiVersions.AsNoTracking()
            .Where(v => intIds.Contains(v.IntegrationId))
            .GroupBy(v => v.IntegrationId)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, cancellationToken);

        var devUserRows = await (
            from idv in dbContext.IntegrationDevelopers.AsNoTracking()
            join u in dbContext.Users.AsNoTracking() on idv.UserId equals u.Id
            where intIds.Contains(idv.IntegrationId)
            orderby u.FullName
            select new { idv.IntegrationId, User = u })
            .ToListAsync(cancellationToken);

        var docDevAssignedToCurrentSet = await dbContext.DocumentDevelopers.AsNoTracking()
            .Where(dd => docIds.Contains(dd.DocumentId) && dd.UserId == currentUserId)
            .Select(dd => dd.DocumentId)
            .ToHashSetAsync(cancellationToken);

        var devsByInt = new Dictionary<Guid, IReadOnlyList<IntegrationDeveloperDto>>();
        foreach (var grp in devUserRows.GroupBy(x => x.IntegrationId))
        {
            var docId = integrations.First(i => i.Id == grp.Key).DocumentId;
            devsByInt[grp.Key] = await ProjectDeveloperDtosWithAclAsync(
                grp.Select(x => x.User).ToList(),
                docId,
                cancellationToken);
        }

        return integrations.Select(i =>
        {
            var doc = docsMap[i.DocumentId];
            var providerName = doc.ProviderInternalSystemId.HasValue
                ? compMap.GetValueOrDefault(doc.ProviderInternalSystemId.Value, string.Empty)
                : extMap.GetValueOrDefault(doc.ProviderExternalSystemId!.Value, string.Empty);

            var consumerName = doc.ConsumerCompanySystemId.HasValue
                ? compMap.GetValueOrDefault(doc.ConsumerCompanySystemId.Value, string.Empty)
                : consMap.GetValueOrDefault(doc.ConsumerEntityId!.Value, string.Empty);

            var devs = devsByInt.GetValueOrDefault(i.Id, []);
            var isAssigned = devs.Any(d => d.UserId == currentUserId) || docDevAssignedToCurrentSet.Contains(doc.Id);

            IReadOnlyList<string> myPerms = accessMap.TryGetValue(doc.Id, out var map)
                ? map.Where(kv => kv.Value.Allowed).Select(kv => kv.Key).OrderBy(k => k, StringComparer.Ordinal).ToList()
                : [];

            return new IntegrationDto(
                i.Id,
                i.DocumentId,
                i.ReferenceCode,
                $"{providerName} ← {consumerName}",
                doc.ProviderInternalSystemId.HasValue ? "INTERNAL" : "EXTERNAL",
                providerName,
                doc.ConsumerCompanySystemId.HasValue ? "COMPANY_SYSTEM" : "CONSUMER_ENTITY",
                consumerName,
                doc.ApiGroupId,
                groupMap.GetValueOrDefault(doc.ApiGroupId, string.Empty),
                tplMap.GetValueOrDefault(doc.RequirementTemplateId, string.Empty),
                routeMap.GetValueOrDefault(doc.ApprovalRouteId, string.Empty),
                policyMap.GetValueOrDefault(doc.ChangePolicyId, string.Empty),
                doc.PublicationOption,
                doc.State,
                i.CreatedBy,
                creatorMap.GetValueOrDefault(i.CreatedBy, string.Empty),
                i.CreatedAt,
                versionCounts.GetValueOrDefault(i.Id, 0),
                isAssigned,
                devs,
                myPerms,
                traceId);
        }).ToList();
    }
}
