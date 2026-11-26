using ApiGovernance.Modules.MasterData.Contracts;
using ApiGovernance.Modules.MasterData.Services;
using ApiGovernance.Shared.Contracts;
using ApiGovernance.Shared.Modularity;
using ApiGovernance.Shared.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ApiGovernance.Modules.MasterData;

/// <summary>
/// Module 2: Master Data (company systems with computed usage types, internal/external providers,
/// flat consumer categories &amp; consumer entities, and API groups — Sprint S04 M01–M04).
/// </summary>
public sealed class MasterDataModule : IBackendModule
{
    public ModuleDescriptor Descriptor { get; } = new(
        Code: "MasterData",
        Name: "Master Data & Systems Catalog",
        PlannedTasks: "M01, M02, M03, M04");

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IMasterDataService, MasterDataService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        // ====================================================================
        // M01 • Company Systems & Computed Usage Types
        // ====================================================================
        var companySystems = endpoints.MapGroup("/company-systems")
            .WithTags("Master Data - Company Systems (M01)")
            .RequireAuthorization();

        companySystems.MapGet("/", async (
                [FromQuery] int? page,
                [FromQuery] int? pageSize,
                [FromQuery] string? search,
                [FromQuery] bool? isActive,
                [FromQuery] string? usageType,
                HttpContext httpContext,
                IMasterDataService masterDataService,
                CancellationToken cancellationToken) =>
            {
                var result = await masterDataService.GetCompanySystemsAsync(
                    page, pageSize, search, isActive, usageType, httpContext.TraceIdentifier, cancellationToken);
                return Results.Ok(result);
            })
            .RequireGeneralPermission("company_systems.view", "COMPANY_SYSTEMS_LIST_DENIED", "COMPANY_SYSTEM")
            .WithName("ListCompanySystems")
            .Produces<PagedResult<CompanySystemDto>>(StatusCodes.Status200OK);

        companySystems.MapPost("/", async (
                CreateCompanySystemRequest request,
                HttpContext httpContext,
                IMasterDataService masterDataService,
                CancellationToken cancellationToken) =>
            {
                var created = await masterDataService.CreateCompanySystemAsync(
                    httpContext.User, request, httpContext.TraceIdentifier, cancellationToken);
                return Results.Created($"/api/v1/company-systems/{created.Id}", created);
            })
            .WithCsrfValidation()
            .RequireGeneralPermission("company_systems.create", "COMPANY_SYSTEM_CREATE_DENIED", "COMPANY_SYSTEM")
            .WithRequestValidation<CreateCompanySystemRequest>()
            .WithName("CreateCompanySystem")
            .Produces<CompanySystemDto>(StatusCodes.Status201Created);

        companySystems.MapPatch("/{systemId:guid}", async (
                Guid systemId,
                UpdateCompanySystemRequest request,
                HttpContext httpContext,
                IMasterDataService masterDataService,
                CancellationToken cancellationToken) =>
            {
                var updated = await masterDataService.UpdateCompanySystemAsync(
                    httpContext.User, systemId, request, httpContext.TraceIdentifier, cancellationToken);
                return Results.Ok(updated);
            })
            .WithCsrfValidation()
            .RequireGeneralPermission("company_systems.edit", "COMPANY_SYSTEM_EDIT_DENIED", "COMPANY_SYSTEM")
            .WithRequestValidation<UpdateCompanySystemRequest>()
            .WithName("UpdateCompanySystem")
            .Produces<CompanySystemDto>(StatusCodes.Status200OK);

        companySystems.MapDelete("/{systemId:guid}", async (
                Guid systemId,
                HttpContext httpContext,
                IMasterDataService masterDataService,
                CancellationToken cancellationToken) =>
            {
                await masterDataService.DeleteCompanySystemAsync(
                    httpContext.User, systemId, cancellationToken);
                return Results.NoContent();
            })
            .WithCsrfValidation()
            .RequireGeneralPermission("company_systems.delete", "COMPANY_SYSTEM_DELETE_DENIED", "COMPANY_SYSTEM")
            .WithName("DeleteCompanySystem")
            .Produces(StatusCodes.Status204NoContent);

        // ====================================================================
        // M02 • Internal & External Provider Systems
        // ====================================================================
        var internalProviders = endpoints.MapGroup("/internal-providers")
            .WithTags("Master Data - Internal Providers (M02)")
            .RequireAuthorization();

        internalProviders.MapGet("/", async (
                [FromQuery] int? page,
                [FromQuery] int? pageSize,
                [FromQuery] string? search,
                [FromQuery] bool? isActive,
                HttpContext httpContext,
                IMasterDataService masterDataService,
                CancellationToken cancellationToken) =>
            {
                var result = await masterDataService.GetInternalProvidersAsync(
                    page, pageSize, search, isActive, httpContext.TraceIdentifier, cancellationToken);
                return Results.Ok(result);
            })
            .RequireGeneralPermission("providers.view", "INTERNAL_PROVIDERS_LIST_DENIED", "INTERNAL_PROVIDER")
            .WithName("ListInternalProviders")
            .Produces<PagedResult<InternalProviderDto>>(StatusCodes.Status200OK);

        internalProviders.MapPost("/", async (
                CreateInternalProviderRequest request,
                HttpContext httpContext,
                IMasterDataService masterDataService,
                CancellationToken cancellationToken) =>
            {
                var created = await masterDataService.CreateInternalProviderAsync(
                    httpContext.User, request, httpContext.TraceIdentifier, cancellationToken);
                return Results.Created($"/api/v1/internal-providers/{created.Id}", created);
            })
            .WithCsrfValidation()
            .RequireGeneralPermission("providers.create", "INTERNAL_PROVIDER_CREATE_DENIED", "INTERNAL_PROVIDER")
            .WithRequestValidation<CreateInternalProviderRequest>()
            .WithName("CreateInternalProvider")
            .Produces<InternalProviderDto>(StatusCodes.Status201Created);

        internalProviders.MapPatch("/{providerId:guid}", async (
                Guid providerId,
                UpdateInternalProviderRequest request,
                HttpContext httpContext,
                IMasterDataService masterDataService,
                CancellationToken cancellationToken) =>
            {
                var updated = await masterDataService.UpdateInternalProviderAsync(
                    httpContext.User, providerId, request, httpContext.TraceIdentifier, cancellationToken);
                return Results.Ok(updated);
            })
            .WithCsrfValidation()
            .RequireGeneralPermission("providers.edit", "INTERNAL_PROVIDER_EDIT_DENIED", "INTERNAL_PROVIDER")
            .WithRequestValidation<UpdateInternalProviderRequest>()
            .WithName("UpdateInternalProvider")
            .Produces<InternalProviderDto>(StatusCodes.Status200OK);

        internalProviders.MapDelete("/{providerId:guid}", async (
                Guid providerId,
                HttpContext httpContext,
                IMasterDataService masterDataService,
                CancellationToken cancellationToken) =>
            {
                await masterDataService.DeleteInternalProviderAsync(
                    httpContext.User, providerId, cancellationToken);
                return Results.NoContent();
            })
            .WithCsrfValidation()
            .RequireGeneralPermission("providers.delete", "INTERNAL_PROVIDER_DELETE_DENIED", "INTERNAL_PROVIDER")
            .WithName("DeleteInternalProvider")
            .Produces(StatusCodes.Status204NoContent);

        var externalProviders = endpoints.MapGroup("/external-providers")
            .WithTags("Master Data - External Providers (M02)")
            .RequireAuthorization();

        externalProviders.MapGet("/", async (
                [FromQuery] int? page,
                [FromQuery] int? pageSize,
                [FromQuery] string? search,
                [FromQuery] bool? isActive,
                HttpContext httpContext,
                IMasterDataService masterDataService,
                CancellationToken cancellationToken) =>
            {
                var result = await masterDataService.GetExternalProvidersAsync(
                    page, pageSize, search, isActive, httpContext.TraceIdentifier, cancellationToken);
                return Results.Ok(result);
            })
            .RequireGeneralPermission("providers.view", "EXTERNAL_PROVIDERS_LIST_DENIED", "EXTERNAL_PROVIDER")
            .WithName("ListExternalProviders")
            .Produces<PagedResult<ExternalProviderDto>>(StatusCodes.Status200OK);

        externalProviders.MapPost("/", async (
                CreateExternalProviderRequest request,
                HttpContext httpContext,
                IMasterDataService masterDataService,
                CancellationToken cancellationToken) =>
            {
                var created = await masterDataService.CreateExternalProviderAsync(
                    httpContext.User, request, httpContext.TraceIdentifier, cancellationToken);
                return Results.Created($"/api/v1/external-providers/{created.Id}", created);
            })
            .WithCsrfValidation()
            .RequireGeneralPermission("providers.create", "EXTERNAL_PROVIDER_CREATE_DENIED", "EXTERNAL_PROVIDER")
            .WithRequestValidation<CreateExternalProviderRequest>()
            .WithName("CreateExternalProvider")
            .Produces<ExternalProviderDto>(StatusCodes.Status201Created);

        externalProviders.MapPatch("/{providerId:guid}", async (
                Guid providerId,
                UpdateExternalProviderRequest request,
                HttpContext httpContext,
                IMasterDataService masterDataService,
                CancellationToken cancellationToken) =>
            {
                var updated = await masterDataService.UpdateExternalProviderAsync(
                    httpContext.User, providerId, request, httpContext.TraceIdentifier, cancellationToken);
                return Results.Ok(updated);
            })
            .WithCsrfValidation()
            .RequireGeneralPermission("providers.edit", "EXTERNAL_PROVIDER_EDIT_DENIED", "EXTERNAL_PROVIDER")
            .WithRequestValidation<UpdateExternalProviderRequest>()
            .WithName("UpdateExternalProvider")
            .Produces<ExternalProviderDto>(StatusCodes.Status200OK);

        externalProviders.MapDelete("/{providerId:guid}", async (
                Guid providerId,
                HttpContext httpContext,
                IMasterDataService masterDataService,
                CancellationToken cancellationToken) =>
            {
                await masterDataService.DeleteExternalProviderAsync(
                    httpContext.User, providerId, cancellationToken);
                return Results.NoContent();
            })
            .WithCsrfValidation()
            .RequireGeneralPermission("providers.delete", "EXTERNAL_PROVIDER_DELETE_DENIED", "EXTERNAL_PROVIDER")
            .WithName("DeleteExternalProvider")
            .Produces(StatusCodes.Status204NoContent);

        // ====================================================================
        // M03 • Consumer Categories & Consumer Entities
        // ====================================================================
        var consumerCategories = endpoints.MapGroup("/consumer-categories")
            .WithTags("Master Data - Consumer Categories (M03)")
            .RequireAuthorization();

        consumerCategories.MapGet("/", async (
                [FromQuery] int? page,
                [FromQuery] int? pageSize,
                [FromQuery] string? search,
                [FromQuery] bool? isActive,
                HttpContext httpContext,
                IMasterDataService masterDataService,
                CancellationToken cancellationToken) =>
            {
                var result = await masterDataService.GetConsumerCategoriesAsync(
                    page, pageSize, search, isActive, httpContext.TraceIdentifier, cancellationToken);
                return Results.Ok(result);
            })
            .RequireGeneralPermission("consumers.view", "CONSUMER_CATEGORIES_LIST_DENIED", "CONSUMER_CATEGORY")
            .WithName("ListConsumerCategories")
            .Produces<PagedResult<ConsumerCategoryDto>>(StatusCodes.Status200OK);

        consumerCategories.MapPost("/", async (
                CreateConsumerCategoryRequest request,
                HttpContext httpContext,
                IMasterDataService masterDataService,
                CancellationToken cancellationToken) =>
            {
                var created = await masterDataService.CreateConsumerCategoryAsync(
                    httpContext.User, request, httpContext.TraceIdentifier, cancellationToken);
                return Results.Created($"/api/v1/consumer-categories/{created.Id}", created);
            })
            .WithCsrfValidation()
            .RequireGeneralPermission("consumers.create", "CONSUMER_CATEGORY_CREATE_DENIED", "CONSUMER_CATEGORY")
            .WithRequestValidation<CreateConsumerCategoryRequest>()
            .WithName("CreateConsumerCategory")
            .Produces<ConsumerCategoryDto>(StatusCodes.Status201Created);

        consumerCategories.MapPatch("/{categoryId:guid}", async (
                Guid categoryId,
                UpdateConsumerCategoryRequest request,
                HttpContext httpContext,
                IMasterDataService masterDataService,
                CancellationToken cancellationToken) =>
            {
                var updated = await masterDataService.UpdateConsumerCategoryAsync(
                    httpContext.User, categoryId, request, httpContext.TraceIdentifier, cancellationToken);
                return Results.Ok(updated);
            })
            .WithCsrfValidation()
            .RequireGeneralPermission("consumers.edit", "CONSUMER_CATEGORY_EDIT_DENIED", "CONSUMER_CATEGORY")
            .WithRequestValidation<UpdateConsumerCategoryRequest>()
            .WithName("UpdateConsumerCategory")
            .Produces<ConsumerCategoryDto>(StatusCodes.Status200OK);

        consumerCategories.MapDelete("/{categoryId:guid}", async (
                Guid categoryId,
                HttpContext httpContext,
                IMasterDataService masterDataService,
                CancellationToken cancellationToken) =>
            {
                await masterDataService.DeleteConsumerCategoryAsync(
                    httpContext.User, categoryId, cancellationToken);
                return Results.NoContent();
            })
            .WithCsrfValidation()
            .RequireGeneralPermission("consumers.delete", "CONSUMER_CATEGORY_DELETE_DENIED", "CONSUMER_CATEGORY")
            .WithName("DeleteConsumerCategory")
            .Produces(StatusCodes.Status204NoContent);

        var consumers = endpoints.MapGroup("/consumers")
            .WithTags("Master Data - Consumer Entities (M03)")
            .RequireAuthorization();

        consumers.MapGet("/", async (
                [FromQuery] int? page,
                [FromQuery] int? pageSize,
                [FromQuery] string? search,
                [FromQuery] bool? isActive,
                [FromQuery] Guid? categoryId,
                HttpContext httpContext,
                IMasterDataService masterDataService,
                CancellationToken cancellationToken) =>
            {
                var result = await masterDataService.GetConsumersAsync(
                    page, pageSize, search, isActive, categoryId, httpContext.TraceIdentifier, cancellationToken);
                return Results.Ok(result);
            })
            .RequireGeneralPermission("consumers.view", "CONSUMERS_LIST_DENIED", "CONSUMER_ENTITY")
            .WithName("ListConsumers")
            .Produces<PagedResult<ConsumerDto>>(StatusCodes.Status200OK);

        consumers.MapPost("/", async (
                CreateConsumerRequest request,
                HttpContext httpContext,
                IMasterDataService masterDataService,
                CancellationToken cancellationToken) =>
            {
                var created = await masterDataService.CreateConsumerAsync(
                    httpContext.User, request, httpContext.TraceIdentifier, cancellationToken);
                return Results.Created($"/api/v1/consumers/{created.Id}", created);
            })
            .WithCsrfValidation()
            .RequireGeneralPermission("consumers.create", "CONSUMER_CREATE_DENIED", "CONSUMER_ENTITY")
            .WithRequestValidation<CreateConsumerRequest>()
            .WithName("CreateConsumer")
            .Produces<ConsumerDto>(StatusCodes.Status201Created);

        consumers.MapPatch("/{consumerId:guid}", async (
                Guid consumerId,
                UpdateConsumerRequest request,
                HttpContext httpContext,
                IMasterDataService masterDataService,
                CancellationToken cancellationToken) =>
            {
                var updated = await masterDataService.UpdateConsumerAsync(
                    httpContext.User, consumerId, request, httpContext.TraceIdentifier, cancellationToken);
                return Results.Ok(updated);
            })
            .WithCsrfValidation()
            .RequireGeneralPermission("consumers.edit", "CONSUMER_EDIT_DENIED", "CONSUMER_ENTITY")
            .WithRequestValidation<UpdateConsumerRequest>()
            .WithName("UpdateConsumer")
            .Produces<ConsumerDto>(StatusCodes.Status200OK);

        consumers.MapDelete("/{consumerId:guid}", async (
                Guid consumerId,
                HttpContext httpContext,
                IMasterDataService masterDataService,
                CancellationToken cancellationToken) =>
            {
                await masterDataService.DeleteConsumerAsync(
                    httpContext.User, consumerId, cancellationToken);
                return Results.NoContent();
            })
            .WithCsrfValidation()
            .RequireGeneralPermission("consumers.delete", "CONSUMER_DELETE_DENIED", "CONSUMER_ENTITY")
            .WithName("DeleteConsumer")
            .Produces(StatusCodes.Status204NoContent);

        // ====================================================================
        // M04 • API Groups
        // ====================================================================
        var apiGroups = endpoints.MapGroup("/api-groups")
            .WithTags("Master Data - API Groups (M04)")
            .RequireAuthorization();

        apiGroups.MapGet("/", async (
                [FromQuery] int? page,
                [FromQuery] int? pageSize,
                [FromQuery] string? search,
                [FromQuery] bool? isActive,
                HttpContext httpContext,
                IMasterDataService masterDataService,
                CancellationToken cancellationToken) =>
            {
                var result = await masterDataService.GetApiGroupsAsync(
                    page, pageSize, search, isActive, httpContext.TraceIdentifier, cancellationToken);
                return Results.Ok(result);
            })
            .RequireGeneralPermission("api_groups.view", "API_GROUPS_LIST_DENIED", "API_GROUP")
            .WithName("ListApiGroups")
            .Produces<PagedResult<ApiGroupDto>>(StatusCodes.Status200OK);

        apiGroups.MapPost("/", async (
                CreateApiGroupRequest request,
                HttpContext httpContext,
                IMasterDataService masterDataService,
                CancellationToken cancellationToken) =>
            {
                var created = await masterDataService.CreateApiGroupAsync(
                    httpContext.User, request, httpContext.TraceIdentifier, cancellationToken);
                return Results.Created($"/api/v1/api-groups/{created.Id}", created);
            })
            .WithCsrfValidation()
            .RequireGeneralPermission("api_groups.create", "API_GROUP_CREATE_DENIED", "API_GROUP")
            .WithRequestValidation<CreateApiGroupRequest>()
            .WithName("CreateApiGroup")
            .Produces<ApiGroupDto>(StatusCodes.Status201Created);

        apiGroups.MapPatch("/{groupId:guid}", async (
                Guid groupId,
                UpdateApiGroupRequest request,
                HttpContext httpContext,
                IMasterDataService masterDataService,
                CancellationToken cancellationToken) =>
            {
                var updated = await masterDataService.UpdateApiGroupAsync(
                    httpContext.User, groupId, request, httpContext.TraceIdentifier, cancellationToken);
                return Results.Ok(updated);
            })
            .WithCsrfValidation()
            .RequireGeneralPermission("api_groups.edit", "API_GROUP_EDIT_DENIED", "API_GROUP")
            .WithRequestValidation<UpdateApiGroupRequest>()
            .WithName("UpdateApiGroup")
            .Produces<ApiGroupDto>(StatusCodes.Status200OK);

        apiGroups.MapDelete("/{groupId:guid}", async (
                Guid groupId,
                HttpContext httpContext,
                IMasterDataService masterDataService,
                CancellationToken cancellationToken) =>
            {
                await masterDataService.DeleteApiGroupAsync(
                    httpContext.User, groupId, cancellationToken);
                return Results.NoContent();
            })
            .WithCsrfValidation()
            .RequireGeneralPermission("api_groups.delete", "API_GROUP_DELETE_DENIED", "API_GROUP")
            .WithName("DeleteApiGroup")
            .Produces(StatusCodes.Status204NoContent);
    }
}
