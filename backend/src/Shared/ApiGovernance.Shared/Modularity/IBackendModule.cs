using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ApiGovernance.Shared.Modularity;

/// <summary>
/// Descriptor for an isolated domain module in the API Governance Modular Monolith.
/// </summary>
public sealed record ModuleDescriptor(
    string Code,
    string Name,
    string PlannedTasks);

/// <summary>
/// Contract implemented by each bounded module inside the Modular Monolith.
/// In F03, modules register their structural boundary only (no business logic, DB, or auth yet).
/// </summary>
public interface IBackendModule
{
    ModuleDescriptor Descriptor { get; }

    void RegisterServices(IServiceCollection services, IConfiguration configuration);

    void MapEndpoints(IEndpointRouteBuilder endpoints);
}

