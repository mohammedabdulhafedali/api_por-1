using System.Security.Claims;
using System.Text.Json;
using ApiGovernance.Shared.Contracts;
using ApiGovernance.Shared.Persistence;
using ApiGovernance.Shared.Persistence.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ApiGovernance.Shared.Security;

public static class PlatformSecurityClaimTypes
{
    public const string SecurityStamp = "apigov:security_stamp";
}

public sealed record RoleSummaryInfo(Guid Id, string Name);

public sealed record AuthenticatedActorContext(
    UserEntity User,
    IReadOnlyList<RoleSummaryInfo> ActiveRoles,
    IReadOnlyList<string> EffectivePermissions);

public sealed record RequiredPermissionEndpointMetadata(
    string PermissionCode,
    string AuditOperationOnDeny,
    string ResourceKind);

public sealed class RequireAuthenticatedEndpointMetadata;

public sealed record EndpointSecurityAuditIssue(
    string RoutePattern,
    string HttpMethods,
    string Issue);

/// <summary>
/// Central Server Authorization Component (Task I05) shared across all 10 Modular Monolith modules.
/// Resolves active sessions and dynamic effective permissions per ADR-01 (User REVOKE > User GRANT > Active Role Union)
/// and records append-only audit trails per OP-01. Never grants hardcoded privileges by role name.
/// </summary>
public interface IServerAuthorizationService
{
    Task<UserEntity> RequireActiveAuthenticatedUserAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default);

    Task<AuthenticatedActorContext> RequireGeneralPermissionAsync(
        ClaimsPrincipal principal,
        string requiredPermissionCode,
        string auditOperationOnDeny,
        Guid? targetResourceId = null,
        string resourceKind = "RESOURCE",
        CancellationToken cancellationToken = default);

    Task<bool> HasGeneralPermissionAsync(
        ClaimsPrincipal principal,
        string requiredPermissionCode,
        CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<RoleSummaryInfo> Roles, IReadOnlyList<string> Permissions)>
        ResolveRolesAndEffectivePermissionsAsync(
            Guid userId,
            CancellationToken cancellationToken = default);

    Task<Dictionary<Guid, (IReadOnlyList<RoleSummaryInfo> Roles, IReadOnlyList<string> Permissions)>>
        ResolveRolesAndEffectivePermissionsBatchAsync(
            IReadOnlyCollection<Guid> userIds,
            CancellationToken cancellationToken = default);

    Task WriteAuditEventAsync(
        Guid? actorUserId,
        string operation,
        string resourceKind,
        Guid? resourceId,
        string result,
        object summary,
        CancellationToken cancellationToken = default);
}

public sealed class ServerAuthorizationService(ApiGovernanceDbContext dbContext) : IServerAuthorizationService
{
    public async Task<UserEntity> RequireActiveAuthenticatedUserAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        var userIdStr = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var claimStamp = principal.FindFirstValue(PlatformSecurityClaimTypes.SecurityStamp);

        if (!Guid.TryParse(userIdStr, out var userId))
        {
            throw ApiContractException.Unauthorized();
        }

        var now = DateTimeOffset.UtcNow;
        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null ||
            !user.IsActive ||
            string.IsNullOrEmpty(user.PasswordHash) ||
            (user.LockoutEnd is not null && user.LockoutEnd > now) ||
            !string.Equals(user.SecurityStamp, claimStamp, StringComparison.Ordinal))
        {
            throw ApiContractException.Unauthorized();
        }

        return user;
    }

    public async Task<AuthenticatedActorContext> RequireGeneralPermissionAsync(
        ClaimsPrincipal principal,
        string requiredPermissionCode,
        string auditOperationOnDeny,
        Guid? targetResourceId = null,
        string resourceKind = "RESOURCE",
        CancellationToken cancellationToken = default)
    {
        var actor = await RequireActiveAuthenticatedUserAsync(principal, cancellationToken);
        var (roles, permissions) = await ResolveRolesAndEffectivePermissionsAsync(actor.Id, cancellationToken);

        if (!permissions.Contains(requiredPermissionCode, StringComparer.OrdinalIgnoreCase))
        {
            await WriteAuditEventAsync(
                actorUserId: actor.Id,
                operation: auditOperationOnDeny,
                resourceKind: resourceKind,
                resourceId: targetResourceId,
                result: "FORBIDDEN",
                summary: new { requiredPermission = requiredPermissionCode },
                cancellationToken);

            throw ApiContractException.Forbidden(
                messageAr: $"لا تملك الصلاحية المطلوبة ({requiredPermissionCode}) لتنفيذ هذا الإجراء.",
                messageEn: $"Required permission '{requiredPermissionCode}' is missing.");
        }

        return new AuthenticatedActorContext(actor, roles, permissions);
    }

    public async Task<bool> HasGeneralPermissionAsync(
        ClaimsPrincipal principal,
        string requiredPermissionCode,
        CancellationToken cancellationToken = default)
    {
        var actor = await RequireActiveAuthenticatedUserAsync(principal, cancellationToken);
        var (_, permissions) = await ResolveRolesAndEffectivePermissionsAsync(actor.Id, cancellationToken);
        return permissions.Contains(requiredPermissionCode, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<(IReadOnlyList<RoleSummaryInfo> Roles, IReadOnlyList<string> Permissions)>
        ResolveRolesAndEffectivePermissionsAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
    {
        var rolesByUser = await ResolveRolesAndEffectivePermissionsBatchAsync([userId], cancellationToken);
        return rolesByUser[userId];
    }

    public async Task<Dictionary<Guid, (IReadOnlyList<RoleSummaryInfo> Roles, IReadOnlyList<string> Permissions)>>
        ResolveRolesAndEffectivePermissionsBatchAsync(
            IReadOnlyCollection<Guid> userIds,
            CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<Guid, (IReadOnlyList<RoleSummaryInfo>, IReadOnlyList<string>)>();
        if (userIds.Count == 0)
        {
            return result;
        }

        var userRoleRows = await (
            from ur in dbContext.UserRoles.AsNoTracking()
            join r in dbContext.Roles.AsNoTracking() on ur.RoleId equals r.Id
            where userIds.Contains(ur.UserId) && r.IsActive
            orderby r.Name
            select new { ur.UserId, r.Id, r.Name })
            .ToListAsync(cancellationToken);

        var activeRoleIds = userRoleRows.Select(x => x.Id).Distinct().ToList();

        var rolePermRows = activeRoleIds.Count == 0
            ? []
            : await (
                from rp in dbContext.RolePermissions.AsNoTracking()
                join p in dbContext.Permissions.AsNoTracking() on rp.PermissionId equals p.Id
                where activeRoleIds.Contains(rp.RoleId)
                select new { rp.RoleId, p.Code })
                .ToListAsync(cancellationToken);

        var rolePermLookup = rolePermRows
            .GroupBy(x => x.RoleId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Code).ToList());

        var overrideRows = await (
            from uo in dbContext.UserPermissionOverrides.AsNoTracking()
            join p in dbContext.Permissions.AsNoTracking() on uo.PermissionId equals p.Id
            where userIds.Contains(uo.UserId)
            select new { uo.UserId, p.Code, uo.Decision })
            .ToListAsync(cancellationToken);

        foreach (var uid in userIds)
        {
            var roles = userRoleRows
                .Where(x => x.UserId == uid)
                .Select(x => new RoleSummaryInfo(x.Id, x.Name))
                .ToList();

            var effectiveSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var role in roles)
            {
                if (rolePermLookup.TryGetValue(role.Id, out var codes))
                {
                    foreach (var code in codes)
                    {
                        effectiveSet.Add(code);
                    }
                }
            }

            // Precedence per ADR-01 Section A: User REVOKE > User GRANT > Union of active roles
            var userOverrides = overrideRows.Where(x => x.UserId == uid);
            foreach (var ov in userOverrides.Where(o => string.Equals(o.Decision, "GRANT", StringComparison.OrdinalIgnoreCase)))
            {
                effectiveSet.Add(ov.Code);
            }
            foreach (var ov in userOverrides.Where(o => string.Equals(o.Decision, "REVOKE", StringComparison.OrdinalIgnoreCase)))
            {
                effectiveSet.Remove(ov.Code);
            }

            result[uid] = (roles, effectiveSet.OrderBy(c => c, StringComparer.Ordinal).ToList());
        }

        return result;
    }

    public async Task WriteAuditEventAsync(
        Guid? actorUserId,
        string operation,
        string resourceKind,
        Guid? resourceId,
        string result,
        object summary,
        CancellationToken cancellationToken = default)
    {
        dbContext.AuditEvents.Add(new AuditEventEntity
        {
            Id = Guid.NewGuid(),
            ActorUserId = actorUserId,
            Operation = operation,
            ResourceKind = resourceKind,
            ResourceId = resourceId,
            Result = result,
            ChangeSummaryJson = JsonSerializer.Serialize(summary),
            RecordedAt = DateTimeOffset.UtcNow
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}

public static class ServerAuthorizationEndpointExtensions
{
    public static RouteHandlerBuilder RequireGeneralPermission(
        this RouteHandlerBuilder builder,
        string permissionCode,
        string? auditOperationOnDeny = null,
        string resourceKind = "RESOURCE")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permissionCode);

        var denyOperation = string.IsNullOrWhiteSpace(auditOperationOnDeny)
            ? $"ENDPOINT_ACCESS_DENIED:{permissionCode.ToUpperInvariant()}"
            : auditOperationOnDeny;

        return builder
            .RequireAuthorization()
            .WithMetadata(new RequiredPermissionEndpointMetadata(permissionCode, denyOperation, resourceKind))
            .AddEndpointFilter(new RequireGeneralPermissionEndpointFilter(permissionCode, denyOperation, resourceKind));
    }

    public static RouteHandlerBuilder RequireActiveSession(this RouteHandlerBuilder builder)
    {
        return builder
            .RequireAuthorization()
            .WithMetadata(new RequireAuthenticatedEndpointMetadata())
            .AddEndpointFilter(new RequireActiveSessionEndpointFilter());
    }

    private sealed class RequireGeneralPermissionEndpointFilter(
        string permissionCode,
        string auditOperationOnDeny,
        string resourceKind) : IEndpointFilter
    {
        public async ValueTask<object?> InvokeAsync(
            EndpointFilterInvocationContext context,
            EndpointFilterDelegate next)
        {
            var httpContext = context.HttpContext;
            var authService = httpContext.RequestServices
                .GetRequiredService<IServerAuthorizationService>();

            var targetResourceId = ResolveTargetGuidFromRoute(httpContext.Request.RouteValues);

            await authService.RequireGeneralPermissionAsync(
                httpContext.User,
                permissionCode,
                auditOperationOnDeny,
                targetResourceId,
                resourceKind,
                httpContext.RequestAborted);

            return await next(context);
        }

        private static Guid? ResolveTargetGuidFromRoute(RouteValueDictionary routeValues)
        {
            foreach (var value in routeValues.Values)
            {
                if (value is Guid guidVal)
                {
                    return guidVal;
                }

                if (value is string strVal && Guid.TryParse(strVal, out var parsed))
                {
                    return parsed;
                }
            }

            return null;
        }
    }

    private sealed class RequireActiveSessionEndpointFilter : IEndpointFilter
    {
        public async ValueTask<object?> InvokeAsync(
            EndpointFilterInvocationContext context,
            EndpointFilterDelegate next)
        {
            var httpContext = context.HttpContext;
            var authService = httpContext.RequestServices
                .GetRequiredService<IServerAuthorizationService>();

            await authService.RequireActiveAuthenticatedUserAsync(
                httpContext.User,
                httpContext.RequestAborted);

            return await next(context);
        }
    }
}

/// <summary>
/// Audits every versioned /api/v1/* endpoint to prove zero unprotected routes exist
/// without explicit AllowAnonymous or central authorization metadata (Task I05 gate).
/// </summary>
public static class EndpointAuthorizationCatalogVerifier
{
    public static IReadOnlyList<EndpointSecurityAuditIssue> AuditVersionedApiEndpoints(
        EndpointDataSource endpointDataSource,
        string apiBasePath = "/api/v1")
    {
        var issues = new List<EndpointSecurityAuditIssue>();

        foreach (var endpoint in endpointDataSource.Endpoints.OfType<RouteEndpoint>())
        {
            var rawPattern = endpoint.RoutePattern.RawText ?? string.Empty;
            var normalizedPattern = rawPattern.StartsWith('/') ? rawPattern : "/" + rawPattern;
            if (!normalizedPattern.StartsWith(apiBasePath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var httpMethods = string.Join(
                ",",
                endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["ANY"]);

            var allowsAnonymous = endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null;
            if (allowsAnonymous)
            {
                continue;
            }

            var hasAuthorize = endpoint.Metadata.GetMetadata<IAuthorizeData>() is not null;
            var hasPermission = endpoint.Metadata.GetMetadata<RequiredPermissionEndpointMetadata>() is not null;
            var hasActiveSession = endpoint.Metadata.GetMetadata<RequireAuthenticatedEndpointMetadata>() is not null;

            if (!hasAuthorize)
            {
                issues.Add(new EndpointSecurityAuditIssue(
                    normalizedPattern,
                    httpMethods,
                    "Missing RequireAuthorization or AllowAnonymous metadata."));
            }
            else if (!hasPermission && !hasActiveSession)
            {
                issues.Add(new EndpointSecurityAuditIssue(
                    normalizedPattern,
                    httpMethods,
                    "Protected /api/v1 endpoint must declare RequireGeneralPermission(...) or RequireActiveSession()."));
            }
        }

        return issues;
    }
}

