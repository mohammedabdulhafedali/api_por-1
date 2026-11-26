using ApiGovernance.Modules.Documents.Contracts;
using ApiGovernance.Modules.Documents.Services;
using ApiGovernance.Shared.Contracts;
using ApiGovernance.Shared.Modularity;
using ApiGovernance.Shared.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ApiGovernance.Modules.Documents;

/// <summary>
/// Module 4: API Documents & Document ACL (Tasks D01, D02, D03, I07, U02).
/// Implements derived title generation, party/config validation, developer assignment separated from ACL grants,
/// conditional deletion protection (1:1 integration link), anti-IDOR 404 masking, and full Document ACL matrix.
/// </summary>
public sealed class DocumentsModule : IBackendModule
{
    public ModuleDescriptor Descriptor { get; } = new(
        Code: "Documents",
        Name: "API Documents & Document ACL",
        PlannedTasks: "D01, D02, D03, I07");

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IDocumentManagementService, DocumentManagementService>();
        services.AddScoped<SubjectDocumentPermissionsService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var subjects = endpoints.MapGroup("/document-permissions/{kind}/{subjectId:guid}")
            .WithTags("Document permissions").RequireAuthorization();
        subjects.MapGet("/", async (string kind, Guid subjectId, int? page, string? search,
                HttpContext context, SubjectDocumentPermissionsService service, CancellationToken ct) =>
            Results.Ok(await service.ReadAsync(context.User, kind, subjectId, page, search, ct)))
            .RequireGeneralPermission("documents.permissions.view", "SUBJECT_DOCUMENT_ACL_READ_DENIED", "API_DOCUMENT");
        subjects.MapPatch("/", async (string kind, Guid subjectId, SubjectDocumentPatch request,
            HttpContext context, SubjectDocumentPermissionsService service, CancellationToken ct) =>
            {
                await service.PatchAsync(context.User, kind, subjectId, request, ct);
                return Results.NoContent();
            }).WithCsrfValidation()
            .RequireGeneralPermission("documents.permissions.manage", "SUBJECT_DOCUMENT_ACL_WRITE_DENIED", "API_DOCUMENT");

        var docs = endpoints.MapGroup("/api-documents")
            .WithTags("API Documents & Access Control (S06)")
            .RequireAuthorization();

        docs.MapGet("/lookups", async (
                HttpContext httpContext,
                IDocumentManagementService service,
                CancellationToken cancellationToken) =>
            {
                var result = await service.GetLookupsAsync(httpContext.TraceIdentifier, cancellationToken);
                return Results.Ok(result);
            })
            .RequireGeneralPermission("api_documents.view", "API_DOCUMENT_LOOKUPS_DENIED", "API_DOCUMENT")
            .WithName("GetApiDocumentLookups")
            .Produces<DocumentLookupsResponse>(StatusCodes.Status200OK);

        docs.MapGet("/form-lookups", async (
                HttpContext httpContext,
                IDocumentManagementService service,
                CancellationToken cancellationToken) =>
            {
                var result = await service.GetLookupsAsync(httpContext.TraceIdentifier, cancellationToken);
                return Results.Ok(result);
            })
            .RequireGeneralPermission("api_documents.create", "API_DOCUMENT_FORM_LOOKUPS_DENIED", "API_DOCUMENT")
            .WithName("GetApiDocumentCreateFormLookups")
            .Produces<DocumentLookupsResponse>(StatusCodes.Status200OK);

        docs.MapGet("/", async (
                [FromQuery] int? page,
                [FromQuery] int? pageSize,
                [FromQuery] string? search,
                [FromQuery] string? state,
                [FromQuery] bool? hasIntegration,
                HttpContext httpContext,
                IDocumentManagementService service,
                CancellationToken cancellationToken) =>
            {
                var result = await service.GetDocumentsAsync(
                    httpContext.User, page, pageSize, search, state, hasIntegration, httpContext.TraceIdentifier, cancellationToken);
                return Results.Ok(result);
            })
            .RequireGeneralPermission("api_documents.view", "API_DOCUMENTS_LIST_DENIED", "API_DOCUMENT")
            .WithName("ListApiDocuments")
            .Produces<PagedResult<ApiDocumentDto>>(StatusCodes.Status200OK);

        docs.MapGet("/{documentId:guid}", async (
                Guid documentId,
                HttpContext httpContext,
                IDocumentManagementService service,
                CancellationToken cancellationToken) =>
            {
                var result = await service.GetDocumentAsync(
                    httpContext.User, documentId, httpContext.TraceIdentifier, cancellationToken);
                return Results.Ok(result);
            })
            .RequireGeneralPermission("api_documents.view", "API_DOCUMENT_READ_DENIED", "API_DOCUMENT")
            .WithName("GetApiDocument")
            .Produces<ApiDocumentDto>(StatusCodes.Status200OK);

        docs.MapPost("/", async (
                CreateApiDocumentRequest request,
                HttpContext httpContext,
                IDocumentManagementService service,
                CancellationToken cancellationToken) =>
            {
                var created = await service.CreateDocumentAsync(
                    httpContext.User, request, httpContext.TraceIdentifier, cancellationToken);
                return Results.Created($"/api/v1/api-documents/{created.Id}", created);
            })
            .WithCsrfValidation()
            .RequireGeneralPermission("api_documents.create", "API_DOCUMENT_CREATE_DENIED", "API_DOCUMENT")
            .WithRequestValidation<CreateApiDocumentRequest>()
            .WithName("CreateApiDocument")
            .Produces<ApiDocumentDto>(StatusCodes.Status201Created);

        docs.MapPatch("/{documentId:guid}", async (
                Guid documentId,
                UpdateApiDocumentRequest request,
                HttpContext httpContext,
                IDocumentManagementService service,
                CancellationToken cancellationToken) =>
            {
                var updated = await service.UpdateDocumentAsync(
                    httpContext.User, documentId, request, httpContext.TraceIdentifier, cancellationToken);
                return Results.Ok(updated);
            })
            .WithCsrfValidation()
            .RequireGeneralPermission("api_documents.edit", "API_DOCUMENT_EDIT_DENIED", "API_DOCUMENT")
            .WithRequestValidation<UpdateApiDocumentRequest>()
            .WithName("UpdateApiDocument")
            .Produces<ApiDocumentDto>(StatusCodes.Status200OK);

        docs.MapDelete("/{documentId:guid}", async (
                Guid documentId,
                HttpContext httpContext,
                IDocumentManagementService service,
                CancellationToken cancellationToken) =>
            {
                await service.DeleteDocumentAsync(httpContext.User, documentId, cancellationToken);
                return Results.NoContent();
            })
            .WithCsrfValidation()
            .RequireGeneralPermission("api_documents.delete", "API_DOCUMENT_DELETE_DENIED", "API_DOCUMENT")
            .WithName("DeleteApiDocument")
            .Produces(StatusCodes.Status204NoContent);

        docs.MapPut("/{documentId:guid}/developers", async (
                Guid documentId,
                ReplaceDocumentDevelopersRequest request,
                HttpContext httpContext,
                IDocumentManagementService service,
                CancellationToken cancellationToken) =>
            {
                var updated = await service.ReplaceDevelopersAsync(
                    httpContext.User, documentId, request, httpContext.TraceIdentifier, cancellationToken);
                return Results.Ok(updated);
            })
            .WithCsrfValidation()
            .RequireGeneralPermission("api_documents.assign_developer", "API_DOCUMENT_DEVELOPERS_DENIED", "API_DOCUMENT")
            .WithRequestValidation<ReplaceDocumentDevelopersRequest>()
            .WithName("ReplaceApiDocumentDevelopers")
            .Produces<ApiDocumentDto>(StatusCodes.Status200OK);

        docs.MapGet("/{documentId:guid}/access", async (
                Guid documentId,
                HttpContext httpContext,
                IDocumentManagementService service,
                CancellationToken cancellationToken) =>
            {
                var matrix = await service.GetDocumentAccessMatrixAsync(
                    httpContext.User, documentId, httpContext.TraceIdentifier, cancellationToken);
                return Results.Ok(matrix);
            })
            .RequireGeneralPermission("documents.permissions.view", "API_DOCUMENT_ACL_VIEW_DENIED", "API_DOCUMENT")
            .WithName("GetApiDocumentAccessMatrix")
            .Produces<DocumentAccessMatrixResponse>(StatusCodes.Status200OK);

        docs.MapPut("/{documentId:guid}/access", async (
                Guid documentId,
                ReplaceDocumentAccessRequest request,
                HttpContext httpContext,
                IDocumentManagementService service,
                CancellationToken cancellationToken) =>
            {
                var matrix = await service.ReplaceDocumentAccessAsync(
                    httpContext.User, documentId, request, httpContext.TraceIdentifier, cancellationToken);
                return Results.Ok(matrix);
            })
            .WithCsrfValidation()
            .RequireGeneralPermission("documents.permissions.manage", "API_DOCUMENT_ACL_MANAGE_DENIED", "API_DOCUMENT")
            .WithRequestValidation<ReplaceDocumentAccessRequest>()
            .WithName("ReplaceApiDocumentAccessMatrix")
            .Produces<DocumentAccessMatrixResponse>(StatusCodes.Status200OK);
    }
}
