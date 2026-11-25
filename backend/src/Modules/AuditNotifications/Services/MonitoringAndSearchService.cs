using System.Security.Claims;
using System.Text.RegularExpressions;
using ApiGovernance.Modules.AuditNotifications.Contracts;
using ApiGovernance.Shared.Persistence;
using ApiGovernance.Shared.Persistence.Entities;
using ApiGovernance.Shared.Security;
using Microsoft.EntityFrameworkCore;

namespace ApiGovernance.Modules.AuditNotifications.Services;

public interface IMonitoringAndSearchService
{
    Task<PagedAuditEventsResponse> GetAuditEventsAsync(
        ClaimsPrincipal actorPrincipal,
        int page,
        int pageSize,
        string? search,
        string? operation,
        string? resourceKind,
        string? result,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<GlobalSearchResponse> SearchAuthorizedAsync(
        ClaimsPrincipal actorPrincipal,
        string? query,
        string? type,
        string? state,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<DashboardSummaryResponse> GetDashboardSummaryAsync(
        ClaimsPrincipal actorPrincipal,
        string traceId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Server-side authorized Audit Log (UI-16), Global Search (UI-18), and Permission-Adaptive Dashboard (UI-17)
/// for Sprint S13 Task P05. Enforces both General Permissions (ADR-01) and Document-Level ACL batch evaluation
/// so restricted resources never leak counts or search results (zero-IDOR).
/// </summary>
public sealed partial class MonitoringAndSearchService(
    ApiGovernanceDbContext dbContext,
    IServerAuthorizationService serverAuthorization,
    IDocumentAuthorizationService documentAuthorization) : IMonitoringAndSearchService
{
    private static readonly string[] OpenChangeRequestStatuses =
    [
        "SUBMITTED",
        "IN_REVIEW",
        "RETURNED",
        "APPROVED"
    ];

    public async Task<PagedAuditEventsResponse> GetAuditEventsAsync(
        ClaimsPrincipal actorPrincipal,
        int page,
        int pageSize,
        string? search,
        string? operation,
        string? resourceKind,
        string? result,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        await serverAuthorization.RequireGeneralPermissionAsync(
            actorPrincipal,
            "audit.view",
            "AUDIT_EVENTS_READ_DENIED",
            resourceKind: "AUDIT_EVENT",
            cancellationToken: cancellationToken);

        var normalizedPage = page < 1 ? 1 : page;
        var normalizedSize = Math.Clamp(pageSize <= 0 ? 25 : pageSize, 1, 100);

        var baseQuery =
            from ae in dbContext.AuditEvents.AsNoTracking()
            join u in dbContext.Users.AsNoTracking() on ae.ActorUserId equals u.Id into userJoin
            from actor in userJoin.DefaultIfEmpty()
            select new
            {
                ae.Id,
                ae.ActorUserId,
                ActorName = actor != null ? actor.FullName : null,
                ActorEmail = actor != null ? actor.Email : null,
                ae.Operation,
                ae.ResourceKind,
                ae.ResourceId,
                ae.Result,
                ae.ChangeSummaryJson,
                ae.RecordedAt
            };

        if (!string.IsNullOrWhiteSpace(operation))
        {
            var opTrim = operation.Trim().ToUpperInvariant();
            baseQuery = baseQuery.Where(x => x.Operation.ToUpper() == opTrim);
        }

        if (!string.IsNullOrWhiteSpace(resourceKind))
        {
            var rkTrim = resourceKind.Trim().ToUpperInvariant();
            baseQuery = baseQuery.Where(x => x.ResourceKind.ToUpper() == rkTrim);
        }

        if (!string.IsNullOrWhiteSpace(result))
        {
            var resTrim = result.Trim().ToUpperInvariant();
            baseQuery = baseQuery.Where(x => x.Result.ToUpper() == resTrim);
        }

        if (fromUtc.HasValue)
        {
            baseQuery = baseQuery.Where(x => x.RecordedAt >= fromUtc.Value);
        }

        if (toUtc.HasValue)
        {
            baseQuery = baseQuery.Where(x => x.RecordedAt <= toUtc.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search.Trim()}%";
            baseQuery = baseQuery.Where(x =>
                EF.Functions.ILike(x.Operation, term) ||
                EF.Functions.ILike(x.ResourceKind, term) ||
                EF.Functions.ILike(x.Result, term) ||
                (x.ActorName != null && EF.Functions.ILike(x.ActorName, term)) ||
                (x.ActorEmail != null && EF.Functions.ILike(x.ActorEmail, term)) ||
                (x.ChangeSummaryJson != null && EF.Functions.ILike(x.ChangeSummaryJson, term)));
        }

        var totalCount = await baseQuery.CountAsync(cancellationToken);

        var rows = await baseQuery
            .OrderByDescending(x => x.RecordedAt)
            .ThenByDescending(x => x.Id)
            .Skip((normalizedPage - 1) * normalizedSize)
            .Take(normalizedSize)
            .ToListAsync(cancellationToken);

        var distinctOps = await dbContext.AuditEvents.AsNoTracking()
            .Select(a => a.Operation)
            .Distinct()
            .OrderBy(o => o)
            .Take(100)
            .ToListAsync(cancellationToken);

        var distinctKinds = await dbContext.AuditEvents.AsNoTracking()
            .Select(a => a.ResourceKind)
            .Distinct()
            .OrderBy(k => k)
            .Take(60)
            .ToListAsync(cancellationToken);

        var dtos = rows.Select(r => new AuditEventListItemDto(
            r.Id,
            r.ActorUserId,
            r.ActorName,
            r.ActorEmail,
            r.Operation,
            r.ResourceKind,
            r.ResourceId,
            r.Result,
            SanitizeAuditJson(r.ChangeSummaryJson),
            r.RecordedAt)).ToList();

        return new PagedAuditEventsResponse(
            totalCount,
            normalizedPage,
            normalizedSize,
            dtos,
            distinctOps,
            distinctKinds,
            traceId);
    }

    public async Task<GlobalSearchResponse> SearchAuthorizedAsync(
        ClaimsPrincipal actorPrincipal,
        string? query,
        string? type,
        string? state,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var actor = await serverAuthorization.RequireGeneralPermissionAsync(
            actorPrincipal,
            "search.view",
            "GLOBAL_SEARCH_DENIED",
            resourceKind: "SEARCH",
            cancellationToken: cancellationToken);

        var canViewDocs = actor.EffectivePermissions.Contains("api_documents.view", StringComparer.OrdinalIgnoreCase);
        var canViewIntegrations = actor.EffectivePermissions.Contains("integrations.view", StringComparer.OrdinalIgnoreCase);

        if (!canViewDocs && !canViewIntegrations)
        {
            return new GlobalSearchResponse(query?.Trim() ?? string.Empty, 0, [], traceId);
        }

        var typeFilter = string.IsNullOrWhiteSpace(type) ? "ALL" : type.Trim().ToUpperInvariant();
        var stateFilter = string.IsNullOrWhiteSpace(state) ? null : state.Trim().ToUpperInvariant();
        var term = string.IsNullOrWhiteSpace(query) ? string.Empty : query.Trim().ToLowerInvariant();

        var docRows = await dbContext.ApiDocuments.AsNoTracking()
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync(cancellationToken);

        if (docRows.Count == 0)
        {
            return new GlobalSearchResponse(query?.Trim() ?? string.Empty, 0, [], traceId);
        }

        var accessMap = await documentAuthorization.EvaluateDocumentPermissionsBatchAsync(
            actor.User.Id,
            docRows.Select(d => d.Id).ToList(),
            cancellationToken);

        var visibleDocs = docRows
            .Where(d => accessMap.TryGetValue(d.Id, out var perms)
                && perms.TryGetValue(DocumentPermissionCatalog.View, out var viewPerm)
                && viewPerm.Allowed)
            .ToList();

        if (visibleDocs.Count == 0)
        {
            return new GlobalSearchResponse(query?.Trim() ?? string.Empty, 0, [], traceId);
        }

        var meta = await LoadDocumentDisplayMetadataAsync(visibleDocs, cancellationToken);
        var visibleDocIds = visibleDocs.Select(d => d.Id).ToList();

        var integrations = await dbContext.Integrations.AsNoTracking()
            .Where(i => visibleDocIds.Contains(i.DocumentId))
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(cancellationToken);

        var integrationIds = integrations.Select(i => i.Id).ToList();
        var latestVersions = integrationIds.Count == 0
            ? new Dictionary<Guid, string>()
            : (await dbContext.ApiVersions.AsNoTracking()
                .Where(v => integrationIds.Contains(v.IntegrationId))
                .OrderByDescending(v => v.CreatedAt)
                .ToListAsync(cancellationToken))
                .GroupBy(v => v.IntegrationId)
                .ToDictionary(g => g.Key, g => g.First().VersionLabel);

        var publishedIntegrationIds = integrationIds.Count == 0
            ? []
            : (await (
                from p in dbContext.Publications.AsNoTracking()
                join r in dbContext.DocumentRevisions.AsNoTracking() on p.RevisionId equals r.Id
                join v in dbContext.ApiVersions.AsNoTracking() on r.ApiVersionId equals v.Id
                where integrationIds.Contains(v.IntegrationId) && p.State == "PUBLISHED"
                select v.IntegrationId)
                .Distinct()
                .ToListAsync(cancellationToken))
                .ToHashSet();

        var integByDoc = integrations.ToDictionary(i => i.DocumentId);
        var results = new List<GlobalSearchResultItemDto>();

        if (canViewDocs && (typeFilter == "ALL" || typeFilter == "DOCUMENT"))
        {
            foreach (var d in visibleDocs)
            {
                if (stateFilter is not null && !string.Equals(d.State, stateFilter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var info = meta[d.Id];
                integByDoc.TryGetValue(d.Id, out var linkedInteg);
                var latestVer = linkedInteg is not null ? latestVersions.GetValueOrDefault(linkedInteg.Id) : null;
                var hasPub = linkedInteg is not null && publishedIntegrationIds.Contains(linkedInteg.Id);

                if (MatchesSearchTerm(term, info.Title, info.ProviderName, info.ConsumerName, info.ApiGroupName, d.State, linkedInteg?.ReferenceCode, latestVer))
                {
                    results.Add(new GlobalSearchResultItemDto(
                        "DOCUMENT",
                        d.Id,
                        d.Id,
                        linkedInteg?.Id,
                        linkedInteg?.ReferenceCode,
                        info.Title,
                        info.ProviderName,
                        info.ConsumerName,
                        info.ApiGroupName,
                        d.State,
                        latestVer,
                        hasPub,
                        d.CreatedAt));
                }
            }
        }

        if (canViewIntegrations && (typeFilter == "ALL" || typeFilter == "INTEGRATION"))
        {
            var docById = visibleDocs.ToDictionary(d => d.Id);
            foreach (var integ in integrations)
            {
                if (!docById.TryGetValue(integ.DocumentId, out var doc))
                {
                    continue;
                }

                var pubState = publishedIntegrationIds.Contains(integ.Id) ? "PUBLISHED" : doc.State;
                if (stateFilter is not null
                    && !string.Equals(doc.State, stateFilter, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(pubState, stateFilter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var info = meta[doc.Id];
                var latestVer = latestVersions.GetValueOrDefault(integ.Id);
                var hasPub = publishedIntegrationIds.Contains(integ.Id);

                if (MatchesSearchTerm(term, info.Title, info.ProviderName, info.ConsumerName, info.ApiGroupName, integ.ReferenceCode, doc.State, latestVer))
                {
                    results.Add(new GlobalSearchResultItemDto(
                        "INTEGRATION",
                        integ.Id,
                        doc.Id,
                        integ.Id,
                        integ.ReferenceCode,
                        info.Title,
                        info.ProviderName,
                        info.ConsumerName,
                        info.ApiGroupName,
                        doc.State,
                        latestVer,
                        hasPub,
                        integ.CreatedAt));
                }
            }
        }

        var ordered = results
            .OrderByDescending(r => r.CreatedAt)
            .Take(60)
            .ToList();

        return new GlobalSearchResponse(
            query?.Trim() ?? string.Empty,
            ordered.Count,
            ordered,
            traceId);
    }

    public async Task<DashboardSummaryResponse> GetDashboardSummaryAsync(
        ClaimsPrincipal actorPrincipal,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var actor = await serverAuthorization.RequireGeneralPermissionAsync(
            actorPrincipal,
            "dashboard.view",
            "DASHBOARD_READ_DENIED",
            resourceKind: "DASHBOARD",
            cancellationToken: cancellationToken);

        var permsSet = actor.EffectivePermissions.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var canViewDocs = permsSet.Contains("api_documents.view");
        var canViewIntegrations = permsSet.Contains("integrations.view");
        var canReviewWorkflows = permsSet.Contains("workflows.view");
        var canViewPublications = permsSet.Contains("publications.view");
        var canViewAudit = permsSet.Contains("audit.view");
        var canViewPortal = permsSet.Contains("portal.view");

        var unreadNotificationsCount = await dbContext.Notifications.AsNoTracking()
            .CountAsync(n => n.UserId == actor.User.Id && n.ReadAt == null, cancellationToken);

        var allDocs = (canViewDocs || canViewIntegrations || canReviewWorkflows || canViewPublications)
            ? await dbContext.ApiDocuments.AsNoTracking().ToListAsync(cancellationToken)
            : [];

        var accessMap = allDocs.Count == 0
            ? new Dictionary<Guid, IReadOnlyDictionary<string, DocumentPermissionResolution>>()
            : await documentAuthorization.EvaluateDocumentPermissionsBatchAsync(
                actor.User.Id,
                allDocs.Select(d => d.Id).ToList(),
                cancellationToken);

        var viewAllowedDocIds = allDocs
            .Where(d => accessMap.TryGetValue(d.Id, out var perms)
                && perms.TryGetValue(DocumentPermissionCatalog.View, out var vp)
                && vp.Allowed)
            .Select(d => d.Id)
            .ToHashSet();

        var reviewAllowedDocIds = allDocs
            .Where(d => viewAllowedDocIds.Contains(d.Id)
                && accessMap.TryGetValue(d.Id, out var perms)
                && perms.TryGetValue(DocumentPermissionCatalog.Review, out var rp)
                && rp.Allowed)
            .Select(d => d.Id)
            .ToHashSet();

        var visibleDocumentsCount = canViewDocs ? viewAllowedDocIds.Count : 0;

        var visibleIntegrations = viewAllowedDocIds.Count == 0
            ? []
            : await dbContext.Integrations.AsNoTracking()
                .Where(i => viewAllowedDocIds.Contains(i.DocumentId))
                .ToListAsync(cancellationToken);

        var visibleIntegrationIds = visibleIntegrations.Select(i => i.Id).ToList();
        var visibleIntegrationsCount = canViewIntegrations ? visibleIntegrations.Count : 0;

        var openChangeRequestsCount = 0;
        if (visibleIntegrationIds.Count > 0 && (canViewIntegrations || canReviewWorkflows))
        {
            openChangeRequestsCount = await dbContext.ChangeRequests.AsNoTracking()
                .CountAsync(cr => visibleIntegrationIds.Contains(cr.IntegrationId)
                    && OpenChangeRequestStatuses.Contains(cr.Status), cancellationToken);
        }

        var publishedRevisionsCount = 0;
        if (visibleIntegrationIds.Count > 0 && (canViewPublications || canViewIntegrations || canViewPortal))
        {
            publishedRevisionsCount = await (
                from p in dbContext.Publications.AsNoTracking()
                join rev in dbContext.DocumentRevisions.AsNoTracking() on p.RevisionId equals rev.Id
                join ver in dbContext.ApiVersions.AsNoTracking() on rev.ApiVersionId equals ver.Id
                where visibleIntegrationIds.Contains(ver.IntegrationId) && p.State == "PUBLISHED"
                select p.Id)
                .CountAsync(cancellationToken);
        }

        var pendingStages = new List<DashboardPendingStageDto>();
        if (canReviewWorkflows && reviewAllowedDocIds.Count > 0 && actor.ActiveRoles.Count > 0)
        {
            var actorRoleIds = actor.ActiveRoles.Select(r => r.Id).ToList();
            var roleNameMap = actor.ActiveRoles.ToDictionary(r => r.Id, r => r.Name);

            var activeStageRows = await (
                from st in dbContext.WorkflowRunStages.AsNoTracking()
                join run in dbContext.WorkflowRuns.AsNoTracking() on st.RunId equals run.Id
                where st.Status == "ACTIVE"
                    && run.Status == "IN_PROGRESS"
                    && actorRoleIds.Contains(st.ResponsibleRoleIdSnapshot)
                orderby run.StartedAt descending
                select new { st, run })
                .Take(30)
                .ToListAsync(cancellationToken);

            if (activeStageRows.Count > 0)
            {
                var docMap = await LoadDocumentDisplayMetadataAsync(
                    allDocs.Where(d => reviewAllowedDocIds.Contains(d.Id)).ToList(),
                    cancellationToken);

                var revIds = activeStageRows.Where(x => x.run.RevisionId.HasValue).Select(x => x.run.RevisionId!.Value).Distinct().ToList();
                var crIds = activeStageRows.Where(x => x.run.ChangeRequestId.HasValue).Select(x => x.run.ChangeRequestId!.Value).Distinct().ToList();

                var revIntegMap = revIds.Count == 0
                    ? new Dictionary<Guid, Guid>()
                    : await (
                        from rev in dbContext.DocumentRevisions.AsNoTracking()
                        join ver in dbContext.ApiVersions.AsNoTracking() on rev.ApiVersionId equals ver.Id
                        where revIds.Contains(rev.Id)
                        select new { rev.Id, ver.IntegrationId })
                        .ToDictionaryAsync(x => x.Id, x => x.IntegrationId, cancellationToken);

                var crIntegMap = crIds.Count == 0
                    ? new Dictionary<Guid, Guid>()
                    : await dbContext.ChangeRequests.AsNoTracking()
                        .Where(c => crIds.Contains(c.Id))
                        .ToDictionaryAsync(c => c.Id, c => c.IntegrationId, cancellationToken);

                var allTargetIntegs = visibleIntegrations.ToDictionary(i => i.Id);

                foreach (var row in activeStageRows)
                {
                    Guid? integId = row.run.RevisionId.HasValue
                        ? (revIntegMap.TryGetValue(row.run.RevisionId.Value, out var ri) ? ri : null)
                        : (row.run.ChangeRequestId.HasValue && crIntegMap.TryGetValue(row.run.ChangeRequestId.Value, out var ci) ? ci : null);

                    if (!integId.HasValue || !allTargetIntegs.TryGetValue(integId.Value, out var integ))
                    {
                        continue;
                    }

                    if (!reviewAllowedDocIds.Contains(integ.DocumentId) || !docMap.TryGetValue(integ.DocumentId, out var info))
                    {
                        continue;
                    }

                    pendingStages.Add(new DashboardPendingStageDto(
                        row.run.Id,
                        row.st.Id,
                        row.st.StageNameSnapshot,
                        row.run.ChangeRequestId.HasValue ? "CHANGE_REQUEST" : "REVISION",
                        row.run.ChangeRequestId ?? row.run.RevisionId!.Value,
                        integ.DocumentId,
                        integ.Id,
                        info.Title,
                        integ.ReferenceCode,
                        roleNameMap.GetValueOrDefault(row.st.ResponsibleRoleIdSnapshot, string.Empty),
                        row.run.StartedAt));
                }
            }
        }

        var recentAuditEvents = new List<DashboardRecentAuditDto>();
        if (canViewAudit)
        {
            recentAuditEvents = await (
                from ae in dbContext.AuditEvents.AsNoTracking()
                join u in dbContext.Users.AsNoTracking() on ae.ActorUserId equals u.Id into userJoin
                from u in userJoin.DefaultIfEmpty()
                orderby ae.RecordedAt descending
                select new DashboardRecentAuditDto(
                    ae.Id,
                    u != null ? u.FullName : null,
                    ae.Operation,
                    ae.ResourceKind,
                    ae.ResourceId,
                    ae.Result,
                    ae.RecordedAt))
                .Take(8)
                .ToListAsync(cancellationToken);
        }

        return new DashboardSummaryResponse(
            visibleDocumentsCount,
            visibleIntegrationsCount,
            pendingStages.Count,
            openChangeRequestsCount,
            publishedRevisionsCount,
            unreadNotificationsCount,
            canViewDocs,
            canViewIntegrations,
            canReviewWorkflows,
            canViewPublications,
            canViewAudit,
            canViewPortal,
            pendingStages,
            recentAuditEvents,
            traceId);
    }

    private sealed record DocumentDisplayMeta(string Title, string ProviderName, string ConsumerName, string ApiGroupName);

    private async Task<Dictionary<Guid, DocumentDisplayMeta>> LoadDocumentDisplayMetadataAsync(
        IReadOnlyList<ApiDocumentEntity> documents,
        CancellationToken cancellationToken)
    {
        if (documents.Count == 0)
        {
            return [];
        }

        var compIds = documents.SelectMany(d => new[] { d.ProviderInternalSystemId, d.ConsumerCompanySystemId })
            .Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();
        var extIds = documents.Where(d => d.ProviderExternalSystemId.HasValue).Select(d => d.ProviderExternalSystemId!.Value).Distinct().ToList();
        var consIds = documents.Where(d => d.ConsumerEntityId.HasValue).Select(d => d.ConsumerEntityId!.Value).Distinct().ToList();
        var grpIds = documents.Select(d => d.ApiGroupId).Distinct().ToList();

        var compMap = compIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await dbContext.CompanySystems.AsNoTracking().Where(c => compIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);
        var extMap = extIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await dbContext.ExternalProviderSystems.AsNoTracking().Where(e => extIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.Name, cancellationToken);
        var consMap = consIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await dbContext.ConsumerEntities.AsNoTracking().Where(c => consIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);
        var grpMap = grpIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await dbContext.ApiGroups.AsNoTracking().Where(g => grpIds.Contains(g.Id)).ToDictionaryAsync(g => g.Id, g => g.Name, cancellationToken);

        return documents.ToDictionary(
            d => d.Id,
            d =>
            {
                var prov = d.ProviderInternalSystemId.HasValue
                    ? compMap.GetValueOrDefault(d.ProviderInternalSystemId.Value, string.Empty)
                    : extMap.GetValueOrDefault(d.ProviderExternalSystemId!.Value, string.Empty);
                var cons = d.ConsumerCompanySystemId.HasValue
                    ? compMap.GetValueOrDefault(d.ConsumerCompanySystemId.Value, string.Empty)
                    : consMap.GetValueOrDefault(d.ConsumerEntityId!.Value, string.Empty);
                var grp = grpMap.GetValueOrDefault(d.ApiGroupId, string.Empty);
                return new DocumentDisplayMeta($"{prov} ← {cons}", prov, cons, grp);
            });
    }

    private static bool MatchesSearchTerm(string term, params string?[] fields)
    {
        if (string.IsNullOrEmpty(term))
        {
            return true;
        }

        foreach (var field in fields)
        {
            if (!string.IsNullOrWhiteSpace(field) && field.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string? SanitizeAuditJson(string? rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return rawJson;
        }

        return SensitiveJsonPairRegex().Replace(rawJson, "$1\"[REDACTED]\"");
    }

    [GeneratedRegex("(\"(?:password|passwordHash|token|secret|apiKey|connectionString)\"\\s*:\\s*)\"[^\"]*\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SensitiveJsonPairRegex();
}
