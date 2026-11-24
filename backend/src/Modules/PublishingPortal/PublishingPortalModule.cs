using ApiGovernance.Shared.Modularity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ApiGovernance.Modules.PublishingPortal;

/// <summary>
/// Module 9: Publishing &amp; Consumer Portal (publications, withdrawal, single active consumer version, scoped downloads).
/// Implementation deferred to Sprint S12 (P01–P04).
/// </summary>
public sealed class PublishingPortalModule : IBackendModule
{
    public ModuleDescriptor Descriptor { get; } = new(
        Code: "PublishingPortal",
        Name: "Publishing & External Consumer Portal",
        PlannedTasks: "P01, P02, P03, P04");

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        // Structural registration only in F03.
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        // Endpoints mapped in P01–P04.
    }
}

