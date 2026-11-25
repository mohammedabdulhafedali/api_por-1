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
    }
}
