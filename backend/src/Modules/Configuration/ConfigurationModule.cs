using ApiGovernance.Modules.Configuration.Contracts;
using ApiGovernance.Modules.Configuration.Services;
using ApiGovernance.Shared.Contracts;
using ApiGovernance.Shared.Modularity;
using ApiGovernance.Shared.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ApiGovernance.Modules.Configuration;

/// <summary>
/// Module 3: Governance Configuration (file types dictionary in M04;
/// requirement templates with historical snapshots in M05;
/// approval routes & ordered role-based stages in M06;
/// change request types & change policies in M07).
/// </summary>
public sealed class ConfigurationModule : IBackendModule
{
    public ModuleDescriptor Descriptor { get; } = new(
        Code: "Configuration",
        Name: "Governance Configuration & Policies",
        PlannedTasks: "M04, M05, M06, M07");

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IFileTypeManagementService, FileTypeManagementService>();
        services.AddScoped<IRequirementTemplateService, RequirementTemplateService>();
        services.AddScoped<IApprovalRouteAndChangeConfigService, ApprovalRouteAndChangeConfigService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        MapFileTypesEndpoints(endpoints);
        MapRequirementTemplatesEndpoints(endpoints);
        MapApprovalRoutesEndpoints(endpoints);
        MapChangeTypesAndPoliciesEndpoints(endpoints);
    }

    private static void MapFileTypesEndpoints(IEndpointRouteBuilder endpoints)
    {
        var fileTypes = endpoints.MapGroup("/file-types")
            .WithTags("Configuration - File Types Dictionary (M04)")
            .RequireAuthorization();

        fileTypes.MapGet("/catalog", (IFileTypeManagementService service) =>
                Results.Ok(service.GetCommonCatalog()))
            .RequireGeneralPermission("file_types.view", "FILE_TYPE_CATALOG_DENIED", "FILE_TYPE")
            .WithName("GetCommonFileTypesCatalog")
            .Produces<IReadOnlyList<FileTypeCatalogEntryDto>>(StatusCodes.Status200OK);

        fileTypes.MapGet("/", async (
                [FromQuery] int? page,
                [FromQuery] int? pageSize,
                [FromQuery] string? search,
                [FromQuery] bool? isActive,
                HttpContext httpContext,
                IFileTypeManagementService service,
                CancellationToken cancellationToken) =>
            {
                var result = await service.GetFileTypesAsync(
                    page, pageSize, search, isActive, httpContext.TraceIdentifier, cancellationToken);
                return Results.Ok(result);
            })
            .RequireGeneralPermission("file_types.view", "FILE_TYPES_LIST_DENIED", "FILE_TYPE")
            .WithName("ListFileTypes")
            .Produces<PagedResult<FileTypeDto>>(StatusCodes.Status200OK);

        fileTypes.MapPost("/", async (
                CreateFileTypeRequest request,
                HttpContext httpContext,
                IFileTypeManagementService service,
                CancellationToken cancellationToken) =>
            {
                var created = await service.CreateFileTypeAsync(
                    httpContext.User, request, httpContext.TraceIdentifier, cancellationToken);
                return Results.Created($"/api/v1/file-types/{created.Id}", created);
            })
            .WithCsrfValidation()
            .RequireGeneralPermission("file_types.manage", "FILE_TYPE_CREATE_DENIED", "FILE_TYPE")
            .WithRequestValidation<CreateFileTypeRequest>()
            .WithName("CreateFileType")
            .Produces<FileTypeDto>(StatusCodes.Status201Created);

        fileTypes.MapPatch("/{fileTypeId:guid}", async (
                Guid fileTypeId,
                UpdateFileTypeRequest request,
                HttpContext httpContext,
                IFileTypeManagementService service,
                CancellationToken cancellationToken) =>
            {
                var updated = await service.UpdateFileTypeAsync(
                    httpContext.User, fileTypeId, request, httpContext.TraceIdentifier, cancellationToken);
                return Results.Ok(updated);
            })
            .WithCsrfValidation()
            .RequireGeneralPermission("file_types.manage", "FILE_TYPE_EDIT_DENIED", "FILE_TYPE")
            .WithRequestValidation<UpdateFileTypeRequest>()
            .WithName("UpdateFileType")
            .Produces<FileTypeDto>(StatusCodes.Status200OK);
    }

    private static void MapRequirementTemplatesEndpoints(IEndpointRouteBuilder endpoints)
    {
        var templates = endpoints.MapGroup("/requirement-templates")
            .WithTags("Configuration - File Requirement Templates (M05)")
            .RequireAuthorization();

        templates.MapGet("/file-types", async (
                IRequirementTemplateService service,
                CancellationToken cancellationToken) =>
            {
                var options = await service.GetActiveFileTypeOptionsAsync(cancellationToken);
                return Results.Ok(options);
            })
            .RequireGeneralPermission("requirements.view", "REQUIREMENT_TEMPLATE_FILE_TYPES_DENIED", "FILE_REQUIREMENT_TEMPLATE")
            .WithName("GetRequirementTemplateFileTypeOptions")
            .Produces<IReadOnlyList<RequirementFileTypeOptionDto>>(StatusCodes.Status200OK);

        templates.MapGet("/", async (
                [FromQuery] int? page,
                [FromQuery] int? pageSize,
                [FromQuery] string? search,
                [FromQuery] bool? isActive,
                HttpContext httpContext,
                IRequirementTemplateService service,
                CancellationToken cancellationToken) =>
            {
                var result = await service.GetRequirementTemplatesAsync(
                    page, pageSize, search, isActive, httpContext.TraceIdentifier, cancellationToken);
                return Results.Ok(result);
            })
            .RequireGeneralPermission("requirements.view", "REQUIREMENT_TEMPLATES_LIST_DENIED", "FILE_REQUIREMENT_TEMPLATE")
            .WithName("ListRequirementTemplates")
            .Produces<PagedResult<FileRequirementTemplateDto>>(StatusCodes.Status200OK);

        templates.MapGet("/{templateId:guid}", async (
                Guid templateId,
                HttpContext httpContext,
                IRequirementTemplateService service,
                CancellationToken cancellationToken) =>
            {
                var result = await service.GetRequirementTemplateAsync(
                    templateId, httpContext.TraceIdentifier, cancellationToken);
                return Results.Ok(result);
            })
            .RequireGeneralPermission("requirements.view", "REQUIREMENT_TEMPLATE_READ_DENIED", "FILE_REQUIREMENT_TEMPLATE")
            .WithName("GetRequirementTemplate")
            .Produces<FileRequirementTemplateDto>(StatusCodes.Status200OK);

        templates.MapGet("/{templateId:guid}/snapshots", async (
                Guid templateId,
                IRequirementTemplateService service,
                CancellationToken cancellationToken) =>
            {
                var snapshots = await service.GetRequirementTemplateSnapshotsAsync(templateId, cancellationToken);
                return Results.Ok(snapshots);
            })
            .RequireGeneralPermission("requirements.view", "REQUIREMENT_TEMPLATE_SNAPSHOTS_DENIED", "FILE_REQUIREMENT_TEMPLATE")
            .WithName("ListRequirementTemplateSnapshots")
            .Produces<IReadOnlyList<RequirementTemplateSnapshotDto>>(StatusCodes.Status200OK);

        templates.MapPost("/", async (
                CreateRequirementTemplateRequest request,
                HttpContext httpContext,
                IRequirementTemplateService service,
                CancellationToken cancellationToken) =>
            {
                var created = await service.CreateRequirementTemplateAsync(
                    httpContext.User, request, httpContext.TraceIdentifier, cancellationToken);
                return Results.Created($"/api/v1/requirement-templates/{created.Id}", created);
            })
            .WithCsrfValidation()
            .RequireGeneralPermission("requirements.manage", "REQUIREMENT_TEMPLATE_CREATE_DENIED", "FILE_REQUIREMENT_TEMPLATE")
            .WithRequestValidation<CreateRequirementTemplateRequest>()
            .WithName("CreateRequirementTemplate")
            .Produces<FileRequirementTemplateDto>(StatusCodes.Status201Created);

        templates.MapPatch("/{templateId:guid}", async (
                Guid templateId,
                UpdateRequirementTemplateRequest request,
                HttpContext httpContext,
                IRequirementTemplateService service,
                CancellationToken cancellationToken) =>
            {
                var updated = await service.UpdateRequirementTemplateAsync(
                    httpContext.User, templateId, request, httpContext.TraceIdentifier, cancellationToken);
                return Results.Ok(updated);
            })
            .WithCsrfValidation()
            .RequireGeneralPermission("requirements.manage", "REQUIREMENT_TEMPLATE_EDIT_DENIED", "FILE_REQUIREMENT_TEMPLATE")
            .WithRequestValidation<UpdateRequirementTemplateRequest>()
            .WithName("UpdateRequirementTemplate")
            .Produces<FileRequirementTemplateDto>(StatusCodes.Status200OK);
    }

    private static void MapApprovalRoutesEndpoints(IEndpointRouteBuilder endpoints)
    {
        var routes = endpoints.MapGroup("/approval-routes")
            .WithTags("Configuration - Approval Routes (M06)")
            .RequireAuthorization();

        routes.MapGet("/roles", async (
                IApprovalRouteAndChangeConfigService service,
                CancellationToken cancellationToken) =>
            {
                var roles = await service.GetAssignableRolesAsync(cancellationToken);
                return Results.Ok(roles);
            })
            .RequireGeneralPermission("routes.view", "APPROVAL_ROUTE_ROLES_DENIED", "APPROVAL_ROUTE")
            .WithName("GetApprovalRouteAssignableRoles")
            .Produces<IReadOnlyList<ApprovalRouteRoleOptionDto>>(StatusCodes.Status200OK);

        routes.MapGet("/", async (
                [FromQuery] int? page,
                [FromQuery] int? pageSize,
                [FromQuery] string? search,
                [FromQuery] bool? isActive,
                HttpContext httpContext,
                IApprovalRouteAndChangeConfigService service,
                CancellationToken cancellationToken) =>
            {
                var result = await service.GetApprovalRoutesAsync(
                    page, pageSize, search, isActive, httpContext.TraceIdentifier, cancellationToken);
                return Results.Ok(result);
            })
            .RequireGeneralPermission("routes.view", "APPROVAL_ROUTES_LIST_DENIED", "APPROVAL_ROUTE")
            .WithName("ListApprovalRoutes")
            .Produces<PagedResult<ApprovalRouteDto>>(StatusCodes.Status200OK);

        routes.MapPost("/", async (
                CreateApprovalRouteRequest request,
                HttpContext httpContext,
                IApprovalRouteAndChangeConfigService service,
                CancellationToken cancellationToken) =>
            {
                var created = await service.CreateApprovalRouteAsync(
                    httpContext.User, request, httpContext.TraceIdentifier, cancellationToken);
                return Results.Created($"/api/v1/approval-routes/{created.Id}", created);
            })
            .WithCsrfValidation()
            .RequireGeneralPermission("routes.manage", "APPROVAL_ROUTE_CREATE_DENIED", "APPROVAL_ROUTE")
            .WithRequestValidation<CreateApprovalRouteRequest>()
            .WithName("CreateApprovalRoute")
            .Produces<ApprovalRouteDto>(StatusCodes.Status201Created);

        routes.MapPatch("/{routeId:guid}", async (
                Guid routeId,
                UpdateApprovalRouteRequest request,
                HttpContext httpContext,
                IApprovalRouteAndChangeConfigService service,
                CancellationToken cancellationToken) =>
            {
                var updated = await service.UpdateApprovalRouteAsync(
                    httpContext.User, routeId, request, httpContext.TraceIdentifier, cancellationToken);
                return Results.Ok(updated);
            })
            .WithCsrfValidation()
            .RequireGeneralPermission("routes.manage", "APPROVAL_ROUTE_EDIT_DENIED", "APPROVAL_ROUTE")
            .WithRequestValidation<UpdateApprovalRouteRequest>()
            .WithName("UpdateApprovalRoute")
            .Produces<ApprovalRouteDto>(StatusCodes.Status200OK);

        routes.MapPut("/{routeId:guid}/stages", async (
                Guid routeId,
                ReplaceApprovalRouteStagesRequest request,
                HttpContext httpContext,
                IApprovalRouteAndChangeConfigService service,
                CancellationToken cancellationToken) =>
            {
                var updated = await service.ReplaceApprovalRouteStagesAsync(
                    httpContext.User, routeId, request, httpContext.TraceIdentifier, cancellationToken);
                return Results.Ok(updated);
            })
            .WithCsrfValidation()
            .RequireGeneralPermission("routes.manage", "APPROVAL_ROUTE_STAGES_DENIED", "APPROVAL_ROUTE")
            .WithRequestValidation<ReplaceApprovalRouteStagesRequest>()
            .WithName("ReplaceApprovalRouteStages")
            .Produces<ApprovalRouteDto>(StatusCodes.Status200OK);
    }

    private static void MapChangeTypesAndPoliciesEndpoints(IEndpointRouteBuilder endpoints)
    {
        var changeTypes = endpoints.MapGroup("/change-types")
            .WithTags("Configuration - Change Request Types (M07)")
            .RequireAuthorization();

        changeTypes.MapGet("/routes", async (
                IApprovalRouteAndChangeConfigService service,
                CancellationToken cancellationToken) =>
            {
                var options = await service.GetAssignableRoutesForChangeTypesAsync(cancellationToken);
                return Results.Ok(options);
            })
            .RequireGeneralPermission("changes.config.view", "CHANGE_TYPE_ROUTES_DENIED", "CHANGE_TYPE")
            .WithName("GetAssignableRoutesForChangeTypes")
            .Produces<IReadOnlyList<ApprovalRouteOptionDto>>(StatusCodes.Status200OK);

        changeTypes.MapGet("/", async (
                [FromQuery] int? page,
                [FromQuery] int? pageSize,
                [FromQuery] string? search,
                [FromQuery] bool? isActive,
                HttpContext httpContext,
                IApprovalRouteAndChangeConfigService service,
                CancellationToken cancellationToken) =>
            {
                var result = await service.GetChangeTypesAsync(
                    page, pageSize, search, isActive, httpContext.TraceIdentifier, cancellationToken);
                return Results.Ok(result);
            })
            .RequireGeneralPermission("changes.config.view", "CHANGE_TYPES_LIST_DENIED", "CHANGE_TYPE")
            .WithName("ListChangeTypes")
            .Produces<PagedResult<ChangeTypeDto>>(StatusCodes.Status200OK);

        changeTypes.MapPost("/", async (
                CreateChangeTypeRequest request,
                HttpContext httpContext,
                IApprovalRouteAndChangeConfigService service,
                CancellationToken cancellationToken) =>
            {
                var created = await service.CreateChangeTypeAsync(
                    httpContext.User, request, httpContext.TraceIdentifier, cancellationToken);
                return Results.Created($"/api/v1/change-types/{created.Id}", created);
            })
            .WithCsrfValidation()
            .RequireGeneralPermission("changes.config.manage", "CHANGE_TYPE_CREATE_DENIED", "CHANGE_TYPE")
            .WithRequestValidation<CreateChangeTypeRequest>()
            .WithName("CreateChangeType")
            .Produces<ChangeTypeDto>(StatusCodes.Status201Created);

        changeTypes.MapPatch("/{typeId:guid}", async (
                Guid typeId,
                UpdateChangeTypeRequest request,
                HttpContext httpContext,
                IApprovalRouteAndChangeConfigService service,
                CancellationToken cancellationToken) =>
            {
                var updated = await service.UpdateChangeTypeAsync(
                    httpContext.User, typeId, request, httpContext.TraceIdentifier, cancellationToken);
                return Results.Ok(updated);
            })
            .WithCsrfValidation()
            .RequireGeneralPermission("changes.config.manage", "CHANGE_TYPE_EDIT_DENIED", "CHANGE_TYPE")
            .WithRequestValidation<UpdateChangeTypeRequest>()
            .WithName("UpdateChangeType")
            .Produces<ChangeTypeDto>(StatusCodes.Status200OK);

        var policies = endpoints.MapGroup("/change-policies")
            .WithTags("Configuration - Change Policies (M07)")
            .RequireAuthorization();

        policies.MapGet("/", async (
                [FromQuery] int? page,
                [FromQuery] int? pageSize,
                [FromQuery] string? search,
                [FromQuery] bool? isActive,
                HttpContext httpContext,
                IApprovalRouteAndChangeConfigService service,
                CancellationToken cancellationToken) =>
            {
                var result = await service.GetChangePoliciesAsync(
                    page, pageSize, search, isActive, httpContext.TraceIdentifier, cancellationToken);
                return Results.Ok(result);
            })
            .RequireGeneralPermission("changes.config.view", "CHANGE_POLICIES_LIST_DENIED", "CHANGE_POLICY")
            .WithName("ListChangePolicies")
            .Produces<PagedResult<ChangePolicyDto>>(StatusCodes.Status200OK);

        policies.MapPost("/", async (
                CreateChangePolicyRequest request,
                HttpContext httpContext,
                IApprovalRouteAndChangeConfigService service,
                CancellationToken cancellationToken) =>
            {
                var created = await service.CreateChangePolicyAsync(
                    httpContext.User, request, httpContext.TraceIdentifier, cancellationToken);
                return Results.Created($"/api/v1/change-policies/{created.Id}", created);
            })
            .WithCsrfValidation()
            .RequireGeneralPermission("changes.config.manage", "CHANGE_POLICY_CREATE_DENIED", "CHANGE_POLICY")
            .WithRequestValidation<CreateChangePolicyRequest>()
            .WithName("CreateChangePolicy")
            .Produces<ChangePolicyDto>(StatusCodes.Status201Created);

        policies.MapPatch("/{policyId:guid}", async (
                Guid policyId,
                UpdateChangePolicyRequest request,
                HttpContext httpContext,
                IApprovalRouteAndChangeConfigService service,
                CancellationToken cancellationToken) =>
            {
                var updated = await service.UpdateChangePolicyAsync(
                    httpContext.User, policyId, request, httpContext.TraceIdentifier, cancellationToken);
                return Results.Ok(updated);
            })
            .WithCsrfValidation()
            .RequireGeneralPermission("changes.config.manage", "CHANGE_POLICY_EDIT_DENIED", "CHANGE_POLICY")
            .WithRequestValidation<UpdateChangePolicyRequest>()
            .WithName("UpdateChangePolicy")
            .Produces<ChangePolicyDto>(StatusCodes.Status200OK);
    }
}
