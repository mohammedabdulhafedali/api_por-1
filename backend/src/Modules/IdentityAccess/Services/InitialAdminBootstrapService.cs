using ApiGovernance.Modules.IdentityAccess.Configuration;
using ApiGovernance.Modules.IdentityAccess.Contracts;
using ApiGovernance.Shared.Persistence;
using ApiGovernance.Shared.Persistence.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ApiGovernance.Modules.IdentityAccess.Services;

public sealed record InitialAdminBootstrapRequest(
    string Email,
    string FullName,
    string? InitialPassword = null);

public sealed record InitialAdminBootstrapResult(
    bool Created,
    string StatusCode,
    Guid? AdminUserId,
    bool RequiresActivation);

public interface IInitialAdminBootstrapService
{
    Task EnsureGovernanceDictionaryAsync(CancellationToken cancellationToken = default);

    Task<InitialAdminBootstrapResult> BootstrapFirstAdminAsync(
        InitialAdminBootstrapRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Secure one-time bootstrap service for the initial platform administrator account (Task I02).
/// Seeds canonical permission codes and the pinned initial admin role, then provisions the first admin
/// only when no existing user already holds user-management permissions. Never hardcodes credentials.
/// </summary>
public sealed class InitialAdminBootstrapService(
    ApiGovernanceDbContext dbContext,
    UserManager<UserEntity> userManager,
    IAuthTokenDeliveryChannel tokenDeliveryChannel,
    IdentityAccessAuthorizationHelper authHelper,
    IOptions<AuthenticationPolicyOptions> policyOptions) : IInitialAdminBootstrapService
{
    public const string DefaultSystemAdminRoleName = "مدير النظام / SystemAdmin";

    private static readonly (string Code, string Resource, string Action)[] BaselinePermissions =
        PlatformPermissionCatalog.CanonicalEntries
            .Select(e => (e.Code, e.Resource, e.Action))
            .ToArray();

    private readonly AuthenticationPolicyOptions _policy = policyOptions.Value;

    public async Task EnsureGovernanceDictionaryAsync(CancellationToken cancellationToken = default)
    {
        await CanonicalPermissionSyncHelper.EnsureSeededAndBackfilledAsync(dbContext, cancellationToken);

        var adminRole = await dbContext.Roles
            .FirstOrDefaultAsync(r => r.Name == DefaultSystemAdminRoleName, cancellationToken);

        if (adminRole is null)
        {
            adminRole = new RoleEntity
            {
                Id = Guid.NewGuid(),
                Name = DefaultSystemAdminRoleName,
                IsActive = true,
                IsPinned = true
            };
            dbContext.Roles.Add(adminRole);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var allPermissionIds = await dbContext.Permissions.AsNoTracking()
            .Where(p => BaselinePermissions.Select(bp => bp.Code).Contains(p.Code))
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);

        var existingRolePerms = await dbContext.RolePermissions.AsNoTracking()
            .Where(rp => rp.RoleId == adminRole.Id)
            .Select(rp => rp.PermissionId)
            .ToListAsync(cancellationToken);
        var grantedSet = new HashSet<Guid>(existingRolePerms);

        foreach (var permissionId in allPermissionIds)
        {
            if (!grantedSet.Contains(permissionId))
            {
                dbContext.RolePermissions.Add(new RolePermissionEntity
                {
                    Id = Guid.NewGuid(),
                    RoleId = adminRole.Id,
                    PermissionId = permissionId
                });
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<InitialAdminBootstrapResult> BootstrapFirstAdminAsync(
        InitialAdminBootstrapRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.FullName))
        {
            throw new ArgumentException("Bootstrap admin Email and FullName are required.");
        }

        await EnsureGovernanceDictionaryAsync(cancellationToken);

        // Enforce strict permanent one-time guarantee: disable once any user exists OR BOOTSTRAP_ADMIN_CREATED succeeded
        var alreadyBootstrappedAudit = await dbContext.AuditEvents.AsNoTracking()
            .AnyAsync(a => a.Operation == "BOOTSTRAP_ADMIN_CREATED" && a.Result == "SUCCESS", cancellationToken);
        var anyUsersExist = await dbContext.Users.AsNoTracking().AnyAsync(cancellationToken);

        if (alreadyBootstrappedAudit || anyUsersExist)
        {
            var existingAdminId = await dbContext.Users.AsNoTracking()
                .Where(u => u.IsActive)
                .OrderBy(u => u.CreatedAt)
                .Select(u => (Guid?)u.Id)
                .FirstOrDefaultAsync(cancellationToken);

            return new InitialAdminBootstrapResult(
                Created: false,
                StatusCode: "BOOTSTRAP_ALREADY_INITIALIZED",
                AdminUserId: existingAdminId,
                RequiresActivation: false);
        }

        var email = request.Email.Trim();
        var normalizedEmail = userManager.NormalizeEmail(email) ?? email.ToUpperInvariant();
        if (await dbContext.Users.AsNoTracking().AnyAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken))
        {
            throw new InvalidOperationException("Cannot bootstrap: target email already belongs to an existing user.");
        }

        var adminRole = await dbContext.Roles.AsNoTracking()
            .FirstAsync(r => r.Name == DefaultSystemAdminRoleName, cancellationToken);

        var adminId = Guid.NewGuid();
        var derivedUsername = Identity.PlatformUserStore.DeriveDefaultUsername(email, adminId);
        var adminUser = new UserEntity
        {
            Id = adminId,
            FullName = request.FullName.Trim(),
            Username = derivedUsername,
            NormalizedUsername = derivedUsername.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = normalizedEmail,
            PasswordHash = null,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString("N"),
            LockoutEnabled = true,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var hasExplicitPassword = !string.IsNullOrEmpty(request.InitialPassword);
        var createResult = hasExplicitPassword
            ? await userManager.CreateAsync(adminUser, request.InitialPassword!)
            : await userManager.CreateAsync(adminUser);

        if (!createResult.Succeeded)
        {
            var reason = string.Join("; ", createResult.Errors.Select(e => e.Description));
            throw new InvalidOperationException($"Bootstrap admin creation failed policy validation: {reason}");
        }

        dbContext.UserRoles.Add(new UserRoleEntity
        {
            Id = Guid.NewGuid(),
            UserId = adminUser.Id,
            RoleId = adminRole.Id
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        if (!hasExplicitPassword)
        {
            var token = await userManager.GeneratePasswordResetTokenAsync(adminUser);
            var expiresAt = DateTimeOffset.UtcNow.AddMinutes(Math.Max(1, _policy.ResetTokenLifespanMinutes));

            await tokenDeliveryChannel.DeliverPasswordResetTokenAsync(
                new AuthTokenDeliveryContext(
                    TargetUserId: adminUser.Id,
                    TargetEmail: adminUser.Email,
                    TargetFullName: adminUser.FullName,
                    Purpose: "ACCOUNT_ACTIVATION",
                    SingleUseToken: token,
                    ExpiresAtUtc: expiresAt,
                    InitiatedByUserId: null),
                cancellationToken);
        }

        await authHelper.WriteAuditEventAsync(
            actorUserId: adminUser.Id,
            operation: "BOOTSTRAP_ADMIN_CREATED",
            resourceKind: "USER",
            resourceId: adminUser.Id,
            result: "SUCCESS",
            summary: new
            {
                mode = hasExplicitPassword ? "OPERATOR_ENV_PASSWORD" : "SINGLE_USE_ACTIVATION_TOKEN",
                assignedRoleId = adminRole.Id
            },
            cancellationToken);

        return new InitialAdminBootstrapResult(
            Created: true,
            StatusCode: "BOOTSTRAP_ADMIN_CREATED",
            AdminUserId: adminUser.Id,
            RequiresActivation: !hasExplicitPassword);
    }
}

