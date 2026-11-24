using ApiGovernance.Modules.VersionsFiles.Configuration;
using ApiGovernance.Modules.VersionsFiles.Contracts;
using ApiGovernance.Modules.VersionsFiles.Scanning;
using ApiGovernance.Modules.VersionsFiles.Services;
using ApiGovernance.Modules.VersionsFiles.Storage;
using ApiGovernance.Modules.VersionsFiles.Validation;
using ApiGovernance.Shared.Contracts;
using ApiGovernance.Shared.Modularity;
using ApiGovernance.Shared.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ApiGovernance.Modules.VersionsFiles;

/// <summary>
/// Module 6: Versions &amp; Files (Sprint S08 Tasks V01–V04: Private immutable file store,
/// upload validation &amp; quarantine, fail-closed security scanning, manual free-text API versions).
/// </summary>
public sealed class VersionsFilesModule : IBackendModule
{
    public ModuleDescriptor Descriptor { get; } = new(
        Code: "VersionsFiles",
        Name: "API Versions, Revisions & Immutable Files",
        PlannedTasks: "V01, V02, V03, V04, V05, V06, V07, V08");

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<FileStorageOptions>(configuration.GetSection(FileStorageOptions.SectionName));
        services.Configure<FileScanningOptions>(configuration.GetSection(FileScanningOptions.SectionName));

        services.AddSingleton<IPrivateFileStore, LocalPrivateFileStore>();
        services.AddSingleton<IFileSecurityScanner, ConfiguredFileSecurityScanner>();
        services.AddSingleton<FileUploadContentValidator>();
        services.AddScoped<IVersionsFilesService, VersionsFilesService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        // 1. Integration API Versions (V04)
        var integrationGroup = endpoints.MapGroup("/integrations")
            .WithTags("API Versions & Files Workspace (V01-V04)")
            .RequireAuthorization();

        integrationGroup.MapGet("/{integrationId:guid}/versions", async (
                Guid integrationId,
                HttpContext httpContext,
                IVersionsFilesService service,
                CancellationToken cancellationToken) =>
            {
                var result = await service.GetIntegrationVersionsAsync(
                    httpContext.User, integrationId, httpContext.TraceIdentifier, cancellationToken);
                return Results.Ok(result);
            })
            .RequireGeneralPermission("versions.view", "API_VERSIONS_LIST_DENIED", "INTEGRATION")
            .WithName("ListIntegrationApiVersions")
            .Produces<IntegrationVersionsResponse>(StatusCodes.Status200OK);

        integrationGroup.MapPost("/{integrationId:guid}/versions", async (
                Guid integrationId,
                CreateApiVersionRequest request,
                HttpContext httpContext,
                IVersionsFilesService service,
                CancellationToken cancellationToken) =>
            {
                var created = await service.CreateApiVersionAsync(
                    httpContext.User, integrationId, request, httpContext.TraceIdentifier, cancellationToken);
                return Results.Created($"/api/v1/integrations/{integrationId}/versions/{created.Id}", created);
            })
            .WithCsrfValidation()
            .RequireGeneralPermission("versions.create", "API_VERSION_CREATE_DENIED", "INTEGRATION")
            .WithRequestValidation<CreateApiVersionRequest>()
            .WithName("CreateIntegrationApiVersion")
            .Produces<ApiVersionDto>(StatusCodes.Status201Created);

        integrationGroup.MapGet("/{integrationId:guid}/files-workspace", async (
                Guid integrationId,
                [FromQuery] Guid? versionId,
                [FromQuery] Guid? revisionId,
                HttpContext httpContext,
                IVersionsFilesService service,
                CancellationToken cancellationToken) =>
            {
                var result = await service.GetFilesWorkspaceAsync(
                    httpContext.User, integrationId, versionId, revisionId, httpContext.TraceIdentifier, cancellationToken);
                return Results.Ok(result);
            })
            .RequireGeneralPermission("files.view", "FILES_WORKSPACE_DENIED", "INTEGRATION")
            .WithName("GetIntegrationFilesWorkspace")
            .Produces<IntegrationFilesWorkspaceResponse>(StatusCodes.Status200OK);

        // 2. Initial Draft Package Revision under an API Version (for UI-10 upload staging)
        var versionGroup = endpoints.MapGroup("/versions")
            .WithTags("Document Revisions (V04-V05)")
            .RequireAuthorization();

        versionGroup.MapPost("/{versionId:guid}/revisions", async (
                Guid versionId,
                HttpContext httpContext,
                IVersionsFilesService service,
                CancellationToken cancellationToken) =>
            {
                var created = await service.CreateInitialDraftRevisionAsync(
                    httpContext.User, versionId, httpContext.TraceIdentifier, cancellationToken);
                return Results.Created($"/api/v1/revisions/{created.Id}", created);
            })
            .WithCsrfValidation()
            .RequireGeneralPermission("revisions.create", "REVISION_CREATE_DENIED", "DOCUMENT_REVISION")
            .WithName("CreateDraftDocumentRevision")
            .Produces<DocumentRevisionDto>(StatusCodes.Status201Created);

        // 3. Revision Files: List, Validated Upload + Quarantine, Re-scan, Delete Draft, Guarded Download (V01-V03)
        var revisionFilesGroup = endpoints.MapGroup("/revisions")
            .WithTags("Revision Files & Security Scanning (V01-V03)")
            .RequireAuthorization();

        revisionFilesGroup.MapGet("/{revisionId:guid}/files", async (
                Guid revisionId,
                HttpContext httpContext,
                IVersionsFilesService service,
                CancellationToken cancellationToken) =>
            {
                var files = await service.GetRevisionFilesAsync(
                    httpContext.User, revisionId, httpContext.TraceIdentifier, cancellationToken);
                return Results.Ok(files);
            })
            .RequireGeneralPermission("files.view", "REVISION_FILES_LIST_DENIED", "DOCUMENT_REVISION")
            .WithName("ListRevisionFiles")
            .Produces<IReadOnlyList<RevisionFileDto>>(StatusCodes.Status200OK);

        revisionFilesGroup.MapPost("/{revisionId:guid}/files", async (
                Guid revisionId,
                HttpContext httpContext,
                IVersionsFilesService service,
                CancellationToken cancellationToken) =>
            {
                var bodySizeFeature = httpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
                if (bodySizeFeature is { IsReadOnly: false })
                {
                    bodySizeFeature.MaxRequestBodySize = 110L * 1024 * 1024;
                }

                if (!httpContext.Request.HasFormContentType)
                {
                    throw ApiContractException.BadRequest(
                        messageAr: "يجب إرسال الملف باستخدام صيغة multipart/form-data.",
                        messageEn: "File upload requires multipart/form-data content type.");
                }

                var form = await httpContext.Request.ReadFormAsync(cancellationToken);
                if (!Guid.TryParse(form["fileTypeId"].FirstOrDefault(), out var fileTypeId) || fileTypeId == Guid.Empty)
                {
                    throw ApiContractException.BadRequest(
                        messageAr: "معرف نوع الملف (fileTypeId) مطلوب وصحيح.",
                        messageEn: "A valid 'fileTypeId' form field is required.");
                }

                var uploadedFile = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
                if (uploadedFile is null)
                {
                    throw ApiContractException.BadRequest(
                        messageAr: "يرجى إرفاق ملف للرفع في حقل file.",
                        messageEn: "An uploaded file part named 'file' is required.");
                }

                var (statusCode, dto) = await service.UploadRevisionFileAsync(
                    httpContext.User, revisionId, fileTypeId, uploadedFile, httpContext.TraceIdentifier, cancellationToken);

                return statusCode == StatusCodes.Status202Accepted
                    ? Results.Accepted($"/api/v1/files/{dto.Id}/scan-status", dto)
                    : Results.Created($"/api/v1/revisions/{revisionId}/files/{dto.Id}", dto);
            })
            .WithCsrfValidation()
            .RequireGeneralPermission("files.upload", "REVISION_FILE_UPLOAD_DENIED", "DOCUMENT_REVISION")
            .WithName("UploadRevisionFile")
            .Produces<RevisionFileDto>(StatusCodes.Status201Created)
            .Produces<RevisionFileDto>(StatusCodes.Status202Accepted);

        revisionFilesGroup.MapPost("/{revisionId:guid}/files/{fileId:guid}/rescan", async (
                Guid revisionId,
                Guid fileId,
                HttpContext httpContext,
                IVersionsFilesService service,
                CancellationToken cancellationToken) =>
            {
                var (statusCode, dto) = await service.RescanRevisionFileAsync(
                    httpContext.User, revisionId, fileId, httpContext.TraceIdentifier, cancellationToken);

                return statusCode == StatusCodes.Status202Accepted
                    ? Results.Accepted($"/api/v1/files/{dto.Id}/scan-status", dto)
                    : Results.Ok(dto);
            })
            .WithCsrfValidation()
            .RequireGeneralPermission("files.upload", "REVISION_FILE_RESCAN_DENIED", "REVISION_FILE")
            .WithName("RescanRevisionFile")
            .Produces<RevisionFileDto>(StatusCodes.Status200OK)
            .Produces<RevisionFileDto>(StatusCodes.Status202Accepted);

        revisionFilesGroup.MapDelete("/{revisionId:guid}/files/{fileId:guid}", async (
                Guid revisionId,
                Guid fileId,
                HttpContext httpContext,
                IVersionsFilesService service,
                CancellationToken cancellationToken) =>
            {
                await service.DeleteDraftRevisionFileAsync(httpContext.User, revisionId, fileId, cancellationToken);
                return Results.NoContent();
            })
            .WithCsrfValidation()
            .RequireGeneralPermission("files.delete", "REVISION_FILE_DELETE_DENIED", "REVISION_FILE")
            .WithName("DeleteDraftRevisionFile")
            .Produces(StatusCodes.Status204NoContent);

        revisionFilesGroup.MapGet("/{revisionId:guid}/files/{fileId:guid}/download", async (
                Guid revisionId,
                Guid fileId,
                HttpContext httpContext,
                IVersionsFilesService service,
                CancellationToken cancellationToken) =>
            {
                var (stream, fileName, contentType, _, _) = await service.DownloadCleanRevisionFileAsync(
                    httpContext.User, revisionId, fileId, cancellationToken);

                httpContext.Response.Headers.XContentTypeOptions = "nosniff";
                httpContext.Response.Headers.CacheControl = "no-store, no-cache";
                return Results.File(stream, contentType, fileDownloadName: fileName, enableRangeProcessing: false);
            })
            .RequireGeneralPermission("files.download", "REVISION_FILE_DOWNLOAD_DENIED", "REVISION_FILE")
            .WithName("DownloadCleanRevisionFile");

        endpoints.MapGroup("/files")
            .WithTags("File Scan Status (V03)")
            .RequireAuthorization()
            .MapGet("/{fileId:guid}/scan-status", async (
                Guid fileId,
                HttpContext httpContext,
                IVersionsFilesService service,
                CancellationToken cancellationToken) =>
            {
                var status = await service.GetFileScanStatusAsync(
                    httpContext.User, fileId, httpContext.TraceIdentifier, cancellationToken);
                return Results.Ok(status);
            })
            .RequireGeneralPermission("files.view", "FILE_SCAN_STATUS_DENIED", "REVISION_FILE")
            .WithName("GetFileScanStatus")
            .Produces<FileScanStatusDto>(StatusCodes.Status200OK);
    }
}
