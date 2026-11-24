using System.Security.Claims;
using ApiGovernance.Modules.IdentityAccess.Configuration;
using ApiGovernance.Modules.IdentityAccess.Contracts;
using ApiGovernance.Modules.IdentityAccess.Identity;
using ApiGovernance.Shared.Contracts;
using ApiGovernance.Shared.Persistence.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ApiGovernance.Modules.IdentityAccess.Services;

/// <summary>
/// Local ASP.NET Core Identity authentication service backed by PostgreSQL 'users',
/// enforcing password complexity, lockout, revocable sessions, OP-01 audit events,
/// and ADR-01 permission evaluation without creating duplicate Identity tables.
/// </summary>
public sealed class LocalIdentityAuthService(
    ApiGovernance.Shared.Persistence.ApiGovernanceDbContext dbContext,
    UserManager<UserEntity> userManager,
    ISessionRevocationService sessionRevocationService,
    IAuthTokenDeliveryChannel tokenDeliveryChannel,
    IdentityAccessAuthorizationHelper authHelper,
    IOptions<AuthenticationPolicyOptions> policyOptions) : IIdentityAuthService
{
    private readonly AuthenticationPolicyOptions _policy = policyOptions.Value;

    public string ProviderName => _policy.Provider;

    public async Task<LoginResult> LoginAsync(
        LoginRequest request,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmail = userManager.NormalizeEmail(request.Email.Trim());
        var user = string.IsNullOrWhiteSpace(normalizedEmail)
            ? null
            : await userManager.FindByEmailAsync(normalizedEmail);

        if (user is null || !user.IsActive || string.IsNullOrEmpty(user.PasswordHash))
        {
            await authHelper.WriteAuditEventAsync(
                actorUserId: user?.Id,
                operation: "AUTH_LOGIN_FAILED",
                resourceKind: "USER",
                resourceId: user?.Id,
                result: "FAILURE",
                summary: new { reason = "INVALID_OR_UNACTIVATED_ACCOUNT", provider = ProviderName },
                cancellationToken);

            throw CreateUnifiedLoginUnauthorized();
        }

        if (await userManager.IsLockedOutAsync(user))
        {
            await authHelper.WriteAuditEventAsync(
                actorUserId: user.Id,
                operation: "AUTH_LOGIN_FAILED",
                resourceKind: "USER",
                resourceId: user.Id,
                result: "LOCKED",
                summary: new { reason = "ACCOUNT_LOCKED", provider = ProviderName },
                cancellationToken);

            throw ApiContractException.TooManyRequests(
                messageAr: "تم قفل الحساب مؤقتًا لتجاوز الحد المسموح من محاولات الدخول الفاشلة. يرجى المحاولة لاحقًا.",
                messageEn: "This account is temporarily locked due to too many failed login attempts. Please try again later.");
        }

        var passwordValid = await userManager.CheckPasswordAsync(user, request.Password);
        if (!passwordValid)
        {
            await userManager.AccessFailedAsync(user);
            var lockedNow = await userManager.IsLockedOutAsync(user);

            await authHelper.WriteAuditEventAsync(
                actorUserId: user.Id,
                operation: "AUTH_LOGIN_FAILED",
                resourceKind: "USER",
                resourceId: user.Id,
                result: lockedNow ? "LOCKED" : "FAILURE",
                summary: new { reason = lockedNow ? "LOCKOUT_TRIGGERED" : "INVALID_CREDENTIALS", provider = ProviderName },
                cancellationToken);

            if (lockedNow)
            {
                throw ApiContractException.TooManyRequests(
                    messageAr: "تم قفل الحساب مؤقتًا لتجاوز الحد المسموح من محاولات الدخول الفاشلة. يرجى المحاولة لاحقًا.",
                    messageEn: "This account is temporarily locked due to too many failed login attempts. Please try again later.");
            }

            throw CreateUnifiedLoginUnauthorized();
        }

        if (user.AccessFailedCount > 0 || user.LockoutEnd is not null)
        {
            await userManager.ResetAccessFailedCountAsync(user);
            await userManager.SetLockoutEndDateAsync(user, null);
        }

        var (roles, permissions) = await authHelper.ResolveRolesAndEffectivePermissionsAsync(user.Id, cancellationToken);
        var principal = BuildPrincipal(user);

        await authHelper.WriteAuditEventAsync(
            actorUserId: user.Id,
            operation: "AUTH_LOGIN_SUCCEEDED",
            resourceKind: "USER",
            resourceId: user.Id,
            result: "SUCCESS",
            summary: new { provider = ProviderName, roleCount = roles.Count },
            cancellationToken);

        var summaryDto = new AuthenticatedUserResponse(
            Id: user.Id,
            FullName: user.FullName,
            Email: user.Email,
            IsActive: user.IsActive,
            ConsumerEntityId: user.ConsumerEntityId,
            AuthProvider: ProviderName,
            Roles: roles,
            Permissions: permissions,
            TraceId: traceId);

        return new LoginResult(summaryDto, principal);
    }

    public async Task LogoutAsync(
        Guid? userId,
        string? ticketKey,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(ticketKey))
        {
            await sessionRevocationService.RevokeSessionAsync(ticketKey, cancellationToken);
        }

        if (userId is not null)
        {
            await authHelper.WriteAuditEventAsync(
                actorUserId: userId,
                operation: "AUTH_LOGOUT",
                resourceKind: "SESSION",
                resourceId: userId,
                result: "SUCCESS",
                summary: new { provider = ProviderName },
                cancellationToken);
        }
    }

    public async Task<AuthenticatedUserResponse> GetCurrentIdentityAsync(
        ClaimsPrincipal principal,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var user = await authHelper.RequireActiveAuthenticatedUserAsync(principal, cancellationToken);
        var (roles, permissions) = await authHelper.ResolveRolesAndEffectivePermissionsAsync(user.Id, cancellationToken);

        return new AuthenticatedUserResponse(
            Id: user.Id,
            FullName: user.FullName,
            Email: user.Email,
            IsActive: user.IsActive,
            ConsumerEntityId: user.ConsumerEntityId,
            AuthProvider: ProviderName,
            Roles: roles,
            Permissions: permissions,
            TraceId: traceId);
    }

    public async Task ChangePasswordAsync(
        ClaimsPrincipal principal,
        ChangePasswordRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await authHelper.RequireActiveAuthenticatedUserAsync(principal, cancellationToken);

        var changeResult = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!changeResult.Succeeded)
        {
            var isWrongCurrentPassword = changeResult.Errors.Any(e =>
                string.Equals(e.Code, "PasswordMismatch", StringComparison.Ordinal));

            await authHelper.WriteAuditEventAsync(
                actorUserId: user.Id,
                operation: "AUTH_PASSWORD_CHANGED",
                resourceKind: "USER",
                resourceId: user.Id,
                result: "FAILURE",
                summary: new { reason = isWrongCurrentPassword ? "CURRENT_PASSWORD_MISMATCH" : "PASSWORD_POLICY_REJECTED" },
                cancellationToken);

            if (isWrongCurrentPassword)
            {
                throw ApiContractException.BadRequest(
                    messageAr: "كلمة المرور الحالية غير صحيحة.",
                    messageEn: "The current password is incorrect.",
                    details: new { currentPassword = new[] { "كلمة المرور الحالية غير صحيحة / Current password is incorrect." } });
            }

            throw CreatePasswordPolicyValidationException(changeResult.Errors);
        }

        await sessionRevocationService.RevokeAllUserSessionsAsync(user.Id, cancellationToken);

        await authHelper.WriteAuditEventAsync(
            actorUserId: user.Id,
            operation: "AUTH_PASSWORD_CHANGED",
            resourceKind: "USER",
            resourceId: user.Id,
            result: "SUCCESS",
            summary: new { sessionsRevoked = true },
            cancellationToken);
    }

    public async Task InitiatePasswordResetAsync(
        ClaimsPrincipal actorPrincipal,
        Guid targetUserId,
        CancellationToken cancellationToken = default)
    {
        var actor = await authHelper.RequireGeneralPermissionAsync(
            actorPrincipal,
            IdentityPermissionCodes.UsersPasswordReset,
            "AUTH_PASSWORD_RESET_INITIATED",
            targetResourceId: targetUserId,
            cancellationToken: cancellationToken);

        var targetUser = await dbContext.Users.FirstOrDefaultAsync(u => u.Id == targetUserId, cancellationToken);
        if (targetUser is null)
        {
            throw ApiContractException.NotFound(
                messageAr: "الحساب المطلوب غير موجود.",
                messageEn: "Target user account was not found.");
        }

        if (!targetUser.IsActive)
        {
            throw ApiContractException.UnprocessableEntity(
                messageAr: "لا يمكن بدء التفعيل أو إعادة التعيين لحساب معطل.",
                messageEn: "Cannot initiate activation or password reset for a deactivated user account.");
        }

        var isInitialActivation = string.IsNullOrEmpty(targetUser.PasswordHash);
        var purpose = isInitialActivation ? "ACCOUNT_ACTIVATION" : "PASSWORD_RESET";
        var token = await userManager.GeneratePasswordResetTokenAsync(targetUser);
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(Math.Max(1, _policy.ResetTokenLifespanMinutes));

        await tokenDeliveryChannel.DeliverPasswordResetTokenAsync(
            new AuthTokenDeliveryContext(
                TargetUserId: targetUser.Id,
                TargetEmail: targetUser.Email,
                TargetFullName: targetUser.FullName,
                Purpose: purpose,
                SingleUseToken: token,
                ExpiresAtUtc: expiresAt,
                InitiatedByUserId: actor.Id),
            cancellationToken);

        await authHelper.WriteAuditEventAsync(
            actorUserId: actor.Id,
            operation: "AUTH_PASSWORD_RESET_INITIATED",
            resourceKind: "USER",
            resourceId: targetUser.Id,
            result: "SUCCESS",
            summary: new { purpose, deliveryChannel = tokenDeliveryChannel.ChannelCode },
            cancellationToken);
    }

    public async Task CompletePasswordResetAsync(
        CompletePasswordResetRequest request,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmail = userManager.NormalizeEmail(request.Email.Trim());
        var user = string.IsNullOrWhiteSpace(normalizedEmail)
            ? null
            : await userManager.FindByEmailAsync(normalizedEmail);

        if (user is null || !user.IsActive)
        {
            await authHelper.WriteAuditEventAsync(
                actorUserId: user?.Id,
                operation: "AUTH_PASSWORD_RESET_COMPLETED",
                resourceKind: "USER",
                resourceId: user?.Id,
                result: "FAILURE",
                summary: new { reason = "INVALID_TOKEN_OR_INACTIVE_ACCOUNT" },
                cancellationToken);

            throw CreateGenericResetFailureException();
        }

        var wasInitialActivation = string.IsNullOrEmpty(user.PasswordHash);
        var resetResult = await userManager.ResetPasswordAsync(user, request.Token, request.NewPassword);

        if (!resetResult.Succeeded)
        {
            var onlyPasswordPolicyErrors = resetResult.Errors.All(e =>
                e.Code.StartsWith("Password", StringComparison.Ordinal));

            await authHelper.WriteAuditEventAsync(
                actorUserId: user.Id,
                operation: "AUTH_PASSWORD_RESET_COMPLETED",
                resourceKind: "USER",
                resourceId: user.Id,
                result: "FAILURE",
                summary: new { reason = onlyPasswordPolicyErrors ? "PASSWORD_POLICY_REJECTED" : "INVALID_OR_EXPIRED_TOKEN" },
                cancellationToken);

            if (onlyPasswordPolicyErrors)
            {
                throw CreatePasswordPolicyValidationException(resetResult.Errors);
            }

            throw CreateGenericResetFailureException();
        }

        if (user.AccessFailedCount > 0 || user.LockoutEnd is not null)
        {
            await userManager.ResetAccessFailedCountAsync(user);
            await userManager.SetLockoutEndDateAsync(user, null);
        }

        await sessionRevocationService.RevokeAllUserSessionsAsync(user.Id, cancellationToken);

        await authHelper.WriteAuditEventAsync(
            actorUserId: user.Id,
            operation: wasInitialActivation ? "AUTH_ACCOUNT_ACTIVATED" : "AUTH_PASSWORD_RESET_COMPLETED",
            resourceKind: "USER",
            resourceId: user.Id,
            result: "SUCCESS",
            summary: new { sessionsRevoked = true },
            cancellationToken);
    }

    private ClaimsPrincipal BuildPrincipal(UserEntity user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Name, user.FullName),
            new(PlatformClaimTypes.FullName, user.FullName),
            new(PlatformClaimTypes.SecurityStamp, user.SecurityStamp),
            new(PlatformClaimTypes.AuthProvider, ProviderName)
        };

        if (user.ConsumerEntityId is not null)
        {
            claims.Add(new Claim(PlatformClaimTypes.ConsumerEntityId, user.ConsumerEntityId.Value.ToString()));
        }

        var identity = new ClaimsIdentity(claims, IdentityConstants.ApplicationScheme);
        return new ClaimsPrincipal(identity);
    }

    private static ApiContractException CreateUnifiedLoginUnauthorized() =>
        ApiContractException.Unauthorized(
            messageAr: "البريد الإلكتروني أو كلمة المرور غير صحيحة أو الحساب غير مفعل.",
            messageEn: "Invalid email or password, or account is not active.");

    private static ApiContractException CreateGenericResetFailureException() =>
        ApiContractException.BadRequest(
            messageAr: "تعذر إتمام تفعيل الحساب أو إعادة تعيين كلمة المرور. يرجى التحقق من صحة الرمز وصلاحيته.",
            messageEn: "Unable to complete account activation or password reset. Please verify the token is valid and unexpired.");

    private static ApiContractException CreatePasswordPolicyValidationException(IEnumerable<IdentityError> errors)
    {
        var messages = errors
            .Select(e => e.Description)
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Distinct()
            .ToArray();

        if (messages.Length == 0)
        {
            messages = ["كلمة المرور لا تستوفي سياسة التعقيد المعتمدة / Password does not meet complexity policy."];
        }

        return ApiContractException.ValidationFailed(
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["newPassword"] = messages
            },
            messageAr: "كلمة المرور الجديدة لا تستوفي سياسة الأمان المعتمدة (15 حرفًا على الأقل مع أحرف كبيرة وصغيرة وأرقام ورموز).",
            messageEn: "New password does not meet the configured complexity policy (min 15 chars with upper, lower, digit, and symbol).");
    }
}
