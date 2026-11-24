using System.Security.Claims;
using ApiGovernance.Modules.IdentityAccess.Contracts;
using ApiGovernance.Shared.Persistence.Entities;
using ApiGovernance.Shared.Security;

namespace ApiGovernance.Modules.IdentityAccess.Services;

/// <summary>
/// IdentityAccess adapter over the central <see cref="IServerAuthorizationService"/> (Task I05)
/// preserving module DTO signatures without duplicating ADR-01 or OP-01 logic.
/// </summary>
public sealed class IdentityAccessAuthorizationHelper(IServerAuthorizationService serverAuthorization)
{
    public Task<UserEntity> RequireActiveAuthenticatedUserAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
        => serverAuthorization.RequireActiveAuthenticatedUserAsync(principal, cancellationToken);

    public async Task<UserEntity> RequireGeneralPermissionAsync(
        ClaimsPrincipal principal,
        string requiredPermissionCode,
        string auditOperationOnDeny,
        Guid? targetResourceId = null,
        string resourceKind = "USER",
        CancellationToken cancellationToken = default)
    {
        var context = await serverAuthorization.RequireGeneralPermissionAsync(
            principal,
            requiredPermissionCode,
            auditOperationOnDeny,
            targetResourceId,
            resourceKind,
            cancellationToken);
        return context.User;
    }

    public async Task<(IReadOnlyList<RoleSummaryDto> Roles, IReadOnlyList<string> Permissions)>
        ResolveRolesAndEffectivePermissionsAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
    {
        var (roles, perms) = await serverAuthorization.ResolveRolesAndEffectivePermissionsAsync(userId, cancellationToken);
        return (roles.Select(r => new RoleSummaryDto(r.Id, r.Name)).ToList(), perms);
    }

    public async Task<Dictionary<Guid, (IReadOnlyList<RoleSummaryDto> Roles, IReadOnlyList<string> Permissions)>>
        ResolveRolesAndEffectivePermissionsBatchAsync(
            IReadOnlyCollection<Guid> userIds,
            CancellationToken cancellationToken = default)
    {
        var batch = await serverAuthorization.ResolveRolesAndEffectivePermissionsBatchAsync(userIds, cancellationToken);
        return batch.ToDictionary(
            kv => kv.Key,
            kv => ((IReadOnlyList<RoleSummaryDto>)kv.Value.Roles.Select(r => new RoleSummaryDto(r.Id, r.Name)).ToList(), kv.Value.Permissions));
    }

    public Task WriteAuditEventAsync(
        Guid? actorUserId,
        string operation,
        string resourceKind,
        Guid? resourceId,
        string result,
        object summary,
        CancellationToken cancellationToken = default)
        => serverAuthorization.WriteAuditEventAsync(
            actorUserId,
            operation,
            resourceKind,
            resourceId,
            result,
            summary,
            cancellationToken);
}
