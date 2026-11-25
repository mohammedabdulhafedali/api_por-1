using ApiGovernance.Modules.AuditNotifications.Contracts;
using ApiGovernance.Modules.AuditNotifications.Services;
using ApiGovernance.Shared.Contracts;
using ApiGovernance.Shared.Modularity;
using ApiGovernance.Shared.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ApiGovernance.Modules.AuditNotifications;

/// <summary>
/// Module 10: Audit, Notifications, Search &amp; Dashboard (OP-01 append-only audit, W07 notifications, P05 monitoring).
/// </summary>
public sealed class AuditNotificationsModule : IBackendModule
{
    public ModuleDescriptor Descriptor { get; } = new(
        Code: "AuditNotifications",
        Name: "Append-Only Audit, Notifications, Search & Dashboard",
        PlannedTasks: "OP-01 (S02/S03), W07, P05");

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<NotificationCenterService>();
        services.AddScoped<INotificationQueryService>(sp => sp.GetRequiredService<NotificationCenterService>());
        services.AddScoped<IInternalNotificationService>(sp => sp.GetRequiredService<NotificationCenterService>());
        services.AddScoped<IMonitoringAndSearchService, MonitoringAndSearchService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var notifications = endpoints.MapGroup("/notifications")
            .WithTags("Internal Notifications (W07)")
            .RequireAuthorization();

        notifications.MapGet("", async (
                [FromQuery] bool? unreadOnly,
                HttpContext httpContext,
                INotificationQueryService service,
                CancellationToken cancellationToken) =>
            {
                var result = await service.GetMyNotificationsAsync(
                    httpContext.User, unreadOnly == true, httpContext.TraceIdentifier, cancellationToken);
                return Results.Ok(result);
            })
            .RequireActiveSession()
            .WithName("GetMyNotifications")
            .Produces<NotificationsListResponse>(StatusCodes.Status200OK);

        notifications.MapPost("/{notificationId:guid}/read", async (
                Guid notificationId,
                HttpContext httpContext,
                INotificationQueryService service,
                CancellationToken cancellationToken) =>
            {
                var result = await service.MarkAsReadAsync(
                    httpContext.User, notificationId, cancellationToken);
                return Results.Ok(result);
            })
            .WithCsrfValidation()
            .RequireActiveSession()
            .WithName("MarkNotificationAsRead")
            .Produces<NotificationItemDto>(StatusCodes.Status200OK);

        // UI-16 Append-Only Audit Log (Read-Only Query; zero mutation endpoints per OP-01)
        endpoints.MapGet("/audit-events", async (
                [FromQuery] int? page,
                [FromQuery] int? pageSize,
                [FromQuery] string? search,
                [FromQuery] string? operation,
                [FromQuery] string? resourceKind,
                [FromQuery] string? result,
                [FromQuery] DateTimeOffset? fromUtc,
                [FromQuery] DateTimeOffset? toUtc,
                HttpContext httpContext,
                IMonitoringAndSearchService service,
                CancellationToken cancellationToken) =>
            {
                var response = await service.GetAuditEventsAsync(
                    httpContext.User,
                    page ?? 1,
                    pageSize ?? 25,
                    search,
                    operation,
                    resourceKind,
                    result,
                    fromUtc,
                    toUtc,
                    httpContext.TraceIdentifier,
                    cancellationToken);
                return Results.Ok(response);
            })
            .WithTags("Audit, Search & Dashboard (P05)")
            .RequireGeneralPermission("audit.view", "AUDIT_EVENTS_READ_DENIED", "AUDIT_EVENT")
            .WithName("GetAuditEvents")
            .Produces<PagedAuditEventsResponse>(StatusCodes.Status200OK);

        // UI-18 Authorized Global Search (RBAC + Document ACL filtered)
        endpoints.MapGet("/search", async (
                [FromQuery] string? q,
                [FromQuery] string? type,
                [FromQuery] string? state,
                HttpContext httpContext,
                IMonitoringAndSearchService service,
                CancellationToken cancellationToken) =>
            {
                var response = await service.SearchAuthorizedAsync(
                    httpContext.User,
                    q,
                    type,
                    state,
                    httpContext.TraceIdentifier,
                    cancellationToken);
                return Results.Ok(response);
            })
            .WithTags("Audit, Search & Dashboard (P05)")
            .RequireGeneralPermission("search.view", "GLOBAL_SEARCH_DENIED", "SEARCH")
            .WithName("SearchAuthorizedResources")
            .Produces<GlobalSearchResponse>(StatusCodes.Status200OK);

        // UI-17 Permission-Adaptive Dashboard
        endpoints.MapGet("/dashboard", async (
                HttpContext httpContext,
                IMonitoringAndSearchService service,
                CancellationToken cancellationToken) =>
            {
                var response = await service.GetDashboardSummaryAsync(
                    httpContext.User,
                    httpContext.TraceIdentifier,
                    cancellationToken);
                return Results.Ok(response);
            })
            .WithTags("Audit, Search & Dashboard (P05)")
            .RequireGeneralPermission("dashboard.view", "DASHBOARD_READ_DENIED", "DASHBOARD")
            .WithName("GetDashboardSummary")
            .Produces<DashboardSummaryResponse>(StatusCodes.Status200OK);
    }
}
