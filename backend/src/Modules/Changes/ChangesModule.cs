using ApiGovernance.Shared.Modularity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ApiGovernance.Modules.Changes;

/// <summary>
/// Module 7: Change Management (change requests, affected files scope, change approval lifecycle).
/// Implementation deferred to Sprint S11 (W05, W06).
/// </summary>
public sealed class ChangesModule : IBackendModule
{
    public ModuleDescriptor Descriptor { get; } = new(
        Code: "Changes",
        Name: "Change Requests Management",
        PlannedTasks: "W05, W06");

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        // Structural registration only in F03.
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        // Endpoints mapped in W05, W06.
    }
}

