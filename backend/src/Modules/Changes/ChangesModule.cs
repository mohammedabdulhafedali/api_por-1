using ApiGovernance.Modules.Changes.Contracts;
using ApiGovernance.Modules.Changes.Services;
using ApiGovernance.Shared.Contracts;
using ApiGovernance.Shared.Modularity;
using ApiGovernance.Shared.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ApiGovernance.Modules.Changes;

/// <summary>
/// Module 7: Change Management (Sprint S11: W05 affected-file change requests &amp; W06 independent approval workflow).
/// </summary>
public sealed class ChangesModule : IBackendModule
{
    public ModuleDescriptor Descriptor { get; } = new(
        Code: "Changes",
        Name: "Change Requests Management",
        PlannedTasks: "W05, W06");

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IChangeRequestsService, ChangeRequestsService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGroup("/integrations")
            .WithTags("Change Requests (W05-W06)")
            .RequireAuthorization()
            .MapGet("/{integrationId:guid}/change-requests", async (
                Guid integrationId,
                HttpContext httpContext,
                IChangeRequestsService service,
                CancellationToken cancellationToken) =>
            {
                var result = await service.GetIntegrationChangeRequestsAsync(
                    httpContext.User, integrationId, httpContext.TraceIdentifier, cancellationToken);
                return Results.Ok(result);
            })
            .RequireGeneralPermission("changes.view", "CHANGE_REQUESTS_LIST_DENIED", "INTEGRATION")
            .WithName("GetIntegrationChangeRequests")
            .Produces<IntegrationChangeRequestsWorkspaceResponse>(StatusCodes.Status200OK);

        endpoints.MapGroup("/integrations")
            .WithTags("Change Requests (W05-W06)")
            .RequireAuthorization()
            .MapPost("/{integrationId:guid}/change-requests", async (
                Guid integrationId,
                CreateChangeRequestInput request,
                HttpContext httpContext,
                IChangeRequestsService service,
                CancellationToken cancellationToken) =>
            {
                var created = await service.CreateChangeRequestAsync(
                    httpContext.User, integrationId, request, httpContext.TraceIdentifier, cancellationToken);
                return Results.Created($"/api/v1/change-requests/{created.Id}", created);
            })
            .WithCsrfValidation()
            .RequireGeneralPermission("changes.create", "CHANGE_REQUEST_CREATE_DENIED", "INTEGRATION")
            .WithRequestValidation<CreateChangeRequestInput>()
            .WithName("CreateIntegrationChangeRequest")
            .Produces<ChangeRequestDto>(StatusCodes.Status201Created);

        var group = endpoints.MapGroup("/change-requests")
            .WithTags("Change Requests (W05-W06)")
            .RequireAuthorization();

        group.MapGet("/{requestId:guid}", async (
                Guid requestId,
                HttpContext httpContext,
                IChangeRequestsService service,
                CancellationToken cancellationToken) =>
            {
                var result = await service.GetChangeRequestDetailsAsync(
                    httpContext.User, requestId, httpContext.TraceIdentifier, cancellationToken);
                return Results.Ok(result);
            })
            .RequireGeneralPermission("changes.view", "CHANGE_REQUEST_READ_DENIED", "CHANGE_REQUEST")
            .WithName("GetChangeRequestDetails")
            .Produces<ChangeRequestDto>(StatusCodes.Status200OK);

        group.MapPatch("/{requestId:guid}", async (
                Guid requestId,
                UpdateChangeRequestInput request,
                HttpContext httpContext,
                IChangeRequestsService service,
                CancellationToken cancellationToken) =>
            {
                var result = await service.UpdateChangeRequestAsync(
                    httpContext.User, requestId, request, httpContext.TraceIdentifier, cancellationToken);
                return Results.Ok(result);
            })
            .WithCsrfValidation()
            .RequireGeneralPermission("changes.edit", "CHANGE_REQUEST_EDIT_DENIED", "CHANGE_REQUEST")
            .WithRequestValidation<UpdateChangeRequestInput>()
            .WithName("UpdateChangeRequest")
            .Produces<ChangeRequestDto>(StatusCodes.Status200OK);

        group.MapPost("/{requestId:guid}/submit", async (
                Guid requestId,
                HttpContext httpContext,
                IChangeRequestsService service,
                CancellationToken cancellationToken) =>
            {
                var result = await service.SubmitChangeRequestAsync(
                    httpContext.User, requestId, httpContext.TraceIdentifier, cancellationToken);
                return Results.Ok(result);
            })
            .WithCsrfValidation()
            .RequireGeneralPermission("changes.submit", "CHANGE_REQUEST_SUBMIT_DENIED", "CHANGE_REQUEST")
            .WithName("SubmitChangeRequest")
            .Produces<SubmitChangeRequestResponse>(StatusCodes.Status200OK);
    }
}
