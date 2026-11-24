using ApiGovernance.Modules.Workflow.Contracts;
using ApiGovernance.Modules.Workflow.Services;
using ApiGovernance.Shared.Contracts;
using ApiGovernance.Shared.Modularity;
using ApiGovernance.Shared.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ApiGovernance.Modules.Workflow;

/// <summary>
/// Module 8: Sequential Approval Workflow Engine (Sprint S10: W01–W04).
/// Enforces immutable stage snapshots at submission time, role-based single-member atomic decisions,
/// ordered stage transitions, and return-for-rework / rejection trail with mandatory notes.
/// </summary>
public sealed class WorkflowModule : IBackendModule
{
    public ModuleDescriptor Descriptor { get; } = new(
        Code: "Workflow",
        Name: "Sequential Approval Workflow Engine",
        PlannedTasks: "W01, W02, W03, W04");

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<WorkflowEngineService>();
        services.AddScoped<IWorkflowEngineService>(sp => sp.GetRequiredService<WorkflowEngineService>());
        services.AddScoped<IWorkflowRunStarter>(sp => sp.GetRequiredService<WorkflowEngineService>());
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/workflows")
            .WithTags("Approval Workflows (W01-W04)")
            .RequireAuthorization();

        group.MapGet("/inbox", async (
                HttpContext httpContext,
                IWorkflowEngineService service,
                CancellationToken cancellationToken) =>
            {
                var result = await service.GetInboxAsync(
                    httpContext.User, httpContext.TraceIdentifier, cancellationToken);
                return Results.Ok(result);
            })
            .RequireGeneralPermission("workflows.view", "WORKFLOW_INBOX_DENIED", "WORKFLOW_RUN")
            .WithName("GetWorkflowApprovalInbox")
            .Produces<WorkflowInboxResponse>(StatusCodes.Status200OK);

        group.MapGet("/{runId:guid}", async (
                Guid runId,
                HttpContext httpContext,
                IWorkflowEngineService service,
                CancellationToken cancellationToken) =>
            {
                var result = await service.GetWorkflowRunDetailsAsync(
                    httpContext.User, runId, httpContext.TraceIdentifier, cancellationToken);
                return Results.Ok(result);
            })
            .RequireGeneralPermission("workflows.view", "WORKFLOW_RUN_READ_DENIED", "WORKFLOW_RUN")
            .WithName("GetWorkflowRunDetails")
            .Produces<WorkflowRunDetailsDto>(StatusCodes.Status200OK);

        group.MapGet("/{runId:guid}/history", async (
                Guid runId,
                HttpContext httpContext,
                IWorkflowEngineService service,
                CancellationToken cancellationToken) =>
            {
                var result = await service.GetWorkflowHistoryAsync(
                    httpContext.User, runId, httpContext.TraceIdentifier, cancellationToken);
                return Results.Ok(result);
            })
            .RequireGeneralPermission("workflows.view", "WORKFLOW_HISTORY_DENIED", "WORKFLOW_RUN")
            .WithName("GetWorkflowHistory")
            .Produces<WorkflowHistoryResponse>(StatusCodes.Status200OK);

        group.MapPost("/{runId:guid}/stages/{stageId:guid}/decisions", async (
                Guid runId,
                Guid stageId,
                SubmitStageDecisionRequest request,
                HttpContext httpContext,
                IWorkflowEngineService service,
                CancellationToken cancellationToken) =>
            {
                var result = await service.SubmitStageDecisionAsync(
                    httpContext.User, runId, stageId, request, httpContext.TraceIdentifier, cancellationToken);
                return Results.Ok(result);
            })
            .WithCsrfValidation()
            .RequireGeneralPermission("workflows.decide", "WORKFLOW_DECISION_DENIED", "WORKFLOW_STAGE")
            .WithRequestValidation<SubmitStageDecisionRequest>()
            .WithName("SubmitWorkflowStageDecision")
            .Produces<SubmitStageDecisionResponse>(StatusCodes.Status200OK);
    }
}
