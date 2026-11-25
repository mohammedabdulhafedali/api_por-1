using ApiGovernance.Modules.PublishingPortal.Contracts;
using ApiGovernance.Modules.PublishingPortal.Services;
using ApiGovernance.Shared.Modularity;
using ApiGovernance.Shared.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ApiGovernance.Modules.PublishingPortal;

/// <summary>
/// Module 9: Publishing &amp; External Consumer Portal (Sprint S12: P01–P04).
/// - P01: Selective file publishing from approved revisions.
/// - P02: Publication withdrawal preserving audit history.
/// - P03: Single active consumer version usage per integration.
/// - P04: External consumer portal strictly scoped by ConsumerEntityId and SHA-256 verified downloads.
/// </summary>
public sealed class PublishingPortalModule : IBackendModule
{
    public ModuleDescriptor Descriptor { get; } = new(
        Code: "PublishingPortal",
        Name: "Publishing & External Consumer Portal",
        PlannedTasks: "P01, P02, P03, P04");

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IPublishingPortalService, PublishingPortalService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        // P01: Publish approved revision with selective files
        endpoints.MapGroup("/revisions")
            .WithTags("Publishing & Withdrawal (P01-P02)")
            .RequireAuthorization()
            .MapPost("/{revisionId:guid}/publications", async (
                Guid revisionId,
                CreatePublicationRequest request,
                HttpContext httpContext,
                IPublishingPortalService service,
                CancellationToken cancellationToken) =>
            {
                var created = await service.PublishRevisionAsync(
                    httpContext.User, revisionId, request, httpContext.TraceIdentifier, cancellationToken);
                return Results.Created($"/api/v1/publications/{created.Id}", created);
            })
            .RequireGeneralPermission(
                PublishingPermissionCodes.PublicationsCreate,
                "REVISION_PUBLISH_DENIED",
                "REVISION")
            .WithName("PublishApprovedRevision")
            .Produces<PublicationDto>(StatusCodes.Status201Created);

        // P02: Withdraw active publication
        endpoints.MapGroup("/publications")
            .WithTags("Publishing & Withdrawal (P01-P02)")
            .RequireAuthorization()
            .MapPost("/{publicationId:guid}/withdraw", async (
                Guid publicationId,
                HttpContext httpContext,
                IPublishingPortalService service,
                CancellationToken cancellationToken) =>
            {
                var updated = await service.WithdrawPublicationAsync(
                    httpContext.User, publicationId, httpContext.TraceIdentifier, cancellationToken);
                return Results.Ok(updated);
            })
            .RequireGeneralPermission(
                PublishingPermissionCodes.PublicationsWithdraw,
                "PUBLICATION_WITHDRAW_DENIED",
                "PUBLICATION")
            .WithName("WithdrawPublication")
            .Produces<PublicationDto>(StatusCodes.Status200OK);

        // P01/P03 Integration Publications & Active Consumer Version
        var integrationGroup = endpoints.MapGroup("/integrations")
            .WithTags("Publishing & Consumer Version Usage (P01-P03)")
            .RequireAuthorization();

        integrationGroup.MapGet("/{integrationId:guid}/publications", async (
                Guid integrationId,
                HttpContext httpContext,
                IPublishingPortalService service,
                CancellationToken cancellationToken) =>
            {
                var result = await service.GetIntegrationPublicationsAsync(
                    httpContext.User, integrationId, httpContext.TraceIdentifier, cancellationToken);
                return Results.Ok(result);
            })
            .RequireGeneralPermission(
                PublishingPermissionCodes.PublicationsView,
                "PUBLICATIONS_LIST_DENIED",
                "INTEGRATION")
            .WithName("GetIntegrationPublications")
            .Produces<IntegrationPublicationsResponse>(StatusCodes.Status200OK);

        integrationGroup.MapGet("/{integrationId:guid}/consumer-version", async (
                Guid integrationId,
                HttpContext httpContext,
                IPublishingPortalService service,
                CancellationToken cancellationToken) =>
            {
                var result = await service.GetConsumerVersionAsync(
                    httpContext.User, integrationId, httpContext.TraceIdentifier, cancellationToken);
                return Results.Ok(result);
            })
            .RequireGeneralPermission(
                PublishingPermissionCodes.VersionsUsageView,
                "CONSUMER_VERSION_VIEW_DENIED",
                "INTEGRATION")
            .WithName("GetIntegrationConsumerVersion")
            .Produces<ConsumerVersionResponse>(StatusCodes.Status200OK);

        integrationGroup.MapPut("/{integrationId:guid}/consumer-version", async (
                Guid integrationId,
                SetConsumerVersionRequest request,
                HttpContext httpContext,
                IPublishingPortalService service,
                CancellationToken cancellationToken) =>
            {
                var result = await service.SetConsumerVersionAsync(
                    httpContext.User, integrationId, request, httpContext.TraceIdentifier, cancellationToken);
                return Results.Ok(result);
            })
            .RequireGeneralPermission(
                PublishingPermissionCodes.VersionsUsageManage,
                "CONSUMER_VERSION_UPDATE_DENIED",
                "INTEGRATION")
            .WithName("SetIntegrationConsumerVersion")
            .Produces<ConsumerVersionResponse>(StatusCodes.Status200OK);

        // P04: External Consumer Portal (Active Session + Strict ConsumerEntityId Scoping)
        var portalGroup = endpoints.MapGroup("/portal")
            .WithTags("External Consumer Portal (P04)")
            .RequireAuthorization();

        portalGroup.MapGet("/integrations", async (
                HttpContext httpContext,
                IPublishingPortalService service,
                CancellationToken cancellationToken) =>
            {
                var result = await service.GetPortalIntegrationsAsync(
                    httpContext.User, httpContext.TraceIdentifier, cancellationToken);
                return Results.Ok(result);
            })
            .RequireActiveSession()
            .WithName("GetConsumerPortalIntegrations")
            .Produces<PortalIntegrationsResponse>(StatusCodes.Status200OK);

        portalGroup.MapGet("/publications/{publicationId:guid}/files", async (
                Guid publicationId,
                HttpContext httpContext,
                IPublishingPortalService service,
                CancellationToken cancellationToken) =>
            {
                var result = await service.GetPortalPublicationFilesAsync(
                    httpContext.User, publicationId, httpContext.TraceIdentifier, cancellationToken);
                return Results.Ok(result);
            })
            .RequireActiveSession()
            .WithName("GetConsumerPortalPublicationFiles")
            .Produces<PortalPublicationFilesResponse>(StatusCodes.Status200OK);

        portalGroup.MapGet("/publications/{publicationId:guid}/files/{fileId:guid}/download", async (
                Guid publicationId,
                Guid fileId,
                HttpContext httpContext,
                IPublishingPortalService service,
                CancellationToken cancellationToken) =>
            {
                var (stream, fileName, contentType, _, sha256) =
                    await service.DownloadPortalPublicationFileAsync(
                        httpContext.User, publicationId, fileId, cancellationToken);

                ApplyDownloadSecurityHeaders(httpContext.Response, sha256);
                return Results.File(stream, contentType, fileDownloadName: fileName, enableRangeProcessing: false);
            })
            .RequireActiveSession()
            .WithName("DownloadConsumerPortalPublicationFile");
    }

    private static void ApplyDownloadSecurityHeaders(HttpResponse response, string sha256)
    {
        response.Headers.XContentTypeOptions = "nosniff";
        response.Headers.CacheControl = "no-store, no-cache";
        response.Headers.ContentSecurityPolicy = "sandbox";
        response.Headers.ETag = $"\"{sha256}\"";
        response.Headers["X-File-Sha256"] = sha256;
    }
}
