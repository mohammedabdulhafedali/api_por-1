using ApiGovernance.Modules.Integrations.Contracts;
using ApiGovernance.Modules.Integrations.Services;
using ApiGovernance.Shared.Contracts;
using ApiGovernance.Shared.Modularity;
using ApiGovernance.Shared.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ApiGovernance.Modules.Integrations;

/// <summary>
/// Module 5: Integrations (D04: 1:1 creation, D05: developer scope &amp; assignment, D06: list &amp; multi-tab details).
/// </summary>
public sealed class IntegrationsModule : IBackendModule
{
    public ModuleDescriptor Descriptor { get; } = new(
        Code: "Integrations",
        Name: "Integrations Management",
        PlannedTasks: "D04, D05, D06");

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IIntegrationManagementService, IntegrationManagementService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var integrations = endpoints.MapGroup("/integrations")
            .WithTags("Integrations Management (D04-D06)")
            .RequireAuthorization();

        integrations.MapGet("/eligible-documents", async (
                HttpContext httpContext,
                IIntegrationManagementService service,
                CancellationToken cancellationToken) =>
            {
                var result = await service.GetEligibleDocumentsAsync(httpContext.User, cancellationToken);
                return Results.Ok(result);
            })
            .RequireGeneralPermission("integrations.create", "INTEGRATION_ELIGIBLE_DOCS_DENIED", "INTEGRATION")
            .WithName("ListEligibleIntegrationDocuments")
            .Produces<IReadOnlyList<EligibleIntegrationDocumentDto>>(StatusCodes.Status200OK);

        integrations.MapGet("/assignable-users", async (
                IIntegrationManagementService service,
                CancellationToken cancellationToken) =>
            {
                var result = await service.GetAssignableUsersAsync(cancellationToken);
                return Results.Ok(result);
            })
            .RequireGeneralPermission("integrations.view", "INTEGRATION_USERS_LOOKUP_DENIED", "INTEGRATION")
            .WithName("ListIntegrationAssignableUsers")
            .Produces<IReadOnlyList<IntegrationPartyOptionDto>>(StatusCodes.Status200OK);

        integrations.MapGet("/", async (
                [FromQuery] int? page,
                [FromQuery] int? pageSize,
                [FromQuery] string? search,
                [FromQuery] bool? assignedToMe,
                [FromQuery] string? state,
                HttpContext httpContext,
                IIntegrationManagementService service,
                CancellationToken cancellationToken) =>
            {
                var result = await service.GetIntegrationsAsync(
                    httpContext.User, page, pageSize, search, assignedToMe, state, httpContext.TraceIdentifier, cancellationToken);
                return Results.Ok(result);
            })
            .RequireGeneralPermission("integrations.view", "INTEGRATIONS_LIST_DENIED", "INTEGRATION")
            .WithName("ListIntegrations")
            .Produces<PagedResult<IntegrationDto>>(StatusCodes.Status200OK);

        integrations.MapGet("/{integrationId:guid}", async (
                Guid integrationId,
                HttpContext httpContext,
                IIntegrationManagementService service,
                CancellationToken cancellationToken) =>
            {
                var result = await service.GetIntegrationDetailsAsync(
                    httpContext.User, integrationId, httpContext.TraceIdentifier, cancellationToken);
                return Results.Ok(result);
            })
            .RequireGeneralPermission("integrations.view", "INTEGRATION_READ_DENIED", "INTEGRATION")
            .WithName("GetIntegrationDetails")
            .Produces<IntegrationDetailsDto>(StatusCodes.Status200OK);

        integrations.MapGet("/{integrationId:guid}/developers", async (
                Guid integrationId,
                HttpContext httpContext,
                IIntegrationManagementService service,
                CancellationToken cancellationToken) =>
            {
                var result = await service.GetIntegrationDevelopersAsync(
                    httpContext.User, integrationId, httpContext.TraceIdentifier, cancellationToken);
                return Results.Ok(result);
            })
            .RequireGeneralPermission("integrations.view", "INTEGRATION_DEVELOPERS_READ_DENIED", "INTEGRATION")
            .WithName("GetIntegrationDevelopers")
            .Produces<IntegrationDevelopersResponse>(StatusCodes.Status200OK);

        integrations.MapPut("/{integrationId:guid}/developers", async (
                Guid integrationId,
                ReplaceIntegrationDevelopersRequest request,
                HttpContext httpContext,
                IIntegrationManagementService service,
                CancellationToken cancellationToken) =>
            {
                var updated = await service.ReplaceIntegrationDevelopersAsync(
                    httpContext.User, integrationId, request, httpContext.TraceIdentifier, cancellationToken);
                return Results.Ok(updated);
            })
            .WithCsrfValidation()
            .RequireGeneralPermission("integrations.assign_developer", "INTEGRATION_DEVELOPERS_DENIED", "INTEGRATION")
            .WithRequestValidation<ReplaceIntegrationDevelopersRequest>()
            .WithName("ReplaceIntegrationDevelopers")
            .Produces<IntegrationDevelopersResponse>(StatusCodes.Status200OK);

        integrations.MapPost("/", async (
                CreateIntegrationRequest request,
                HttpContext httpContext,
                IIntegrationManagementService service,
                CancellationToken cancellationToken) =>
            {
                var created = await service.CreateIntegrationAsync(
                    httpContext.User, request, httpContext.TraceIdentifier, cancellationToken);
                return Results.Created($"/api/v1/integrations/{created.Id}", created);
            })
            .WithCsrfValidation()
            .RequireGeneralPermission("integrations.create", "INTEGRATION_CREATE_DENIED", "INTEGRATION")
            .WithRequestValidation<CreateIntegrationRequest>()
            .WithName("CreateIntegration")
            .Produces<IntegrationDto>(StatusCodes.Status201Created);
    }
}
