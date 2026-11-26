using System.Security.Claims;
using ApiGovernance.Modules.IdentityAccess.Contracts;
using ApiGovernance.Shared.Contracts;
using ApiGovernance.Shared.Persistence;
using ApiGovernance.Shared.Persistence.Entities;
using ApiGovernance.Shared.Security;
using Microsoft.EntityFrameworkCore;

namespace ApiGovernance.Modules.IdentityAccess.Services;

public interface IPermissionManagementService
{
    Task<PermissionsCatalogResponse> GetPermissionsCatalogAsync(
        ClaimsPrincipal actorPrincipal,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<RolePermissionsResponse> GetRolePermissionsAsync(
        ClaimsPrincipal actorPrincipal,
        Guid roleId,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<RolePermissionsResponse> ReplaceRolePermissionsAsync(
        ClaimsPrincipal actorPrincipal,
        Guid roleId,
        ReplaceRolePermissionsRequest request,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<UserPermissionsMatrixResponse> GetUserPermissionsMatrixAsync(
        ClaimsPrincipal actorPrincipal,
        Guid targetUserId,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<UserPermissionsMatrixResponse> ReplaceUserPermissionOverridesAsync(
        ClaimsPrincipal actorPrincipal,
        Guid targetUserId,
        ReplaceUserPermissionOverridesRequest request,
        string traceId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Implements Task I04 Role Permissions Matrix and User Individual Overrides (GRANT / REVOKE / Inherit).
/// Enforces ADR-01 precedence (User REVOKE > User GRANT > Active Role Union), preserves the permanent
/// system permission dictionary (never deleting rows), prevents privilege escalation, and audits all changes (OP-01).
/// </summary>
public sealed class PermissionManagementService(
    ApiGovernanceDbContext dbContext,
    IdentityAccessAuthorizationHelper authHelper) : IPermissionManagementService
{
    private static readonly string[] CriticalAdminPermissionCodes =
    [
        UserPermissionCodes.PermissionsManage,
        UserPermissionCodes.PermissionsView,
        UserPermissionCodes.RolesEdit,
        UserPermissionCodes.UsersAssignRoles,
        UserPermissionCodes.UsersEdit
    ];

    public async Task<PermissionsCatalogResponse> GetPermissionsCatalogAsync(
        ClaimsPrincipal actorPrincipal,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        await authHelper.RequireGeneralPermissionAsync(
            actorPrincipal,
            UserPermissionCodes.PermissionsView,
            "PERMISSIONS_CATALOG_DENIED",
            resourceKind: "PERMISSION",
            cancellationToken: cancellationToken);

        await EnsureCanonicalDictionarySeededAsync(cancellationToken);

        var items = await LoadOrderedCatalogItemsAsync(cancellationToken);

        return new PermissionsCatalogResponse(
            Permissions: items,
            Columns: PlatformPermissionCatalog.ActionColumns,
            Screens: PlatformPermissionCatalog.ScreenRows,
            TraceId: traceId);
    }

    public async Task<RolePermissionsResponse> GetRolePermissionsAsync(
        ClaimsPrincipal actorPrincipal,
        Guid roleId,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        await authHelper.RequireGeneralPermissionAsync(
            actorPrincipal,
            UserPermissionCodes.PermissionsView,
            "ROLE_PERMISSIONS_VIEW_DENIED",
            targetResourceId: roleId,
            resourceKind: "ROLE",
            cancellationToken: cancellationToken);

        await EnsureCanonicalDictionarySeededAsync(cancellationToken);

        var role = await dbContext.Roles.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == roleId, cancellationToken);
        if (role is null)
        {
            throw ApiContractException.NotFound(
                messageAr: "الدور المطلوب غير موجود.",
                messageEn: "The requested role was not found.");
        }

        var grantedRows = await (
            from rp in dbContext.RolePermissions.AsNoTracking()
            join p in dbContext.Permissions.AsNoTracking() on rp.PermissionId equals p.Id
            where rp.RoleId == roleId
            orderby p.Code
            select new { p.Id, p.Code })
            .ToListAsync(cancellationToken);

        return new RolePermissionsResponse(
            RoleId: role.Id,
            RoleName: role.Name,
            IsActive: role.IsActive,
            IsPinned: role.IsPinned,
            PermissionIds: grantedRows.Select(x => x.Id).ToList(),
            PermissionCodes: grantedRows.Select(x => x.Code).ToList(),
            TraceId: traceId);
    }

    public async Task<RolePermissionsResponse> ReplaceRolePermissionsAsync(
        ClaimsPrincipal actorPrincipal,
        Guid roleId,
        ReplaceRolePermissionsRequest request,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var actor = await authHelper.RequireGeneralPermissionAsync(
            actorPrincipal,
            UserPermissionCodes.PermissionsManage,
            "ROLE_PERMISSIONS_REPLACE_DENIED",
            targetResourceId: roleId,
            resourceKind: "ROLE",
            cancellationToken: cancellationToken);

        await EnsureCanonicalDictionarySeededAsync(cancellationToken);

        var role = await dbContext.Roles.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == roleId, cancellationToken);
        if (role is null)
        {
            throw ApiContractException.NotFound(
                messageAr: "الدور المطلوب غير موجود.",
                messageEn: "The requested role was not found.");
        }

        var requestedIds = (request.PermissionIds ?? []).Distinct().ToList();
        var allPermissions = await dbContext.Permissions.AsNoTracking()
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        var unknownIds = requestedIds.Where(id => !allPermissions.ContainsKey(id)).ToList();
        if (unknownIds.Count > 0)
        {
            throw ApiContractException.UnprocessableEntity(
                messageAr: "توجد صلاحية واحدة أو أكثر غير معرفة في قاموس النظام.",
                messageEn: "One or more permission IDs do not exist in the system dictionary.");
        }

        var existingAssignments = await dbContext.RolePermissions
            .Where(rp => rp.RoleId == roleId)
            .ToListAsync(cancellationToken);
        var existingSet = existingAssignments.Select(rp => rp.PermissionId).ToHashSet();
        var requestedSet = requestedIds.ToHashSet();

        // Anti-privilege-escalation guard: actor cannot grant any permission they do not hold effectively
        var (_, actorEffectiveCodes) = await authHelper.ResolveRolesAndEffectivePermissionsAsync(
            actor.Id,
            cancellationToken);
        var actorPermSet = new HashSet<string>(actorEffectiveCodes, StringComparer.OrdinalIgnoreCase);

        var newlyAddedIds = requestedIds.Where(id => !existingSet.Contains(id)).ToList();
        foreach (var addedId in newlyAddedIds)
        {
            var code = allPermissions[addedId].Code;
            if (!actorPermSet.Contains(code))
            {
                await authHelper.WriteAuditEventAsync(
                    actorUserId: actor.Id,
                    operation: "ROLE_PERMISSIONS_ESCALATION_BLOCKED",
                    resourceKind: "ROLE",
                    resourceId: roleId,
                    result: "FORBIDDEN",
                    summary: new { roleId, blockedPermissionCode = code },
                    cancellationToken);

                throw ApiContractException.Forbidden(
                    messageAr: $"لا يمكنك منح صلاحية ({code}) لأن حسابك نفسه لا يملكها فعليًا (منع تصعيد الصلاحيات).",
                    messageEn: $"Privilege escalation blocked: you cannot grant '{code}' because your account does not hold it.");
            }
        }

        // Critical capability retention guard (cannot strip the last active administrator's management capabilities)
        await EnsureCriticalCapabilitiesRetainedAsync(
            simulatedRoleId: roleId,
            simulatedRolePermissionIds: requestedSet,
            simulatedUserId: null,
            simulatedUserOverrides: null,
            cancellationToken: cancellationToken);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var toRemove = existingAssignments.Where(rp => !requestedSet.Contains(rp.PermissionId)).ToList();
        if (toRemove.Count > 0)
        {
            dbContext.RolePermissions.RemoveRange(toRemove);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        foreach (var addedId in newlyAddedIds)
        {
            dbContext.RolePermissions.Add(new RolePermissionEntity
            {
                Id = Guid.NewGuid(),
                RoleId = roleId,
                PermissionId = addedId
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var addedCodes = newlyAddedIds.Select(id => allPermissions[id].Code).OrderBy(c => c, StringComparer.Ordinal).ToList();
        var removedCodes = toRemove.Select(rp => allPermissions[rp.PermissionId].Code).OrderBy(c => c, StringComparer.Ordinal).ToList();
        var finalCodes = requestedIds.Select(id => allPermissions[id].Code).OrderBy(c => c, StringComparer.Ordinal).ToList();

        await authHelper.WriteAuditEventAsync(
            actorUserId: actor.Id,
            operation: "ROLE_PERMISSIONS_UPDATED",
            resourceKind: "ROLE",
            resourceId: roleId,
            result: "SUCCESS",
            summary: new
            {
                role.Name,
                addedCodes,
                removedCodes,
                totalGrantedCount = finalCodes.Count
            },
            cancellationToken);

        return new RolePermissionsResponse(
            RoleId: role.Id,
            RoleName: role.Name,
            IsActive: role.IsActive,
            IsPinned: role.IsPinned,
            PermissionIds: requestedIds,
            PermissionCodes: finalCodes,
            TraceId: traceId);
    }

    public async Task<UserPermissionsMatrixResponse> GetUserPermissionsMatrixAsync(
        ClaimsPrincipal actorPrincipal,
        Guid targetUserId,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        await authHelper.RequireGeneralPermissionAsync(
            actorPrincipal,
            UserPermissionCodes.PermissionsView,
            "USER_PERMISSIONS_VIEW_DENIED",
            targetResourceId: targetUserId,
            resourceKind: "USER",
            cancellationToken: cancellationToken);

        await EnsureCanonicalDictionarySeededAsync(cancellationToken);

        return await BuildUserPermissionsMatrixResponseAsync(targetUserId, traceId, cancellationToken);
    }

    public async Task<UserPermissionsMatrixResponse> ReplaceUserPermissionOverridesAsync(
        ClaimsPrincipal actorPrincipal,
        Guid targetUserId,
        ReplaceUserPermissionOverridesRequest request,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var actor = await authHelper.RequireGeneralPermissionAsync(
            actorPrincipal,
            UserPermissionCodes.PermissionsManage,
            "USER_OVERRIDES_UPDATE_DENIED",
            targetResourceId: targetUserId,
            resourceKind: "USER",
            cancellationToken: cancellationToken);

        await EnsureCanonicalDictionarySeededAsync(cancellationToken);

        var targetUser = await dbContext.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == targetUserId, cancellationToken);
        if (targetUser is null)
        {
            throw ApiContractException.NotFound(
                messageAr: "حساب المستخدم المطلوب غير موجود.",
                messageEn: "Target user account was not found.");
        }

        var rawOverrides = request.Overrides ?? [];
        var seenPermissionIds = new HashSet<Guid>();
        var desiredOverrides = new Dictionary<Guid, string>();

        foreach (var item in rawOverrides)
        {
            if (!seenPermissionIds.Add(item.PermissionId))
            {
                throw ApiContractException.UnprocessableEntity(
                    messageAr: "لا يجوز تكرار نفس الصلاحية في قائمة الاستثناءات الفردية.",
                    messageEn: "Duplicate permissionId detected in permission overrides payload.");
            }

            var normalizedDecision = (item.Decision ?? string.Empty).Trim().ToUpperInvariant();
            if (normalizedDecision is "INHERIT" or "NONE" or "")
            {
                // Explicit INHERIT removes any override row (reverts to role inheritance)
                continue;
            }

            if (normalizedDecision is not ("GRANT" or "REVOKE"))
            {
                throw ApiContractException.UnprocessableEntity(
                    messageAr: "قرار الاستثناء الفردي يجب أن يكون GRANT أو REVOKE أو INHERIT.",
                    messageEn: "Override decision must be GRANT, REVOKE, or INHERIT.");
            }

            desiredOverrides[item.PermissionId] = normalizedDecision;
        }

        var allPermissions = await dbContext.Permissions.AsNoTracking()
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        if (seenPermissionIds.Any(id => !allPermissions.ContainsKey(id)))
        {
            throw ApiContractException.UnprocessableEntity(
                messageAr: "توجد صلاحية واحدة أو أكثر غير معرفة في قاموس النظام.",
                messageEn: "One or more permission IDs do not exist in the system dictionary.");
        }

        // Load existing overrides and inherited active role permissions for escalation checks
        var existingOverrides = await dbContext.UserPermissionOverrides
            .Where(uo => uo.UserId == targetUserId)
            .ToListAsync(cancellationToken);
        var existingOverrideMap = existingOverrides.ToDictionary(uo => uo.PermissionId, uo => uo.Decision);

        var inheritedPermissionIds = await (
            from ur in dbContext.UserRoles.AsNoTracking()
            join r in dbContext.Roles.AsNoTracking() on ur.RoleId equals r.Id
            join rp in dbContext.RolePermissions.AsNoTracking() on r.Id equals rp.RoleId
            where ur.UserId == targetUserId && r.IsActive
            select rp.PermissionId)
            .Distinct()
            .ToHashSetAsync(cancellationToken);

        // Anti-privilege-escalation guard:
        // Actor cannot introduce any new effective capability OR GRANT override for a permission they do not hold
        var (_, actorEffectiveCodes) = await authHelper.ResolveRolesAndEffectivePermissionsAsync(
            actor.Id,
            cancellationToken);
        var actorPermSet = new HashSet<string>(actorEffectiveCodes, StringComparer.OrdinalIgnoreCase);

        foreach (var permId in allPermissions.Keys)
        {
            existingOverrideMap.TryGetValue(permId, out var oldDecision);
            desiredOverrides.TryGetValue(permId, out var newDecision);

            var allowedBefore = EvaluatePermissionDecision(inheritedPermissionIds.Contains(permId), oldDecision);
            var allowedAfter = EvaluatePermissionDecision(inheritedPermissionIds.Contains(permId), newDecision);

            var introducingGrant = string.Equals(newDecision, "GRANT", StringComparison.OrdinalIgnoreCase) &&
                                   !string.Equals(oldDecision, "GRANT", StringComparison.OrdinalIgnoreCase);
            var gainingEffectiveAccess = !allowedBefore && allowedAfter;

            if (introducingGrant || gainingEffectiveAccess)
            {
                var code = allPermissions[permId].Code;
                if (!actorPermSet.Contains(code))
                {
                    await authHelper.WriteAuditEventAsync(
                        actorUserId: actor.Id,
                        operation: "USER_OVERRIDES_ESCALATION_BLOCKED",
                        resourceKind: "USER",
                        resourceId: targetUserId,
                        result: "FORBIDDEN",
                        summary: new { targetUserId, blockedPermissionCode = code },
                        cancellationToken);

                    throw ApiContractException.Forbidden(
                        messageAr: $"لا يمكنك منح صلاحية ({code}) للمستخدم لأن حسابك نفسه لا يملكها فعليًا (منع تصعيد الصلاحيات).",
                        messageEn: $"Privilege escalation blocked: you cannot grant '{code}' because your account does not hold it.");
                }
            }
        }

        // Critical capability retention guard for active administrators
        if (targetUser.IsActive && !string.IsNullOrEmpty(targetUser.PasswordHash))
        {
            await EnsureCriticalCapabilitiesRetainedAsync(
                simulatedRoleId: null,
                simulatedRolePermissionIds: null,
                simulatedUserId: targetUserId,
                simulatedUserOverrides: desiredOverrides,
                cancellationToken: cancellationToken);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        if (existingOverrides.Count > 0)
        {
            dbContext.UserPermissionOverrides.RemoveRange(existingOverrides);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        foreach (var (permId, decision) in desiredOverrides)
        {
            dbContext.UserPermissionOverrides.Add(new UserPermissionOverrideEntity
            {
                Id = Guid.NewGuid(),
                UserId = targetUserId,
                PermissionId = permId,
                Decision = decision
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var grantedCodes = desiredOverrides
            .Where(kv => kv.Value == "GRANT")
            .Select(kv => allPermissions[kv.Key].Code)
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToList();
        var revokedCodes = desiredOverrides
            .Where(kv => kv.Value == "REVOKE")
            .Select(kv => allPermissions[kv.Key].Code)
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToList();
        var revertedToInheritedCodes = existingOverrides
            .Where(eo => !desiredOverrides.ContainsKey(eo.PermissionId))
            .Select(eo => allPermissions[eo.PermissionId].Code)
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToList();

        await authHelper.WriteAuditEventAsync(
            actorUserId: actor.Id,
            operation: "USER_PERMISSION_OVERRIDES_UPDATED",
            resourceKind: "USER",
            resourceId: targetUserId,
            result: "SUCCESS",
            summary: new
            {
                grantedCodes,
                revokedCodes,
                revertedToInheritedCodes
            },
            cancellationToken);

        return await BuildUserPermissionsMatrixResponseAsync(targetUserId, traceId, cancellationToken);
    }

    private Task EnsureCanonicalDictionarySeededAsync(CancellationToken cancellationToken) =>
        CanonicalPermissionSyncHelper.EnsureSeededAndBackfilledAsync(dbContext, cancellationToken);

    private async Task<List<PermissionDictionaryItemDto>> LoadOrderedCatalogItemsAsync(CancellationToken cancellationToken)
    {
        var dbRows = await dbContext.Permissions.AsNoTracking()
            .Where(p => p.Resource != DocumentPermissionCatalog.ResourceName)
            .ToListAsync(cancellationToken);
        var catalogOrder = PlatformPermissionCatalog.CanonicalEntries
            .Select((entry, idx) => new { entry.Code, idx })
            .ToDictionary(x => x.Code, x => x.idx, StringComparer.OrdinalIgnoreCase);

        return dbRows
            .OrderBy(p => catalogOrder.TryGetValue(p.Code, out var idx) ? idx : int.MaxValue)
            .ThenBy(p => p.Code, StringComparer.Ordinal)
            .Select(p =>
            {
                var meta = PlatformPermissionCatalog.ResolveMetadata(p.Code, p.Resource, p.Action);
                return new PermissionDictionaryItemDto(
                    Id: p.Id,
                    Code: p.Code,
                    Resource: p.Resource,
                    Action: p.Action,
                    ScreenId: meta.ScreenId,
                    GroupId: meta.GroupId,
                    ColumnId: meta.ColumnId);
            })
            .ToList();
    }

    private async Task<UserPermissionsMatrixResponse> BuildUserPermissionsMatrixResponseAsync(
        Guid targetUserId,
        string traceId,
        CancellationToken cancellationToken)
    {
        var targetUser = await dbContext.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == targetUserId, cancellationToken);
        if (targetUser is null)
        {
            throw ApiContractException.NotFound(
                messageAr: "حساب المستخدم المطلوب غير موجود.",
                messageEn: "Target user account was not found.");
        }

        var catalogItems = await LoadOrderedCatalogItemsAsync(cancellationToken);

        var activeRoleRows = await (
            from ur in dbContext.UserRoles.AsNoTracking()
            join r in dbContext.Roles.AsNoTracking() on ur.RoleId equals r.Id
            where ur.UserId == targetUserId && r.IsActive
            orderby r.Name
            select new RoleSummaryDto(r.Id, r.Name))
            .ToListAsync(cancellationToken);

        var activeRoleIds = activeRoleRows.Select(r => r.Id).ToList();
        var activeRoleMap = activeRoleRows.ToDictionary(r => r.Id);

        var rolePermPairs = activeRoleIds.Count == 0
            ? []
            : await dbContext.RolePermissions.AsNoTracking()
                .Where(rp => activeRoleIds.Contains(rp.RoleId))
                .Select(rp => new { rp.RoleId, rp.PermissionId })
                .ToListAsync(cancellationToken);

        var rolesByPermission = rolePermPairs
            .GroupBy(x => x.PermissionId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<RoleSummaryDto>)g
                    .Select(x => activeRoleMap[x.RoleId])
                    .OrderBy(r => r.Name, StringComparer.Ordinal)
                    .ToList());

        var overrideRows = await dbContext.UserPermissionOverrides.AsNoTracking()
            .Where(uo => uo.UserId == targetUserId)
            .ToDictionaryAsync(uo => uo.PermissionId, uo => uo.Decision, cancellationToken);

        var entries = new List<UserPermissionEntryDto>(catalogItems.Count);
        var effectiveAllowedCount = 0;

        foreach (var item in catalogItems)
        {
            var inheritedRoles = rolesByPermission.TryGetValue(item.Id, out var matchedRoles)
                ? matchedRoles
                : [];
            var isInherited = inheritedRoles.Count > 0;
            overrideRows.TryGetValue(item.Id, out var overrideDecision);

            var effectiveAllowed = EvaluatePermissionDecision(isInherited, overrideDecision);
            if (effectiveAllowed)
            {
                effectiveAllowedCount++;
            }

            var resolutionSource = string.Equals(overrideDecision, "REVOKE", StringComparison.OrdinalIgnoreCase)
                ? "USER_REVOKE"
                : string.Equals(overrideDecision, "GRANT", StringComparison.OrdinalIgnoreCase)
                    ? "USER_GRANT"
                    : (isInherited ? "ROLE_INHERITED" : "DENIED_DEFAULT");

            entries.Add(new UserPermissionEntryDto(
                PermissionId: item.Id,
                Code: item.Code,
                Resource: item.Resource,
                Action: item.Action,
                ScreenId: item.ScreenId,
                GroupId: item.GroupId,
                ColumnId: item.ColumnId,
                IsInherited: isInherited,
                InheritedFromRoles: inheritedRoles,
                OverrideDecision: overrideDecision,
                EffectiveAllowed: effectiveAllowed,
                ResolutionSource: resolutionSource));
        }

        return new UserPermissionsMatrixResponse(
            UserId: targetUser.Id,
            FullName: targetUser.FullName,
            Email: targetUser.Email,
            IsActive: targetUser.IsActive,
            ActiveRoles: activeRoleRows,
            Permissions: entries,
            Columns: PlatformPermissionCatalog.ActionColumns,
            Screens: PlatformPermissionCatalog.ScreenRows,
            EffectiveAllowedCount: effectiveAllowedCount,
            TraceId: traceId);
    }

    private static bool EvaluatePermissionDecision(bool isInherited, string? overrideDecision)
    {
        if (string.Equals(overrideDecision, "REVOKE", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.Equals(overrideDecision, "GRANT", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return isInherited;
    }

    private async Task EnsureCriticalCapabilitiesRetainedAsync(
        Guid? simulatedRoleId,
        IReadOnlySet<Guid>? simulatedRolePermissionIds,
        Guid? simulatedUserId,
        IReadOnlyDictionary<Guid, string>? simulatedUserOverrides,
        CancellationToken cancellationToken)
    {
        var activeUserIds = await dbContext.Users.AsNoTracking()
            .Where(u => u.IsActive && u.PasswordHash != null)
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);

        var criticalPermissions = await dbContext.Permissions.AsNoTracking()
            .Where(p => CriticalAdminPermissionCodes.Contains(p.Code))
            .ToListAsync(cancellationToken);

        var userRoles = await (
            from ur in dbContext.UserRoles.AsNoTracking()
            join r in dbContext.Roles.AsNoTracking() on ur.RoleId equals r.Id
            where activeUserIds.Contains(ur.UserId) && r.IsActive
            select new { ur.UserId, ur.RoleId })
            .ToListAsync(cancellationToken);

        var rolePermissions = await dbContext.RolePermissions.AsNoTracking()
            .Select(rp => new { rp.RoleId, rp.PermissionId })
            .ToListAsync(cancellationToken);

        var rolePermsByRole = rolePermissions
            .GroupBy(x => x.RoleId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.PermissionId).ToHashSet());

        if (simulatedRoleId is not null && simulatedRolePermissionIds is not null)
        {
            rolePermsByRole[simulatedRoleId.Value] = [.. simulatedRolePermissionIds];
        }

        var existingOverrides = await dbContext.UserPermissionOverrides.AsNoTracking()
            .Where(uo => activeUserIds.Contains(uo.UserId))
            .ToListAsync(cancellationToken);

        var overridesByUser = existingOverrides
            .GroupBy(x => x.UserId)
            .ToDictionary(g => g.Key, g => g.ToDictionary(x => x.PermissionId, x => x.Decision));

        if (simulatedUserId is not null && simulatedUserOverrides is not null)
        {
            overridesByUser[simulatedUserId.Value] = new Dictionary<Guid, string>(simulatedUserOverrides);
        }

        foreach (var critical in criticalPermissions)
        {
            var hasHolder = false;
            foreach (var uid in activeUserIds)
            {
                var userRoleIds = userRoles.Where(ur => ur.UserId == uid).Select(ur => ur.RoleId);
                var inherited = userRoleIds.Any(rid =>
                    rolePermsByRole.TryGetValue(rid, out var perms) && perms.Contains(critical.Id));

                string? decision = null;
                if (overridesByUser.TryGetValue(uid, out var userMap))
                {
                    userMap.TryGetValue(critical.Id, out decision);
                }

                if (EvaluatePermissionDecision(inherited, decision))
                {
                    hasHolder = true;
                    break;
                }
            }

            if (!hasHolder)
            {
                throw ApiContractException.UnprocessableEntity(
                    messageAr: "لا يمكن حفظ هذا التغيير لأنه سيفقد النظام آخر حساب إداري فعّال يملك صلاحية إدارة الصلاحيات والمستخدمين.",
                    messageEn: $"Cannot apply change because no active user would retain critical permission '{critical.Code}'.");
            }
        }
    }
}

