using System.Security.Claims;
using ApiGovernance.Modules.IdentityAccess.Configuration;
using ApiGovernance.Modules.IdentityAccess.Contracts;
using ApiGovernance.Modules.IdentityAccess.Identity;
using ApiGovernance.Shared.Contracts;
using ApiGovernance.Shared.Persistence;
using ApiGovernance.Shared.Persistence.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ApiGovernance.Modules.IdentityAccess.Services;

public interface IUserManagementService
{
    Task<PagedUsersResponse> GetUsersAsync(
        ClaimsPrincipal actorPrincipal,
        int page,
        int pageSize,
        string? search,
        bool? isActive,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<UserSummaryDto> CreateUserAsync(
        ClaimsPrincipal actorPrincipal,
        CreateUserRequest request,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<UserSummaryDto> UpdateUserAsync(
        ClaimsPrincipal actorPrincipal,
        Guid targetUserId,
        UpdateUserRequest request,
        string traceId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Implements Task I02 MVP user lifecycle operations (list, create, edit, activate/deactivate)
/// backed by existing PostgreSQL 'users' table with strict ADR-01 permission checks,
/// immediate session revocation upon deactivation, and OP-01 audit events.
/// </summary>
public sealed class UserManagementService(
    ApiGovernanceDbContext dbContext,
    UserManager<UserEntity> userManager,
    ISessionRevocationService sessionRevocationService,
    IAuthTokenDeliveryChannel tokenDeliveryChannel,
    IdentityAccessAuthorizationHelper authHelper,
    IOptions<AuthenticationPolicyOptions> policyOptions) : IUserManagementService
{
    private readonly AuthenticationPolicyOptions _policy = policyOptions.Value;

    public async Task<PagedUsersResponse> GetUsersAsync(
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
            UserPermissionCodes.UsersView,
            "USERS_LIST_DENIED",
            cancellationToken: cancellationToken);

        var safePage = Math.Max(1, page);
        var safePageSize = Math.Clamp(pageSize, 1, 100);

        var query = dbContext.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(u =>
                u.FullName.ToLower().Contains(term) ||
                u.Email.ToLower().Contains(term));
        }

        if (isActive is not null)
        {
            query = query.Where(u => u.IsActive == isActive.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)safePageSize));
        var normalizedPage = Math.Min(safePage, totalPages);

        var users = await query
            .OrderByDescending(u => u.CreatedAt)
            .ThenBy(u => u.FullName)
            .Skip((normalizedPage - 1) * safePageSize)
            .Take(safePageSize)
            .ToListAsync(cancellationToken);

        var rolesMap = await authHelper.ResolveRolesAndEffectivePermissionsBatchAsync(
            users.Select(u => u.Id).ToList(),
            cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var items = users.Select(u => MapToSummaryDto(u, rolesMap[u.Id].Roles, traceId, now)).ToList();

        return new PagedUsersResponse(
            Items: items,
            Page: normalizedPage,
            PageSize: safePageSize,
            TotalCount: totalCount,
            TotalPages: totalPages,
            TraceId: traceId);
    }

    public async Task<UserSummaryDto> CreateUserAsync(
        ClaimsPrincipal actorPrincipal,
        CreateUserRequest request,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var actor = await authHelper.RequireGeneralPermissionAsync(
            actorPrincipal,
            UserPermissionCodes.UsersCreate,
            "USER_CREATE_DENIED",
            cancellationToken: cancellationToken);

        if (request.TriggerActivationToken)
        {
            await authHelper.RequireGeneralPermissionAsync(
                actorPrincipal,
                UserPermissionCodes.UsersPasswordReset,
                "USER_CREATE_ACTIVATION_DENIED",
                cancellationToken: cancellationToken);
        }

        var distinctRoleIds = (request.RoleIds ?? []).Distinct().ToList();
        var validatedRoles = new List<RoleEntity>();
        if (distinctRoleIds.Count > 0)
        {
            await authHelper.RequireGeneralPermissionAsync(
                actorPrincipal,
                UserPermissionCodes.UsersAssignRoles,
                "USER_CREATE_ROLES_DENIED",
                cancellationToken: cancellationToken);

            validatedRoles = await dbContext.Roles.AsNoTracking()
                .Where(r => distinctRoleIds.Contains(r.Id))
                .ToListAsync(cancellationToken);

            if (validatedRoles.Count != distinctRoleIds.Count || validatedRoles.Any(r => !r.IsActive))
            {
                throw ApiContractException.UnprocessableEntity(
                    messageAr: "أحد الأدوار المحددة غير موجود أو معطّل.",
                    messageEn: "One or more selected roles do not exist or are inactive.");
            }
        }

        var email = request.Email.Trim();
        var normalizedEmail = userManager.NormalizeEmail(email) ?? email.ToUpperInvariant();

        var emailExists = await dbContext.Users.AsNoTracking()
            .AnyAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken);
        if (emailExists)
        {
            throw ApiContractException.Conflict(
                messageAr: "يوجد حساب مستخدم مسجل مسبقًا بهذا البريد الإلكتروني.",
                messageEn: "A user account with this email address already exists.");
        }

        await ValidateConsumerEntityIfProvidedAsync(request.ConsumerEntityId, cancellationToken);

        var user = new UserEntity
        {
            Id = Guid.NewGuid(),
            FullName = request.FullName.Trim(),
            Email = email,
            NormalizedEmail = normalizedEmail,
            PasswordHash = null, // New account requires owner activation via single-use token (04_Authentication.md)
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString("N"),
            LockoutEnabled = true,
            IsActive = true,
            ConsumerEntityId = request.ConsumerEntityId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var createResult = await userManager.CreateAsync(user);
        if (!createResult.Succeeded)
        {
            throw ApiContractException.Conflict(
                messageAr: "تعذر إنشاء حساب المستخدم لتعارض بياناته مع حساب قائم.",
                messageEn: "Unable to create user account due to a conflicting record.");
        }

        if (distinctRoleIds.Count > 0)
        {
            foreach (var roleId in distinctRoleIds)
            {
                dbContext.UserRoles.Add(new UserRoleEntity
                {
                    Id = Guid.NewGuid(),
                    UserId = user.Id,
                    RoleId = roleId
                });
            }
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        await authHelper.WriteAuditEventAsync(
            actorUserId: actor.Id,
            operation: "USER_CREATED",
            resourceKind: "USER",
            resourceId: user.Id,
            result: "SUCCESS",
            summary: new
            {
                isActive = user.IsActive,
                isActivated = false,
                hasConsumerScope = user.ConsumerEntityId is not null,
                triggerActivationToken = request.TriggerActivationToken,
                assignedRoleIds = distinctRoleIds
            },
            cancellationToken);

        if (request.TriggerActivationToken)
        {
            var token = await userManager.GeneratePasswordResetTokenAsync(user);
            var expiresAt = DateTimeOffset.UtcNow.AddMinutes(Math.Max(1, _policy.ResetTokenLifespanMinutes));

            await tokenDeliveryChannel.DeliverPasswordResetTokenAsync(
                new AuthTokenDeliveryContext(
                    TargetUserId: user.Id,
                    TargetEmail: user.Email,
                    TargetFullName: user.FullName,
                    Purpose: "ACCOUNT_ACTIVATION",
                    SingleUseToken: token,
                    ExpiresAtUtc: expiresAt,
                    InitiatedByUserId: actor.Id),
                cancellationToken);

            await authHelper.WriteAuditEventAsync(
                actorUserId: actor.Id,
                operation: "AUTH_PASSWORD_RESET_INITIATED",
                resourceKind: "USER",
                resourceId: user.Id,
                result: "SUCCESS",
                summary: new { purpose = "ACCOUNT_ACTIVATION", deliveryChannel = tokenDeliveryChannel.ChannelCode },
                cancellationToken);
        }

        var assignedRoleSummaries = validatedRoles
            .OrderBy(r => r.Name)
            .Select(r => new RoleSummaryDto(r.Id, r.Name))
            .ToList();

        return MapToSummaryDto(user, assignedRoleSummaries, traceId, DateTimeOffset.UtcNow);
    }

    public async Task<UserSummaryDto> UpdateUserAsync(
        ClaimsPrincipal actorPrincipal,
        Guid targetUserId,
        UpdateUserRequest request,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var actor = await authHelper.RequireGeneralPermissionAsync(
            actorPrincipal,
            UserPermissionCodes.UsersEdit,
            "USER_EDIT_DENIED",
            targetResourceId: targetUserId,
            cancellationToken: cancellationToken);

        var targetUser = await dbContext.Users.FirstOrDefaultAsync(u => u.Id == targetUserId, cancellationToken);
        if (targetUser is null)
        {
            throw ApiContractException.NotFound(
                messageAr: "حساب المستخدم المطلوب غير موجود.",
                messageEn: "Target user account was not found.");
        }

        if (request.ConsumerEntityId is not null)
        {
            await ValidateConsumerEntityIfProvidedAsync(request.ConsumerEntityId, cancellationToken);
        }

        var wasActive = targetUser.IsActive;
        var deactivating = wasActive && request.IsActive == false;
        var reactivating = !wasActive && request.IsActive == true;

        if (deactivating)
        {
            if (actor.Id == targetUser.Id)
            {
                throw ApiContractException.UnprocessableEntity(
                    messageAr: "لا يمكنك تعطيل حسابك النشط الحالي.",
                    messageEn: "You cannot deactivate your own active account.");
            }

            // Prevent locking out the entire platform by deactivating the last active account with users.edit
            var hasAnotherActiveAdmin = await HasAnotherActiveUserWithPermissionAsync(
                excludedUserId: targetUser.Id,
                permissionCode: UserPermissionCodes.UsersEdit,
                cancellationToken);

            if (!hasAnotherActiveAdmin)
            {
                throw ApiContractException.UnprocessableEntity(
                    messageAr: "لا يمكن تعطيل آخر حساب فعّال يملك صلاحية إدارة المستخدمين.",
                    messageEn: "Cannot deactivate the last active user holding user-management permission.");
            }
        }

        if (request.FullName is not null)
        {
            targetUser.FullName = request.FullName.Trim();
        }

        if (request.ClearConsumerEntity)
        {
            targetUser.ConsumerEntityId = null;
        }
        else if (request.ConsumerEntityId is not null)
        {
            targetUser.ConsumerEntityId = request.ConsumerEntityId;
        }

        if (request.IsActive is not null)
        {
            targetUser.IsActive = request.IsActive.Value;
        }

        var updateResult = await userManager.UpdateAsync(targetUser);
        if (!updateResult.Succeeded)
        {
            throw ApiContractException.Conflict(
                messageAr: "تعذر تحديث بيانات المستخدم بسبب تعارض متزامن.",
                messageEn: "Unable to update user due to a concurrent modification.");
        }

        if (deactivating)
        {
            await userManager.UpdateSecurityStampAsync(targetUser);
            await sessionRevocationService.RevokeAllUserSessionsAsync(targetUser.Id, cancellationToken);
        }

        var operation = deactivating
            ? "USER_DEACTIVATED"
            : reactivating
                ? "USER_ACTIVATED"
                : "USER_UPDATED";

        await authHelper.WriteAuditEventAsync(
            actorUserId: actor.Id,
            operation: operation,
            resourceKind: "USER",
            resourceId: targetUser.Id,
            result: "SUCCESS",
            summary: new
            {
                isActive = targetUser.IsActive,
                sessionsRevoked = deactivating,
                consumerScopeChanged = request.ClearConsumerEntity || request.ConsumerEntityId is not null
            },
            cancellationToken);

        var (roles, _) = await authHelper.ResolveRolesAndEffectivePermissionsAsync(targetUser.Id, cancellationToken);
        return MapToSummaryDto(targetUser, roles, traceId, DateTimeOffset.UtcNow);
    }

    private async Task ValidateConsumerEntityIfProvidedAsync(
        Guid? consumerEntityId,
        CancellationToken cancellationToken)
    {
        if (consumerEntityId is null)
        {
            return;
        }

        var entity = await dbContext.ConsumerEntities.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == consumerEntityId.Value, cancellationToken);
        if (entity is null || !entity.IsActive)
        {
            throw ApiContractException.UnprocessableEntity(
                messageAr: "الجهة المستهلكة المحددة غير موجودة أو غير فعّالة.",
                messageEn: "The specified consumer entity does not exist or is inactive.");
        }
    }

    private async Task<bool> HasAnotherActiveUserWithPermissionAsync(
        Guid excludedUserId,
        string permissionCode,
        CancellationToken cancellationToken)
    {
        var activeCandidateIds = await dbContext.Users.AsNoTracking()
            .Where(u => u.Id != excludedUserId && u.IsActive && u.PasswordHash != null)
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);

        if (activeCandidateIds.Count == 0)
        {
            return false;
        }

        var resolved = await authHelper.ResolveRolesAndEffectivePermissionsBatchAsync(
            activeCandidateIds,
            cancellationToken);

        return resolved.Values.Any(v =>
            v.Permissions.Contains(permissionCode, StringComparer.OrdinalIgnoreCase));
    }

    private static UserSummaryDto MapToSummaryDto(
        UserEntity user,
        IReadOnlyList<RoleSummaryDto> roles,
        string traceId,
        DateTimeOffset now)
    {
        var isLockedOut = user.LockoutEnd is not null && user.LockoutEnd > now;
        return new UserSummaryDto(
            Id: user.Id,
            FullName: user.FullName,
            Email: user.Email,
            IsActive: user.IsActive,
            IsActivated: !string.IsNullOrEmpty(user.PasswordHash),
            IsLockedOut: isLockedOut,
            LockoutEnd: isLockedOut ? user.LockoutEnd : null,
            ConsumerEntityId: user.ConsumerEntityId,
            CreatedAt: user.CreatedAt,
            Roles: roles,
            TraceId: traceId);
    }
}

