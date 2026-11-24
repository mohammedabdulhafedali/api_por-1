using System.Security.Claims;
using ApiGovernance.Modules.IdentityAccess.Contracts;

namespace ApiGovernance.Modules.IdentityAccess.Services;

public sealed record LoginResult(
    AuthenticatedUserResponse User,
    ClaimsPrincipal Principal);

/// <summary>
/// Swappable identity authentication provider contract (Sprint S02 / Task I01).
/// Allows future corporate identity providers to plug in alongside LocalIdentity without rewriting API contracts.
/// </summary>
public interface IIdentityAuthService
{
    string ProviderName { get; }

    Task<LoginResult> LoginAsync(
        LoginRequest request,
        string traceId,
        CancellationToken cancellationToken = default);

    Task LogoutAsync(
        Guid? userId,
        string? ticketKey,
        CancellationToken cancellationToken = default);

    Task<AuthenticatedUserResponse> GetCurrentIdentityAsync(
        ClaimsPrincipal principal,
        string traceId,
        CancellationToken cancellationToken = default);

    Task ChangePasswordAsync(
        ClaimsPrincipal principal,
        ChangePasswordRequest request,
        CancellationToken cancellationToken = default);

    Task InitiatePasswordResetAsync(
        ClaimsPrincipal actorPrincipal,
        Guid targetUserId,
        CancellationToken cancellationToken = default);

    Task CompletePasswordResetAsync(
        CompletePasswordResetRequest request,
        CancellationToken cancellationToken = default);
}

