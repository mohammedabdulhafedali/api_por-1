using System.Security.Claims;
using ApiGovernance.Shared.Contracts;
using ApiGovernance.Shared.Persistence;
using ApiGovernance.Shared.Persistence.Entities;
using ApiGovernance.Shared.Security;
using Microsoft.EntityFrameworkCore;

namespace ApiGovernance.Modules.Documents.Services;

public sealed record SubjectDocumentCell(string? Decision, bool DefaultAllowed, bool Allowed, string Source);
public sealed record SubjectDocumentRow(Guid DocumentId, string Title, string GroupName, bool CanManage,
    IReadOnlyDictionary<string, SubjectDocumentCell> Cells);
public sealed record SubjectDocumentPage(IReadOnlyList<DocumentPermissionMetadata> Permissions,
    IReadOnlyList<SubjectDocumentRow> Items, int Page, int TotalPages, int TotalCount);
public sealed record SubjectDocumentChange(Guid DocumentId, string PermissionCode, string? ExpectedDecision, string? Decision);
public sealed record SubjectDocumentPatch(IReadOnlyList<SubjectDocumentChange> Changes);

/// <summary>
/// A subject-oriented projection of existing document ACLs. Writes patch only changed
/// cells, lock document rows in a stable order, and compare original decisions.
/// No permission to administer a role/user implies access to any document.
/// </summary>
public sealed class SubjectDocumentPermissionsService(ApiGovernanceDbContext db,
    IServerAuthorizationService authorization, IDocumentAuthorizationService documentAuthorization)
{
    private async Task ValidateSubjectAsync(string kind, Guid id, CancellationToken ct)
    {
        var exists = kind switch
        {
            "users" => await db.Users.AnyAsync(x => x.Id == id, ct),
            "roles" => await db.Roles.AnyAsync(x => x.Id == id, ct),
            _ => false
        };
        if (!exists) throw ApiContractException.NotFound();
    }

    public async Task<SubjectDocumentPage> ReadAsync(ClaimsPrincipal principal, string kind, Guid id,
        int? page, string? search, CancellationToken ct)
    {
        await ValidateSubjectAsync(kind, id, ct);
        var actor = await authorization.RequireActiveAuthenticatedUserAsync(principal, ct);
        var candidates = await db.ApiDocuments.AsNoTracking().Select(x => x.Id).ToListAsync(ct);
        var actorAcl = await documentAuthorization.EvaluateDocumentPermissionsBatchAsync(actor.Id, candidates, ct);
        // Same requirement as the existing /api-documents/{id}/access endpoint.
        var visible = candidates.Where(docId => actorAcl[docId][DocumentPermissionCatalog.View].Allowed
            && actorAcl[docId][DocumentPermissionCatalog.AccessManage].Allowed).ToList();
        var projection = db.ApiDocuments.AsNoTracking().Where(x => visible.Contains(x.Id)).Select(x => new
        {
            x.Id,
            DerivedTitle = (x.ProviderInternalSystemId.HasValue
                ? db.CompanySystems.Where(s => s.Id == x.ProviderInternalSystemId).Select(s => s.Name).FirstOrDefault()
                : db.ExternalProviderSystems.Where(s => s.Id == x.ProviderExternalSystemId).Select(s => s.Name).FirstOrDefault())
                + " ← " + (x.ConsumerCompanySystemId.HasValue
                    ? db.CompanySystems.Where(s => s.Id == x.ConsumerCompanySystemId).Select(s => s.Name).FirstOrDefault()
                    : db.ConsumerEntities.Where(s => s.Id == x.ConsumerEntityId).Select(s => s.Name).FirstOrDefault()),
            GroupName = db.ApiGroups.Where(g => g.Id == x.ApiGroupId).Select(g => g.Name).FirstOrDefault() ?? ""
        });
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            projection = projection.Where(x => x.DerivedTitle.Contains(term) || x.GroupName.Contains(term));
        }
        var count = await projection.CountAsync(ct);
        var (_, size, totalPages, currentPage) = PaginationHelper.Compute(page, 15, count);
        var documents = await projection.OrderBy(x => x.DerivedTitle).ThenBy(x => x.Id)
            .Skip((currentPage - 1) * size).Take(size).ToListAsync(ct);
        var pageIds = documents.Select(x => x.Id).ToList();
        var dict = await documentAuthorization.EnsureDocumentPermissionDictionaryAsync(ct);
        var codes = dict.Values.ToDictionary(x => x.Id, x => x.Code);
        var roleIds = kind == "roles" ? new List<Guid> { id } : await (
            from ur in db.UserRoles.AsNoTracking() join r in db.Roles.AsNoTracking() on ur.RoleId equals r.Id
            where ur.UserId == id && r.IsActive select r.Id).Distinct().ToListAsync(ct);
        var roleRows = await db.DocumentRolePermissions.AsNoTracking()
            .Where(x => pageIds.Contains(x.DocumentId) && roleIds.Contains(x.RoleId)).ToListAsync(ct);
        var userRows = kind == "users" ? await db.DocumentUserPermissions.AsNoTracking()
            .Where(x => pageIds.Contains(x.DocumentId) && x.UserId == id).ToListAsync(ct) : [];
        var roles = roleRows.Where(x => codes.ContainsKey(x.PermissionId))
            .ToDictionary(x => (x.DocumentId, x.RoleId, codes[x.PermissionId]), x => x.Decision);
        var users = userRows.Where(x => codes.ContainsKey(x.PermissionId))
            .ToDictionary(x => (x.DocumentId, codes[x.PermissionId]), x => x.Decision);
        var permissions = DocumentPermissionCatalog.All.Where(x => !LegacyCompoundCodes.Contains(x.Code)).ToList();
        var canManage = await authorization.HasGeneralPermissionAsync(principal, "documents.permissions.manage", ct);
        var items = new List<SubjectDocumentRow>();
        foreach (var docId in pageIds)
        {
            var doc = documents.Single(x => x.Id == docId);
            var cells = new Dictionary<string, SubjectDocumentCell>();
            foreach (var permission in permissions)
            {
                var code = permission.Code;
                if (kind == "roles")
                {
                    var decision = roles.GetValueOrDefault((docId, id, code));
                    cells[code] = new(decision, false, decision == "GRANT",
                        decision is not null ? "ROLE_" + decision : "DENIED_DEFAULT");
                }
                else
                {
                    var decision = users.GetValueOrDefault((docId, code));
                    var roleDecisions = roleIds.Select(rid => roles.GetValueOrDefault((docId, rid, code))).OfType<string>().ToList();
                    var baseline = DocumentAuthorizationService.ResolveSinglePermission(null, roleDecisions);
                    var effective = DocumentAuthorizationService.ResolveSinglePermission(decision, roleDecisions);
                    cells[code] = new(decision, baseline.Allowed, effective.Allowed,
                        effective.ResolutionSource);
                }
            }
            items.Add(new(docId, doc.DerivedTitle, doc.GroupName, canManage, cells));
        }
        return new(permissions, items, currentPage, totalPages, count);
    }

    private static readonly HashSet<string> LegacyCompoundCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        DocumentPermissionCatalog.FilesManage, DocumentPermissionCatalog.VersionManage,
        DocumentPermissionCatalog.Review, DocumentPermissionCatalog.Publish, DocumentPermissionCatalog.Change
    };

    public async Task PatchAsync(ClaimsPrincipal principal, string kind, Guid id, SubjectDocumentPatch request, CancellationToken ct)
    {
        await ValidateSubjectAsync(kind, id, ct);
        if (request.Changes is null || request.Changes.Count > 1000
            || request.Changes.Any(x => x is null)
            || request.Changes.Select(x => (x.DocumentId, x.PermissionCode?.ToLowerInvariant())).Distinct().Count() != request.Changes.Count
            || request.Changes.Any(x => x is null || !DocumentPermissionCatalog.IsDocumentPermissionCode(x.PermissionCode)
                || x.PermissionCode != x.PermissionCode.Trim()
                || LegacyCompoundCodes.Contains(x.PermissionCode)
                || x.Decision is not (null or "GRANT" or "REVOKE")
                || x.ExpectedDecision is not (null or "GRANT" or "REVOKE")))
            throw ApiContractException.BadRequest("تغييرات الصلاحيات غير صالحة.", "Invalid permission changes.");
        var dictionary = await documentAuthorization.EnsureDocumentPermissionDictionaryAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var management = new DocumentManagementService(db, authorization, documentAuthorization);
        foreach (var group in request.Changes.GroupBy(x => x.DocumentId).OrderBy(x => x.Key))
        {
            // Serialize against other subject edits, including the original full ACL editor.
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM api_documents WHERE id = {group.Key} FOR UPDATE", ct);
            var access = await documentAuthorization.RequireDocumentPermissionAsync(principal, group.Key,
                DocumentPermissionCatalog.AccessManage, "SUBJECT_DOCUMENT_ACL_DENIED", ct);
            var roleRows = await db.DocumentRolePermissions.Where(x => x.DocumentId == group.Key).ToListAsync(ct);
            var userRows = await db.DocumentUserPermissions.Where(x => x.DocumentId == group.Key).ToListAsync(ct);
            foreach (var change in group)
            {
                var permissionId = dictionary[change.PermissionCode].Id;
                var roleRow = kind == "roles" ? roleRows.FirstOrDefault(x => x.RoleId == id && x.PermissionId == permissionId) : null;
                var userRow = kind == "users" ? userRows.FirstOrDefault(x => x.UserId == id && x.PermissionId == permissionId) : null;
                var current = kind == "roles" ? roleRow?.Decision : userRow?.Decision;
                if (current != change.ExpectedDecision)
                    throw ApiContractException.Conflict("تغيرت الصلاحيات بواسطة مستخدم آخر. أعد تحميلها قبل الحفظ.",
                        "Permissions changed since loading. Reload before saving.");
                if (kind == "roles")
                {
                    if (change.Decision is null) { if (roleRow is not null) { db.Remove(roleRow); roleRows.Remove(roleRow); } }
                    else if (roleRow is not null) roleRow.Decision = change.Decision;
                    else { var row = new DocumentRolePermissionEntity { Id = Guid.NewGuid(), DocumentId = group.Key, RoleId = id, PermissionId = permissionId, Decision = change.Decision }; db.Add(row); roleRows.Add(row); }
                }
                else
                {
                    if (change.Decision is null) { if (userRow is not null) { db.Remove(userRow); userRows.Remove(userRow); } }
                    else if (userRow is not null) userRow.Decision = change.Decision;
                    else { var row = new DocumentUserPermissionEntity { Id = Guid.NewGuid(), DocumentId = group.Key, UserId = id, PermissionId = permissionId, Decision = change.Decision }; db.Add(row); userRows.Add(row); }
                }
            }
            await management.EnsureAtLeastOneDocumentAccessManagerRemainsAsync(group.Key,
                dictionary[DocumentPermissionCatalog.AccessManage].Id, roleRows, userRows, ct);
            await db.SaveChangesAsync(ct);
            await authorization.WriteAuditEventAsync(access.Actor.Id, "SUBJECT_DOCUMENT_ACL_UPDATED", "API_DOCUMENT", group.Key,
                "SUCCESS", new { subjectKind = kind, subjectId = id, changes = group.ToArray() }, ct);
        }
        await tx.CommitAsync(ct);
    }
}
