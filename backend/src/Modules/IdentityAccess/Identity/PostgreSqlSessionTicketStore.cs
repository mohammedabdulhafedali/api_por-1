using System.Security.Claims;
using System.Security.Cryptography;
using ApiGovernance.Modules.IdentityAccess.Configuration;
using ApiGovernance.Modules.IdentityAccess.Contracts;
using ApiGovernance.Shared.Persistence;
using ApiGovernance.Shared.Persistence.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ApiGovernance.Modules.IdentityAccess.Identity;

public interface ISessionRevocationService
{
    Task RevokeSessionAsync(string ticketKey, CancellationToken cancellationToken = default);

    Task RevokeAllUserSessionsAsync(Guid userId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Server-side revocable Cookie session store (ITicketStore) backed by PostgreSQL 'auth_sessions'.
/// Supports idle + absolute session timeouts and immediate revocation on logout, password change, reset, or account deactivation.
/// </summary>
public sealed class PostgreSqlSessionTicketStore(
    IServiceScopeFactory scopeFactory,
    IOptions<AuthenticationPolicyOptions> policyOptions) : ITicketStore, ISessionRevocationService
{
    private readonly AuthenticationPolicyOptions _policy = policyOptions.Value;

    public async Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        var userIdStr = ticket.Principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdStr, out var userId))
        {
            throw new InvalidOperationException("Authenticated ticket must carry a valid UUID NameIdentifier claim.");
        }

        var securityStamp = ticket.Principal.FindFirstValue(PlatformClaimTypes.SecurityStamp) ?? string.Empty;
        var now = DateTimeOffset.UtcNow;
        var ticketKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var absoluteExpiry = now.AddMinutes(Math.Max(1, _policy.SessionAbsoluteTimeoutMinutes));
        var idleExpiry = ticket.Properties.ExpiresUtc ?? now.AddMinutes(Math.Max(1, _policy.SessionIdleTimeoutMinutes));
        var effectiveExpiry = idleExpiry <= absoluteExpiry ? idleExpiry : absoluteExpiry;

        var serializedTicket = TicketSerializer.Default.Serialize(ticket);

        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApiGovernanceDbContext>();

        dbContext.AuthSessions.Add(new AuthSessionEntity
        {
            Id = ticketKey,
            UserId = userId,
            SecurityStamp = securityStamp,
            TicketBytes = serializedTicket,
            CreatedAt = now,
            LastSeenAt = now,
            ExpiresAt = effectiveExpiry,
            AbsoluteExpiresAt = absoluteExpiry,
            RevokedAt = null
        });

        await dbContext.SaveChangesAsync();
        return ticketKey;
    }

    public async Task RenewAsync(string key, AuthenticationTicket ticket)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApiGovernanceDbContext>();

        var session = await dbContext.AuthSessions
            .FirstOrDefaultAsync(x => x.Id == key);

        var now = DateTimeOffset.UtcNow;
        if (session is null || session.RevokedAt is not null || session.AbsoluteExpiresAt <= now)
        {
            return;
        }

        var desiredExpiry = ticket.Properties.ExpiresUtc ?? now.AddMinutes(Math.Max(1, _policy.SessionIdleTimeoutMinutes));
        var effectiveExpiry = desiredExpiry <= session.AbsoluteExpiresAt ? desiredExpiry : session.AbsoluteExpiresAt;
        ticket.Properties.ExpiresUtc = effectiveExpiry;

        session.ExpiresAt = effectiveExpiry;
        session.LastSeenAt = now;
        session.TicketBytes = TicketSerializer.Default.Serialize(ticket);

        await dbContext.SaveChangesAsync();
    }

    public async Task<AuthenticationTicket?> RetrieveAsync(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApiGovernanceDbContext>();

        var now = DateTimeOffset.UtcNow;
        var session = await dbContext.AuthSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == key);

        if (session is null ||
            session.RevokedAt is not null ||
            session.ExpiresAt <= now ||
            session.AbsoluteExpiresAt <= now)
        {
            return null;
        }

        var user = await dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == session.UserId);

        if (user is null ||
            !user.IsActive ||
            string.IsNullOrEmpty(user.PasswordHash) ||
            !string.Equals(user.SecurityStamp, session.SecurityStamp, StringComparison.Ordinal))
        {
            return null;
        }

        return TicketSerializer.Default.Deserialize(session.TicketBytes);
    }

    public async Task RemoveAsync(string key)
    {
        await RevokeSessionAsync(key);
    }

    public async Task RevokeSessionAsync(string ticketKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ticketKey))
        {
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApiGovernanceDbContext>();

        var now = DateTimeOffset.UtcNow;
        await dbContext.AuthSessions
            .Where(s => s.Id == ticketKey && s.RevokedAt == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(s => s.RevokedAt, now),
                cancellationToken);
    }

    public async Task RevokeAllUserSessionsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApiGovernanceDbContext>();

        var now = DateTimeOffset.UtcNow;
        await dbContext.AuthSessions
            .Where(s => s.UserId == userId && s.RevokedAt == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(s => s.RevokedAt, now),
                cancellationToken);
    }
}

