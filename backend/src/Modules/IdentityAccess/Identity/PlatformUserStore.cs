using ApiGovernance.Shared.Persistence;
using ApiGovernance.Shared.Persistence.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ApiGovernance.Modules.IdentityAccess.Identity;

/// <summary>
/// ASP.NET Core Identity store backed directly by the approved UUID 'users' table (UserEntity)
/// so I01 integrates cleanly without creating duplicate AspNetUsers tables (08_DB_and_plan_delta.md).
/// </summary>
public sealed class PlatformUserStore(ApiGovernanceDbContext dbContext) :
    IUserPasswordStore<UserEntity>,
    IUserEmailStore<UserEntity>,
    IUserSecurityStampStore<UserEntity>,
    IUserLockoutStore<UserEntity>
{
    public void Dispose()
    {
        // Lifecycle managed by scoped DI container
    }

    public Task<string> GetUserIdAsync(UserEntity user, CancellationToken cancellationToken)
        => Task.FromResult(user.Id.ToString());

    public Task<string?> GetUserNameAsync(UserEntity user, CancellationToken cancellationToken)
    {
        var name = !string.IsNullOrWhiteSpace(user.Username) ? user.Username : user.Email;
        return Task.FromResult<string?>(name);
    }

    public Task SetUserNameAsync(UserEntity user, string? userName, CancellationToken cancellationToken)
    {
        user.Username = userName ?? string.Empty;
        return Task.CompletedTask;
    }

    public Task<string?> GetNormalizedUserNameAsync(UserEntity user, CancellationToken cancellationToken)
    {
        var normalized = !string.IsNullOrWhiteSpace(user.NormalizedUsername) ? user.NormalizedUsername : user.NormalizedEmail;
        return Task.FromResult<string?>(normalized);
    }

    public Task SetNormalizedUserNameAsync(UserEntity user, string? normalizedName, CancellationToken cancellationToken)
    {
        user.NormalizedUsername = normalizedName ?? string.Empty;
        return Task.CompletedTask;
    }

    public async Task<IdentityResult> CreateAsync(UserEntity user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (user.Id == Guid.Empty)
        {
            user.Id = Guid.NewGuid();
        }
        if (string.IsNullOrWhiteSpace(user.Username))
        {
            user.Username = DeriveDefaultUsername(user.Email, user.Id);
        }
        if (string.IsNullOrWhiteSpace(user.NormalizedUsername))
        {
            user.NormalizedUsername = user.Username.ToUpperInvariant();
        }
        if (string.IsNullOrWhiteSpace(user.NormalizedEmail) && !string.IsNullOrWhiteSpace(user.Email))
        {
            user.NormalizedEmail = user.Email.ToUpperInvariant();
        }
        if (string.IsNullOrWhiteSpace(user.SecurityStamp))
        {
            user.SecurityStamp = Guid.NewGuid().ToString("N");
        }
        if (string.IsNullOrWhiteSpace(user.ConcurrencyStamp))
        {
            user.ConcurrencyStamp = Guid.NewGuid().ToString("N");
        }
        if (user.CreatedAt == default)
        {
            user.CreatedAt = DateTimeOffset.UtcNow;
        }

        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync(cancellationToken);
        return IdentityResult.Success;
    }

    public static string DeriveDefaultUsername(string? email, Guid userId)
    {
        if (!string.IsNullOrWhiteSpace(email))
        {
            var trimmed = email.Trim();
            var atIndex = trimmed.IndexOf('@');
            var localPart = atIndex > 0 ? trimmed[..atIndex] : trimmed;
            if (!string.IsNullOrWhiteSpace(localPart))
            {
                return localPart.Length <= 100 ? localPart : localPart[..100];
            }
        }

        return $"user-{userId:N}"[..24];
    }

    public async Task<IdentityResult> UpdateAsync(UserEntity user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        user.ConcurrencyStamp = Guid.NewGuid().ToString("N");

        var entry = dbContext.Entry(user);
        if (entry.State == EntityState.Detached)
        {
            dbContext.Users.Attach(user);
            entry.State = EntityState.Modified;
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return IdentityResult.Success;
        }
        catch (DbUpdateConcurrencyException)
        {
            return IdentityResult.Failed(new IdentityError
            {
                Code = "ConcurrencyFailure",
                Description = "Optimistic concurrency failure while updating user."
            });
        }
    }

    public Task<IdentityResult> DeleteAsync(UserEntity user, CancellationToken cancellationToken)
    {
        // Account deletion is disallowed by governance policy; deactivation (IsActive = false) is used in I02.
        return Task.FromResult(IdentityResult.Failed(new IdentityError
        {
            Code = "UserDeletionDisallowed",
            Description = "Physical deletion of user accounts is prohibited."
        }));
    }

    public async Task<UserEntity?> FindByIdAsync(string userId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Guid.TryParse(userId, out var parsedId))
        {
            return null;
        }

        return await dbContext.Users.FirstOrDefaultAsync(u => u.Id == parsedId, cancellationToken);
    }

    public async Task<UserEntity?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(normalizedUserName))
        {
            return null;
        }

        return await dbContext.Users.FirstOrDefaultAsync(
            u => u.NormalizedUsername == normalizedUserName || u.NormalizedEmail == normalizedUserName,
            cancellationToken);
    }

    // IUserPasswordStore
    public Task SetPasswordHashAsync(UserEntity user, string? passwordHash, CancellationToken cancellationToken)
    {
        user.PasswordHash = passwordHash;
        return Task.CompletedTask;
    }

    public Task<string?> GetPasswordHashAsync(UserEntity user, CancellationToken cancellationToken)
        => Task.FromResult(user.PasswordHash);

    public Task<bool> HasPasswordAsync(UserEntity user, CancellationToken cancellationToken)
        => Task.FromResult(!string.IsNullOrEmpty(user.PasswordHash));

    // IUserEmailStore
    public Task SetEmailAsync(UserEntity user, string? email, CancellationToken cancellationToken)
    {
        user.Email = email ?? string.Empty;
        return Task.CompletedTask;
    }

    public Task<string?> GetEmailAsync(UserEntity user, CancellationToken cancellationToken)
        => Task.FromResult<string?>(user.Email);

    public Task<bool> GetEmailConfirmedAsync(UserEntity user, CancellationToken cancellationToken)
        => Task.FromResult(user.IsActive && !string.IsNullOrEmpty(user.PasswordHash));

    public Task SetEmailConfirmedAsync(UserEntity user, bool confirmed, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public async Task<UserEntity?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(normalizedEmail))
        {
            return null;
        }

        return await dbContext.Users.FirstOrDefaultAsync(
            u => u.NormalizedEmail == normalizedEmail,
            cancellationToken);
    }

    public Task<string?> GetNormalizedEmailAsync(UserEntity user, CancellationToken cancellationToken)
        => Task.FromResult<string?>(user.NormalizedEmail);

    public Task SetNormalizedEmailAsync(UserEntity user, string? normalizedEmail, CancellationToken cancellationToken)
    {
        user.NormalizedEmail = normalizedEmail ?? string.Empty;
        return Task.CompletedTask;
    }

    // IUserSecurityStampStore
    public Task SetSecurityStampAsync(UserEntity user, string stamp, CancellationToken cancellationToken)
    {
        user.SecurityStamp = stamp;
        return Task.CompletedTask;
    }

    public Task<string?> GetSecurityStampAsync(UserEntity user, CancellationToken cancellationToken)
        => Task.FromResult<string?>(user.SecurityStamp);

    // IUserLockoutStore
    public Task<DateTimeOffset?> GetLockoutEndDateAsync(UserEntity user, CancellationToken cancellationToken)
        => Task.FromResult(user.LockoutEnd);

    public Task SetLockoutEndDateAsync(UserEntity user, DateTimeOffset? lockoutEnd, CancellationToken cancellationToken)
    {
        user.LockoutEnd = lockoutEnd;
        return Task.CompletedTask;
    }

    public Task<int> IncrementAccessFailedCountAsync(UserEntity user, CancellationToken cancellationToken)
    {
        user.AccessFailedCount++;
        return Task.FromResult(user.AccessFailedCount);
    }

    public Task ResetAccessFailedCountAsync(UserEntity user, CancellationToken cancellationToken)
    {
        user.AccessFailedCount = 0;
        return Task.CompletedTask;
    }

    public Task<int> GetAccessFailedCountAsync(UserEntity user, CancellationToken cancellationToken)
        => Task.FromResult(user.AccessFailedCount);

    public Task<bool> GetLockoutEnabledAsync(UserEntity user, CancellationToken cancellationToken)
        => Task.FromResult(user.LockoutEnabled);

    public Task SetLockoutEnabledAsync(UserEntity user, bool enabled, CancellationToken cancellationToken)
    {
        user.LockoutEnabled = enabled;
        return Task.CompletedTask;
    }
}

