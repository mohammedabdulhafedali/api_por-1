using ApiGovernance.Shared.Modularity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ApiGovernance.Modules.AuditNotifications;

/// <summary>
/// Module 10: Audit, Notifications, Search &amp; Dashboard (append-only audit events from S02/S03 per OP-01, notifications W07, UI P05).
/// </summary>
public sealed class AuditNotificationsModule : IBackendModule
{
    public ModuleDescriptor Descriptor { get; } = new(
        Code: "AuditNotifications",
        Name: "Append-Only Audit, Notifications, Search & Dashboard",
        PlannedTasks: "OP-01 (S02/S03), W07, P05");

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        // Structural registration only in F03.
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        // Endpoints mapped in W07, P05.
    }
}

