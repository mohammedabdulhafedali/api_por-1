using System.Security.Claims;
using ApiGovernance.Shared.Contracts;
using ApiGovernance.Shared.Persistence;
using ApiGovernance.Shared.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace ApiGovernance.Shared.Security;

public sealed record DocumentPermissionMetadata(
    string Code,
    string ActionKey,
    string LabelAr,
    string LabelEn);

public sealed record DocumentPermissionResolution(
    bool Allowed,
    string ResolutionSource);

public static class DocumentPermissionCatalog
{
    public const string ResourceName = "document";

    public const string View = "doc.view";
    public const string Edit = "doc.edit";
    public const string Delete = "doc.delete";
    public const string AccessManage = "doc.access.manage";
    public const string Assign = "doc.assign";
    public const string ManageIntegration = "doc.manage_integration";
    public const string VersionManage = "doc.version_manage";
    public const string FilesManage = "doc.files_manage";
    public const string Change = "doc.change";
    public const string Review = "doc.review";
    public const string Publish = "doc.publish";

    public static readonly IReadOnlyList<DocumentPermissionMetadata> All =
    [
        new(View, "view", "عرض الوثيقة والتكامل", "View Document & Integration"),
        new(Edit, "edit", "تعديل إعدادات الوثيقة", "Edit Document Settings"),
        new(Delete, "delete", "حذف الوثيقة غير المرتبطة", "Delete Unlinked Document"),
        new(AccessManage, "access.manage", "إدارة صلاحيات الوثيقة", "Manage Document ACL"),
        new(Assign, "assign", "تعيين المطورين", "Assign Developers"),
        new(ManageIntegration, "manage_integration", "إنشاء وإدارة التكامل", "Manage 1:1 Integration"),
        new(VersionManage, "version_manage", "إدارة إصدارات API", "Manage API Versions"),
        new(FilesManage, "files_manage", "إدارة ملفات المراجعات", "Manage Revision Files"),
        new(Change, "change", "طلبات التغيير", "Change Requests"),
        new(Review, "review", "مراجعة واعتماد", "Review & Decide"),
        new(Publish, "publish", "نشر وإيقاف نشر", "Publish & Withdraw")
    ];

    private static readonly Dictionary<string, DocumentPermissionMetadata> ByCode =
        All.ToDictionary(p => p.Code, StringComparer.OrdinalIgnoreCase);

    public static bool IsDocumentPermissionCode(string? code)
        => !string.IsNullOrWhiteSpace(code) && ByCode.ContainsKey(code.Trim());

    public static DocumentPermissionMetadata GetRequired(string code)
        => ByCode[code.Trim()];
}

public sealed record AuthorizedDocumentAccess(
    UserEntity Actor,
    ApiDocumentEntity Document,
    IReadOnlyDictionary<string, DocumentPermissionResolution> EffectivePermissions);

/// <summary>
/// Central Document-Level ACL Engine (Task I07 & ADR-01 Section B):
/// - Documents are restricted by default (zero implicit access by role name or developer assignment).
/// - Precedence: Explicit User REVOKE > Explicit User GRANT > Role REVOKE (beats Role GRANT conflict) > Role GRANT > Default Deny.
/// - Returns 404 NotFound when document does not exist or actor lacks doc.view to prevent IDOR enumeration.
/// - Returns 403 Forbidden when actor has doc.view but lacks the requested document action.
/// </summary>
public interface IDocumentAuthorizationService
{
    Task<Dictionary<string, PermissionEntity>> EnsureDocumentPermissionDictionaryAsync(
        CancellationToken cancellationToken = default);

    Task GrantCreatorDefaultDocumentAclAsync(
        Guid documentId,
        Guid creatorUserId,
        CancellationToken cancellationToken = default);

    Task<Dictionary<Guid, IReadOnlyDictionary<string, DocumentPermissionResolution>>> EvaluateDocumentPermissionsBatchAsync(
        Guid userId,
        IReadOnlyCollection<Guid> documentIds,
        CancellationToken cancellationToken = default);

    Task<AuthorizedDocumentAccess> RequireDocumentPermissionAsync(
        ClaimsPrincipal actorPrincipal,
        Guid documentId,
        string requiredDocPermissionCode,
        string auditOperationOnDeny,
        CancellationToken cancellationToken = default);

    Task<(AuthorizedDocumentAccess Access, IntegrationEntity Integration)> RequireIntegrationDocumentPermissionAsync(
        ClaimsPrincipal actorPrincipal,
        Guid integrationId,
        string requiredDocPermissionCode,
        string auditOperationOnDeny,
        CancellationToken cancellationToken = default);

    Task<bool> IsUserAssignedDeveloperAsync(
        Guid userId,
        Guid documentId,
        Guid? integrationId = null,
        CancellationToken cancellationToken = default);
}

public sealed class DocumentAuthorizationService(
    ApiGovernanceDbContext dbContext,
    IServerAuthorizationService serverAuthorization) : IDocumentAuthorizationService
{
    public async Task<Dictionary<string, PermissionEntity>> EnsureDocumentPermissionDictionaryAsync(
        CancellationToken cancellationToken = default)
    {
        var codes = DocumentPermissionCatalog.All.Select(p => p.Code).ToList();
        var existingRows = await dbContext.Permissions
            .Where(p => codes.Contains(p.Code))
            .ToListAsync(cancellationToken);

        var map = existingRows.ToDictionary(p => p.Code, StringComparer.OrdinalIgnoreCase);
        var addedAny = false;

        foreach (var meta in DocumentPermissionCatalog.All)
        {
            if (!map.ContainsKey(meta.Code))
            {
                var created = new PermissionEntity
                {
                    Id = Guid.NewGuid(),
                    Code = meta.Code,
                    Resource = DocumentPermissionCatalog.ResourceName,
                    Action = meta.ActionKey
                };
                dbContext.Permissions.Add(created);
                map[meta.Code] = created;
                addedAny = true;
            }
        }

        if (addedAny)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return map;
    }

    public async Task GrantCreatorDefaultDocumentAclAsync(
        Guid documentId,
        Guid creatorUserId,
        CancellationToken cancellationToken = default)
    {
        var dict = await EnsureDocumentPermissionDictionaryAsync(cancellationToken);

        var existingPermIds = await dbContext.DocumentUserPermissions.AsNoTracking()
            .Where(dup => dup.DocumentId == documentId && dup.UserId == creatorUserId)
            .Select(dup => dup.PermissionId)
            .ToHashSetAsync(cancellationToken);

        foreach (var meta in DocumentPermissionCatalog.All)
        {
            var perm = dict[meta.Code];
            if (!existingPermIds.Contains(perm.Id))
            {
                dbContext.DocumentUserPermissions.Add(new DocumentUserPermissionEntity
                {
                    Id = Guid.NewGuid(),
                    DocumentId = documentId,
                    UserId = creatorUserId,
                    PermissionId = perm.Id,
                    Decision = "GRANT"
                });
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<Dictionary<Guid, IReadOnlyDictionary<string, DocumentPermissionResolution>>> EvaluateDocumentPermissionsBatchAsync(
        Guid userId,
        IReadOnlyCollection<Guid> documentIds,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<Guid, IReadOnlyDictionary<string, DocumentPermissionResolution>>();
        if (documentIds.Count == 0)
        {
            return result;
        }

        var dict = await EnsureDocumentPermissionDictionaryAsync(cancellationToken);
        var permIdToCode = dict.Values.ToDictionary(p => p.Id, p => p.Code);
        var docPermIds = permIdToCode.Keys.ToList();

        var activeRoleIds = await (
            from ur in dbContext.UserRoles.AsNoTracking()
            join r in dbContext.Roles.AsNoTracking() on ur.RoleId equals r.Id
            where ur.UserId == userId && r.IsActive
            select r.Id)
            .Distinct()
            .ToListAsync(cancellationToken);

        var userOverrides = await dbContext.DocumentUserPermissions.AsNoTracking()
            .Where(dup => dup.UserId == userId
                          && documentIds.Contains(dup.DocumentId)
                          && docPermIds.Contains(dup.PermissionId))
            .ToListAsync(cancellationToken);

        var roleGrantsAndRevokes = activeRoleIds.Count == 0
            ? []
            : await dbContext.DocumentRolePermissions.AsNoTracking()
                .Where(drp => activeRoleIds.Contains(drp.RoleId)
                              && documentIds.Contains(drp.DocumentId)
                              && docPermIds.Contains(drp.PermissionId))
                .ToListAsync(cancellationToken);

        var userMapByDoc = userOverrides
            .GroupBy(x => x.DocumentId)
            .ToDictionary(
                g => g.Key,
                g => g.ToDictionary(x => permIdToCode[x.PermissionId], x => x.Decision, StringComparer.OrdinalIgnoreCase));

        var roleDecisionsByDocAndCode = roleGrantsAndRevokes
            .GroupBy(x => x.DocumentId)
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(x => permIdToCode[x.PermissionId], StringComparer.OrdinalIgnoreCase)
                      .ToDictionary(
                          pg => pg.Key,
                          pg => pg.Select(x => x.Decision).ToList(),
                          StringComparer.OrdinalIgnoreCase));

        foreach (var docId in documentIds)
        {
            userMapByDoc.TryGetValue(docId, out var userPerms);
            roleDecisionsByDocAndCode.TryGetValue(docId, out var rolePerms);

            var docResolution = new Dictionary<string, DocumentPermissionResolution>(StringComparer.OrdinalIgnoreCase);
            foreach (var meta in DocumentPermissionCatalog.All)
            {
                string? userDecision = null;
                userPerms?.TryGetValue(meta.Code, out userDecision);

                List<string>? activeRoleDecisions = null;
                rolePerms?.TryGetValue(meta.Code, out activeRoleDecisions);

                docResolution[meta.Code] = ResolveSinglePermission(userDecision, activeRoleDecisions);
            }

            result[docId] = docResolution;
        }

        return result;
    }

    public static DocumentPermissionResolution ResolveSinglePermission(
        string? explicitUserDecision,
        IReadOnlyCollection<string>? activeRoleDecisions)
    {
        if (string.Equals(explicitUserDecision, "REVOKE", StringComparison.OrdinalIgnoreCase))
        {
            return new(false, "USER_REVOKE");
        }

        if (string.Equals(explicitUserDecision, "GRANT", StringComparison.OrdinalIgnoreCase))
        {
            return new(true, "USER_GRANT");
        }

        if (activeRoleDecisions is { Count: > 0 })
        {
            var hasRoleRevoke = activeRoleDecisions.Any(d => string.Equals(d, "REVOKE", StringComparison.OrdinalIgnoreCase));
            var hasRoleGrant = activeRoleDecisions.Any(d => string.Equals(d, "GRANT", StringComparison.OrdinalIgnoreCase));

            // ADR-01 Section B.4: Explicit Role REVOKE beats Role GRANT on the same document/action.
            if (hasRoleRevoke)
            {
                return new(false, hasRoleGrant ? "ROLE_REVOKE_CONFLICT" : "ROLE_REVOKE");
            }

            if (hasRoleGrant)
            {
                return new(true, "ROLE_GRANT");
            }
        }

        return new(false, "DENIED_DEFAULT");
    }

    public async Task<AuthorizedDocumentAccess> RequireDocumentPermissionAsync(
        ClaimsPrincipal actorPrincipal,
        Guid documentId,
        string requiredDocPermissionCode,
        string auditOperationOnDeny,
        CancellationToken cancellationToken = default)
    {
        var actor = await serverAuthorization.RequireActiveAuthenticatedUserAsync(actorPrincipal, cancellationToken);

        var doc = await dbContext.ApiDocuments.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == documentId, cancellationToken);

        if (doc is null)
        {
            throw ApiContractException.NotFound(
                messageAr: "وثيقة API المطلوبة غير موجودة أو غير متاحة ضمن صلاحياتك.",
                messageEn: "API document was not found or access is restricted.");
        }

        var batch = await EvaluateDocumentPermissionsBatchAsync(actor.Id, [documentId], cancellationToken);
        var effectiveMap = batch[documentId];

        // Anti-IDOR rule (02_UX_UI_v1.2.md section 5): return identical 404 when caller lacks doc.view
        // so existence or details of restricted documents cannot be enumerated by direct UUID.
        if (!effectiveMap.TryGetValue(DocumentPermissionCatalog.View, out var viewRes) || !viewRes.Allowed)
        {
            await serverAuthorization.WriteAuditEventAsync(
                actor.Id,
                auditOperationOnDeny,
                "API_DOCUMENT",
                documentId,
                "DENIED",
                new { requiredPermission = DocumentPermissionCatalog.View, reason = viewRes?.ResolutionSource ?? "DENIED_DEFAULT" },
                cancellationToken);

            throw ApiContractException.NotFound(
                messageAr: "وثيقة API المطلوبة غير موجودة أو غير متاحة ضمن صلاحياتك.",
                messageEn: "API document was not found or access is restricted.");
        }

        if (!string.Equals(requiredDocPermissionCode, DocumentPermissionCatalog.View, StringComparison.OrdinalIgnoreCase))
        {
            if (!effectiveMap.TryGetValue(requiredDocPermissionCode, out var actionRes) || !actionRes.Allowed)
            {
                await serverAuthorization.WriteAuditEventAsync(
                    actor.Id,
                    auditOperationOnDeny,
                    "API_DOCUMENT",
                    documentId,
                    "DENIED",
                    new { requiredPermission = requiredDocPermissionCode, reason = actionRes?.ResolutionSource ?? "DENIED_DEFAULT" },
                    cancellationToken);

                throw ApiContractException.Forbidden(
                    messageAr: $"ليس لديك صلاحية تنفيذ هذا الإجراء على الوثيقة ({requiredDocPermissionCode}).",
                    messageEn: $"You do not have document-level permission '{requiredDocPermissionCode}' for this API document.");
            }
        }

        return new AuthorizedDocumentAccess(actor, doc, effectiveMap);
    }

    public async Task<(AuthorizedDocumentAccess Access, IntegrationEntity Integration)> RequireIntegrationDocumentPermissionAsync(
        ClaimsPrincipal actorPrincipal,
        Guid integrationId,
        string requiredDocPermissionCode,
        string auditOperationOnDeny,
        CancellationToken cancellationToken = default)
    {
        var actor = await serverAuthorization.RequireActiveAuthenticatedUserAsync(actorPrincipal, cancellationToken);

        var integration = await dbContext.Integrations.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == integrationId, cancellationToken);

        if (integration is null)
        {
            throw ApiContractException.NotFound(
                messageAr: "التكامل المطلوب غير موجود أو غير متاح ضمن صلاحياتك.",
                messageEn: "Integration was not found or access is restricted.");
        }

        var access = await RequireDocumentPermissionAsync(
            actorPrincipal,
            integration.DocumentId,
            requiredDocPermissionCode,
            auditOperationOnDeny,
            cancellationToken);

        return (access, integration);
    }

    public async Task<bool> IsUserAssignedDeveloperAsync(
        Guid userId,
        Guid documentId,
        Guid? integrationId = null,
        CancellationToken cancellationToken = default)
    {
        if (integrationId.HasValue)
        {
            var onIntegration = await dbContext.IntegrationDevelopers.AsNoTracking()
                .AnyAsync(idv => idv.IntegrationId == integrationId.Value && idv.UserId == userId, cancellationToken);
            if (onIntegration)
            {
                return true;
            }
        }

        return await dbContext.DocumentDevelopers.AsNoTracking()
            .AnyAsync(dd => dd.DocumentId == documentId && dd.UserId == userId, cancellationToken);
    }
}

