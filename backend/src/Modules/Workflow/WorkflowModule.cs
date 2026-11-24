using ApiGovernance.Shared.Modularity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ApiGovernance.Modules.Workflow;

/// <summary>
/// Module 8: Workflow Engine (immutable stage snapshots, role-based single-member atomic decisions, transitions).
/// Implementation deferred to Sprint S10 (W01–W04).
/// </summary>
public sealed class WorkflowModule : IBackendModule
{
    public ModuleDescriptor Descriptor { get; } = new(
        Code: "Workflow",
        Name: "Sequential Approval Workflow Engine",
        PlannedTasks: "W01, W02, W03, W04");

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        // Structural registration only in F03.
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        // Endpoints mapped in W01–W04.
    }
}

