using System.Security.Claims;
using ApiGovernance.Modules.IdentityAccess.Contracts;
using ApiGovernance.Shared.Contracts;
using ApiGovernance.Shared.Persistence;
using ApiGovernance.Shared.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace ApiGovernance.Modules.IdentityAccess.Services;

public interface IRoleManagementService
{
    Task<PagedRolesResponse> GetRolesAsync(
        ClaimsPrincipal actorPrincipal,
        int page,
        int pageSize,
        string? search,
        bool? isActive,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<RoleDetailsDto> CreateRoleAsync(
        ClaimsPrincipal actorPrincipal,
        CreateRoleRequest request,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<RoleDetailsDto> UpdateRoleAsync(
        ClaimsPrincipal actorPrincipal,
        Guid roleId,
        UpdateRoleRequest request,
        string traceId,
        CancellationToken cancellationToken = default);

    Task DeleteRoleAsync(
        ClaimsPrincipal actorPrincipal,
        Guid roleId,
        CancellationToken cancellationToken = default);

    Task<ReplaceUserRolesResponse> ReplaceUserRolesAsync(
        ClaimsPrincipal actorPrincipal,
        Guid targetUserId,
        ReplaceUserRolesRequest request,
        string traceId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Implements Task I03 Role Management CRUD and atomic multi-role assignment for users.
/// Strictly enforces ADR-01: NO role-name-based hardcoded privileges; permissions are resolved
/// dynamically from role_permissions + user_permission_overrides across all assigned active roles.
/// </summary>
public sealed class RoleManagementService(
    ApiGovernanceDbContext dbContext,
    IdentityAccessAuthorizationHelper authHelper) : IRoleManagementService
{
    public async Task<PagedRolesResponse> GetRolesAsync(
        ClaimsPrincipal actorPrincipal,
        int page,
        int pageSize,
        string? search,
        bool? isActive,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        await authHelper.RequireGeneralPermissionAsync(
            actorPrincipal,
            UserPermissionCodes.RolesView,
            "ROLES_LIST_DENIED",
            resourceKind: "ROLE",
            cancellationToken: cancellationToken);

        var safePage = Math.Max(1, page);
        var safePageSize = Math.Clamp(pageSize, 1, 100);

        var query = dbContext.Roles.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(r => r.Name.ToLower().Contains(term));
        }

        if (isActive is not null)
        {
            query = query.Where(r => r.IsActive == isActive.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)safePageSize));
        var normalizedPage = Math.Min(safePage, totalPages);

        var roles = await query
            .OrderByDescending(r => r.IsPinned)
            .ThenBy(r => r.Name)
            .Skip((normalizedPage - 1) * safePageSize)
            .Take(safePageSize)
            .ToListAsync(cancellationToken);

        var roleIds = roles.Select(r => r.Id).ToList();
        var userCounts = roleIds.Count == 0
            ? new Dictionary<Guid, int>()
            : await dbContext.UserRoles.AsNoTracking()
                .Where(ur => roleIds.Contains(ur.RoleId))
                .GroupBy(ur => ur.RoleId)
                .ToDictionaryAsync(g => g.Key, g => g.Count(), cancellationToken);

        var permCounts = roleIds.Count == 0
            ? new Dictionary<Guid, int>()
            : await dbContext.RolePermissions.AsNoTracking()
                .Where(rp => roleIds.Contains(rp.RoleId))
                .GroupBy(rp => rp.RoleId)
                .ToDictionaryAsync(g => g.Key, g => g.Count(), cancellationToken);

        var items = roles.Select(r => new RoleDetailsDto(
            Id: r.Id,
            Name: r.Name,
            IsActive: r.IsActive,
            IsPinned: r.IsPinned,
            AssignedUsersCount: userCounts.GetValueOrDefault(r.Id, 0),
            GrantedPermissionsCount: permCounts.GetValueOrDefault(r.Id, 0),
            TraceId: traceId)).ToList();

        return new PagedRolesResponse(
            Items: items,
            Page: normalizedPage,
            PageSize: safePageSize,
            TotalCount: totalCount,
            TotalPages: totalPages,
            TraceId: traceId);
    }

    public async Task<RoleDetailsDto> CreateRoleAsync(
        ClaimsPrincipal actorPrincipal,
        CreateRoleRequest request,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var actor = await authHelper.RequireGeneralPermissionAsync(
            actorPrincipal,
            UserPermissionCodes.RolesCreate,
            "ROLE_CREATE_DENIED",
            resourceKind: "ROLE",
            cancellationToken: cancellationToken);

        var trimmedName = request.Name.Trim();
        var normalizedUpper = trimmedName.ToUpperInvariant();

        var exists = await dbContext.Roles.AsNoTracking()
            .AnyAsync(r => r.Name.ToUpper() == normalizedUpper, cancellationToken);
        if (exists)
        {
            throw ApiContractException.Conflict(
                messageAr: "يوجد دور مسجل مسبقًا بهذا الاسم.",
                messageEn: "A role with this name already exists.");
        }

        // New role strictly starts with 0 permissions (A14 / no hardcoded role-name privileges)
        var role = new RoleEntity
        {
            Id = Guid.NewGuid(),
            Name = trimmedName,
            IsActive = true,
            IsPinned = request.IsPinned
        };

        dbContext.Roles.Add(role);
        await dbContext.SaveChangesAsync(cancellationToken);

        await authHelper.WriteAuditEventAsync(
            actorUserId: actor.Id,
            operation: "ROLE_CREATED",
            resourceKind: "ROLE",
            resourceId: role.Id,
            result: "SUCCESS",
            summary: new { role.Name, role.IsPinned, grantedPermissions = 0 },
            cancellationToken);

        return new RoleDetailsDto(
            Id: role.Id,
            Name: role.Name,
            IsActive: role.IsActive,
            IsPinned: role.IsPinned,
            AssignedUsersCount: 0,
            GrantedPermissionsCount: 0,
            TraceId: traceId);
    }

    public async Task<RoleDetailsDto> UpdateRoleAsync(
        ClaimsPrincipal actorPrincipal,
        Guid roleId,
        UpdateRoleRequest request,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var actor = await authHelper.RequireGeneralPermissionAsync(
            actorPrincipal,
            UserPermissionCodes.RolesEdit,
            "ROLE_EDIT_DENIED",
            targetResourceId: roleId,
            resourceKind: "ROLE",
            cancellationToken: cancellationToken);

        var role = await dbContext.Roles.FirstOrDefaultAsync(r => r.Id == roleId, cancellationToken);
        if (role is null)
        {
            throw ApiContractException.NotFound(
                messageAr: "الدور المطلوب غير موجود.",
                messageEn: "The requested role was not found.");
        }

        var nextPinned = request.IsPinned ?? role.IsPinned;
        var nextActive = request.IsActive ?? role.IsActive;

        if (nextPinned && !nextActive)
        {
            throw ApiContractException.UnprocessableEntity(
                messageAr: "لا يمكن تعطيل دور مثبت. قم بإلغاء التثبيت أولًا إذا كان مسموحًا.",
                messageEn: "A pinned role cannot be deactivated while pinned.");
        }

        if (request.Name is not null)
        {
            var trimmedName = request.Name.Trim();
            var upperName = trimmedName.ToUpperInvariant();
            var conflict = await dbContext.Roles.AsNoTracking()
                .AnyAsync(r => r.Id != roleId && r.Name.ToUpper() == upperName, cancellationToken);
            if (conflict)
            {
                throw ApiContractException.Conflict(
                    messageAr: "اسم الدور مستخدم بالفعل لدور آخر.",
                    messageEn: "Another role already uses this name.");
            }
            role.Name = trimmedName;
        }

        var deactivating = role.IsActive && !nextActive;
        var activating = !role.IsActive && nextActive;
        if (deactivating)
        {
            await EnsureAdminCapabilitiesRetainedAfterRoleChangeAsync(
                deactivatedRoleId: role.Id,
                cancellationToken: cancellationToken);
        }

        role.IsPinned = nextPinned;
        role.IsActive = nextActive;

        await dbContext.SaveChangesAsync(cancellationToken);

        var op = deactivating
            ? "ROLE_DEACTIVATED"
            : (activating ? "ROLE_ACTIVATED" : "ROLE_UPDATED");

        await authHelper.WriteAuditEventAsync(
            actorUserId: actor.Id,
            operation: op,
            resourceKind: "ROLE",
            resourceId: role.Id,
            result: "SUCCESS",
            summary: new { role.Name, role.IsActive, role.IsPinned },
            cancellationToken);

        var userCount = await dbContext.UserRoles.AsNoTracking().CountAsync(ur => ur.RoleId == role.Id, cancellationToken);
        var permCount = await dbContext.RolePermissions.AsNoTracking().CountAsync(rp => rp.RoleId == role.Id, cancellationToken);

        return new RoleDetailsDto(
            Id: role.Id,
            Name: role.Name,
            IsActive: role.IsActive,
            IsPinned: role.IsPinned,
            AssignedUsersCount: userCount,
            GrantedPermissionsCount: permCount,
            TraceId: traceId);
    }

    public async Task DeleteRoleAsync(
        ClaimsPrincipal actorPrincipal,
        Guid roleId,
        CancellationToken cancellationToken = default)
    {
        var actor = await authHelper.RequireGeneralPermissionAsync(
            actorPrincipal,
            UserPermissionCodes.RolesDelete,
            "ROLE_DELETE_DENIED",
            targetResourceId: roleId,
            resourceKind: "ROLE",
            cancellationToken: cancellationToken);

        var role = await dbContext.Roles.FirstOrDefaultAsync(r => r.Id == roleId, cancellationToken);
        if (role is null)
        {
            throw ApiContractException.NotFound(
                messageAr: "الدور المطلوب غير موجود.",
                messageEn: "The requested role was not found.");
        }

        if (role.IsPinned)
        {
            throw ApiContractException.UnprocessableEntity(
                messageAr: "لا يمكن حذف دور مثبت.",
                messageEn: "Pinned roles cannot be deleted.");
        }

        var hasUsers = await dbContext.UserRoles.AsNoTracking().AnyAsync(ur => ur.RoleId == roleId, cancellationToken);
        var hasPerms = await dbContext.RolePermissions.AsNoTracking().AnyAsync(rp => rp.RoleId == roleId, cancellationToken);
        var hasRouteStages = await dbContext.ApprovalRouteStages.AsNoTracking().AnyAsync(s => s.ResponsibleRoleId == roleId, cancellationToken);
        var hasDocPerms = await dbContext.DocumentRolePermissions.AsNoTracking().AnyAsync(d => d.RoleId == roleId, cancellationToken);
        var hasRunStages = await dbContext.WorkflowRunStages.AsNoTracking().AnyAsync(w => w.ResponsibleRoleIdSnapshot == roleId, cancellationToken);

        if (hasUsers || hasPerms || hasRouteStages || hasDocPerms || hasRunStages)
        {
            throw ApiContractException.UnprocessableEntity(
                messageAr: "لا يمكن حذف دور مستخدم أو مرتبط بمستخدمين أو صلاحيات أو مسارات اعتماد.",
                messageEn: "Cannot delete a role referenced by users, permissions, documents, or workflow stages.");
        }

        dbContext.Roles.Remove(role);
        await dbContext.SaveChangesAsync(cancellationToken);

        await authHelper.WriteAuditEventAsync(
            actorUserId: actor.Id,
            operation: "ROLE_DELETED",
            resourceKind: "ROLE",
            resourceId: role.Id,
            result: "SUCCESS",
            summary: new { role.Name },
            cancellationToken);
    }

    public async Task<ReplaceUserRolesResponse> ReplaceUserRolesAsync(
        ClaimsPrincipal actorPrincipal,
        Guid targetUserId,
        ReplaceUserRolesRequest request,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var actor = await authHelper.RequireGeneralPermissionAsync(
            actorPrincipal,
            UserPermissionCodes.UsersAssignRoles,
            "USER_ROLES_REPLACE_DENIED",
            targetResourceId: targetUserId,
            resourceKind: "USER",
            cancellationToken: cancellationToken);

        var targetUser = await dbContext.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == targetUserId, cancellationToken);
        if (targetUser is null)
        {
            throw ApiContractException.NotFound(
                messageAr: "حساب المستخدم المطلوب غير موجود.",
                messageEn: "Target user account was not found.");
        }

        var distinctRoleIds = (request.RoleIds ?? []).Distinct().ToList();
        var validatedRoles = await ValidateActiveRolesAsync(distinctRoleIds, cancellationToken);

        // Guard against stripping essential administrative capabilities from the last active user
        if (targetUser.IsActive && !string.IsNullOrEmpty(targetUser.PasswordHash))
        {
            await EnsureAdminCapabilitiesRetainedAfterUserRolesChangeAsync(
                targetUserId: targetUser.Id,
                nextRoleIds: distinctRoleIds,
                cancellationToken: cancellationToken);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var existingAssignments = await dbContext.UserRoles
            .Where(ur => ur.UserId == targetUserId)
            .ToListAsync(cancellationToken);

        var previousRoleIds = existingAssignments.Select(ur => ur.RoleId).ToList();

        dbContext.UserRoles.RemoveRange(existingAssignments);
        await dbContext.SaveChangesAsync(cancellationToken);

        foreach (var roleId in distinctRoleIds)
        {
            dbContext.UserRoles.Add(new UserRoleEntity
            {
                Id = Guid.NewGuid(),
                UserId = targetUserId,
                RoleId = roleId
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await authHelper.WriteAuditEventAsync(
            actorUserId: actor.Id,
            operation: "USER_ROLES_REPLACED",
            resourceKind: "USER",
            resourceId: targetUserId,
            result: "SUCCESS",
            summary: new { previousRoleIds, newRoleIds = distinctRoleIds },
            cancellationToken);

        var (_, effectivePermissions) = await authHelper.ResolveRolesAndEffectivePermissionsAsync(
            targetUserId,
            cancellationToken);

        var summaryRoles = validatedRoles
            .OrderBy(r => r.Name)
            .Select(r => new RoleSummaryDto(r.Id, r.Name))
            .ToList();

        return new ReplaceUserRolesResponse(
            UserId: targetUserId,
            Roles: summaryRoles,
            EffectivePermissionsCount: effectivePermissions.Count,
            TraceId: traceId);
    }

    internal async Task<List<RoleEntity>> ValidateActiveRolesAsync(
        IReadOnlyCollection<Guid> roleIds,
        CancellationToken cancellationToken)
    {
        if (roleIds.Count == 0)
        {
            return [];
        }

        var foundRoles = await dbContext.Roles.AsNoTracking()
            .Where(r => roleIds.Contains(r.Id))
            .ToListAsync(cancellationToken);

        if (foundRoles.Count != roleIds.Count || foundRoles.Any(r => !r.IsActive))
        {
            throw ApiContractException.UnprocessableEntity(
                messageAr: "أحد الأدوار المحددة غير موجود أو معطّل.",
                messageEn: "One or more selected roles do not exist or are inactive.");
        }

        return foundRoles;
    }

    private async Task EnsureAdminCapabilitiesRetainedAfterRoleChangeAsync(
        Guid deactivatedRoleId,
        CancellationToken cancellationToken)
    {
        var activeAdminCandidateIds = await dbContext.Users.AsNoTracking()
            .Where(u => u.IsActive && u.PasswordHash != null)
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);

        var activeOtherRoleIds = await dbContext.Roles.AsNoTracking()
            .Where(r => r.IsActive && r.Id != deactivatedRoleId)
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        var criticalCodes = new[] { UserPermissionCodes.RolesEdit, UserPermissionCodes.UsersAssignRoles };
        foreach (var code in criticalCodes)
        {
            var permId = await dbContext.Permissions.AsNoTracking()
                .Where(p => p.Code == code)
                .Select(p => (Guid?)p.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (permId is null) continue;

            var rolesWithPerm = activeOtherRoleIds.Count == 0
                ? new HashSet<Guid>()
                : (await dbContext.RolePermissions.AsNoTracking()
                    .Where(rp => activeOtherRoleIds.Contains(rp.RoleId) && rp.PermissionId == permId.Value)
                    .Select(rp => rp.RoleId)
                    .ToListAsync(cancellationToken)).ToHashSet();

            var usersWithRemainingRole = rolesWithPerm.Count == 0
                ? false
                : await dbContext.UserRoles.AsNoTracking()
                    .AnyAsync(ur => activeAdminCandidateIds.Contains(ur.UserId) && rolesWithPerm.Contains(ur.RoleId), cancellationToken);

            var usersWithGrantOverride = await dbContext.UserPermissionOverrides.AsNoTracking()
                .AnyAsync(uo => activeAdminCandidateIds.Contains(uo.UserId) && uo.PermissionId == permId.Value && uo.Decision == "GRANT", cancellationToken);

            if (!usersWithRemainingRole && !usersWithGrantOverride)
            {
                throw ApiContractException.UnprocessableEntity(
                    messageAr: "لا يمكن تعطيل هذا الدور لأنه يزيل آخر مسار صلاحية إدارية فعال عن المستخدمين.",
                    messageEn: $"Cannot deactivate role because no active user would retain '{code}'.");
            }
        }
    }

    private async Task EnsureAdminCapabilitiesRetainedAfterUserRolesChangeAsync(
        Guid targetUserId,
        IReadOnlyCollection<Guid> nextRoleIds,
        CancellationToken cancellationToken)
    {
        var otherActiveUserIds = await dbContext.Users.AsNoTracking()
            .Where(u => u.Id != targetUserId && u.IsActive && u.PasswordHash != null)
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);

        var otherPerms = await authHelper.ResolveRolesAndEffectivePermissionsBatchAsync(
            otherActiveUserIds,
            cancellationToken);

        // Compute what permissions targetUserId would still hold under nextRoleIds
        var targetRolePerms = nextRoleIds.Count == 0
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : (await (
                from rp in dbContext.RolePermissions.AsNoTracking()
                join p in dbContext.Permissions.AsNoTracking() on rp.PermissionId equals p.Id
                where nextRoleIds.Contains(rp.RoleId)
                select p.Code)
                .ToListAsync(cancellationToken)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var criticalCodes = new[] { UserPermissionCodes.UsersAssignRoles, UserPermissionCodes.UsersEdit };
        foreach (var code in criticalCodes)
        {
            var otherHas = otherPerms.Values.Any(v => v.Permissions.Contains(code, StringComparer.OrdinalIgnoreCase));
            var targetRetains = targetRolePerms.Contains(code);
            if (!otherHas && !targetRetains)
            {
                throw ApiContractException.UnprocessableEntity(
                    messageAr: "لا يمكن إزالة هذا الدور لأن النظام سيفقد آخر حساب فعّال يملك صلاحية إدارة المستخدمين والأدوار.",
                    messageEn: $"Cannot update user roles because no active user would retain '{code}'.");
            }
        }
    }
}
