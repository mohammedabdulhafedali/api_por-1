using System.Text.Json;
using ApiGovernance.Modules.VersionsFiles.Configuration;
using ApiGovernance.Modules.VersionsFiles.Contracts;
using ApiGovernance.Modules.VersionsFiles.OpenXml;
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
/// Module 6: Versions &amp; Files
/// - Sprint S08 (V01–V04): Private immutable file store, upload validation &amp; quarantine, fail-closed security scanning, manual free-text API versions.
/// - Sprint S09 (V05–V08): Documentation package revisions &amp; mandatory file validation, approved-revision immutability &amp; working copies (OP-05),
///   audited SHA-256 verified downloads, and Open XML SDK Word governance derivative PoC (OP-03).
/// </summary>
public sealed class VersionsFilesModule : IBackendModule
{
    private static readonly JsonSerializerOptions BodyJsonOpts = new(JsonSerializerDefaults.Web);

    public ModuleDescriptor Descriptor { get; } = new(
        Code: "VersionsFiles",
        Name: "API Versions, Revisions & Immutable Files",
        PlannedTasks: "V01, V02, V03, V04, V05, V06, V07, V08");

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<FileStorageOptions>(configuration.GetSection(FileStorageOptions.SectionName));
        services.Configure<FileScanningOptions>(configuration.GetSection(FileScanningOptions.SectionName));

        services.AddSingleton<IPrivateFileStore, LocalPrivateFileStore>();
        services.AddSingleton<IPublishedObjectReadAccessor>(
            sp => (LocalPrivateFileStore)sp.GetRequiredService<IPrivateFileStore>());
        services.AddSingleton<IFileSecurityScanner, ConfiguredFileSecurityScanner>();
        services.AddSingleton<FileUploadContentValidator>();
        services.AddSingleton<IWordGovernanceDerivativeService, OpenXmlWordGovernanceDerivativeService>();
        services.AddScoped<IVersionsFilesService, VersionsFilesService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        // 1. Integration API Versions (V04)
        var integrationGroup = endpoints.MapGroup("/integrations")
            .WithTags("API Versions & Files Workspace (V01-V08)")
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

        // 2. Documentation Package Revisions & Working Copies (V05-V06)
        var versionGroup = endpoints.MapGroup("/versions")
            .WithTags("Document Revisions (V04-V06)")
            .RequireAuthorization();

        versionGroup.MapGet("/{versionId:guid}/revisions", async (
                Guid versionId,
                HttpContext httpContext,
                IVersionsFilesService service,
                CancellationToken cancellationToken) =>
            {
                var revisions = await service.GetVersionRevisionsAsync(
                    httpContext.User, versionId, httpContext.TraceIdentifier, cancellationToken);
                return Results.Ok(revisions);
            })
            .RequireGeneralPermission("revisions.view", "REVISION_LIST_DENIED", "DOCUMENT_REVISION")
            .WithName("ListVersionRevisions")
            .Produces<IReadOnlyList<DocumentRevisionDto>>(StatusCodes.Status200OK);

        versionGroup.MapPost("/{versionId:guid}/revisions", async (
                Guid versionId,
                HttpContext httpContext,
                IVersionsFilesService service,
                CancellationToken cancellationToken) =>
            {
                CreateDocumentRevisionRequest? req = null;
                if (httpContext.Request.ContentLength is > 0 ||
                    (httpContext.Request.ContentType?.Contains("application/json", StringComparison.OrdinalIgnoreCase) ?? false))
                {
                    httpContext.Request.EnableBuffering();
                    using var reader = new StreamReader(httpContext.Request.Body, leaveOpen: true);
                    var raw = await reader.ReadToEndAsync(cancellationToken);
                    if (!string.IsNullOrWhiteSpace(raw))
                    {
                        try
                        {
                            req = JsonSerializer.Deserialize<CreateDocumentRevisionRequest>(raw, BodyJsonOpts);
                        }
                        catch (JsonException)
                        {
                            throw ApiContractException.BadRequest(
                                messageAr: "صيغة طلب إنشاء المراجعة غير صالحة.",
                                messageEn: "Invalid JSON payload for creating a document revision.");
                        }
                    }
                }

                var created = await service.CreateRevisionAsync(
                    httpContext.User, versionId, req, httpContext.TraceIdentifier, cancellationToken);
                return Results.Created($"/api/v1/revisions/{created.Id}", created);
            })
            .WithCsrfValidation()
            .RequireGeneralPermission("revisions.create", "REVISION_CREATE_DENIED", "DOCUMENT_REVISION")
            .WithName("CreateDraftDocumentRevision")
            .Produces<DocumentRevisionDto>(StatusCodes.Status201Created);

        // 3. Revision Details, Submission, Files, Verified Download & Word Derivatives (V01-V08)
        var revisionGroup = endpoints.MapGroup("/revisions")
            .WithTags("Revision Files, Scanning, Downloads & Word PoC (V01-V08)")
            .RequireAuthorization();

        revisionGroup.MapGet("/{revisionId:guid}", async (
                Guid revisionId,
                HttpContext httpContext,
                IVersionsFilesService service,
                CancellationToken cancellationToken) =>
            {
                var details = await service.GetRevisionDetailsAsync(
                    httpContext.User, revisionId, httpContext.TraceIdentifier, cancellationToken);
                return Results.Ok(details);
            })
            .RequireGeneralPermission("revisions.view", "REVISION_DETAILS_DENIED", "DOCUMENT_REVISION")
            .WithName("GetRevisionDetails")
            .Produces<RevisionDetailsResponse>(StatusCodes.Status200OK);

        revisionGroup.MapPost("/{revisionId:guid}/submit", async (
                Guid revisionId,
                HttpContext httpContext,
                IVersionsFilesService service,
                CancellationToken cancellationToken) =>
            {
                var submitted = await service.SubmitRevisionAsync(
                    httpContext.User, revisionId, httpContext.TraceIdentifier, cancellationToken);
                return Results.Ok(submitted);
            })
            .WithCsrfValidation()
            .RequireGeneralPermission("revisions.submit", "REVISION_SUBMIT_DENIED", "DOCUMENT_REVISION")
            .WithName("SubmitDocumentRevision")
            .Produces<SubmitRevisionResponse>(StatusCodes.Status200OK);

        revisionGroup.MapGet("/{revisionId:guid}/files", async (
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

        revisionGroup.MapPost("/{revisionId:guid}/files", async (
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

        revisionGroup.MapPost("/{revisionId:guid}/files/{fileId:guid}/rescan", async (
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

        revisionGroup.MapDelete("/{revisionId:guid}/files/{fileId:guid}", async (
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

        revisionGroup.MapGet("/{revisionId:guid}/files/{fileId:guid}/download", async (
                Guid revisionId,
                Guid fileId,
                HttpContext httpContext,
                IVersionsFilesService service,
                CancellationToken cancellationToken) =>
            {
                var (stream, fileName, contentType, _, sha256) = await service.DownloadCleanRevisionFileAsync(
                    httpContext.User, revisionId, fileId, cancellationToken);

                ApplyDownloadSecurityHeaders(httpContext.Response, sha256);
                return Results.File(stream, contentType, fileDownloadName: fileName, enableRangeProcessing: false);
            })
            .RequireGeneralPermission("files.download", "REVISION_FILE_DOWNLOAD_DENIED", "REVISION_FILE")
            .WithName("DownloadCleanRevisionFile");

        // 4. Word Governance Derivative PoC (V08 / OP-03)
        revisionGroup.MapPost("/{revisionId:guid}/files/{fileId:guid}/word-derivative", async (
                Guid revisionId,
                Guid fileId,
                HttpContext httpContext,
                IVersionsFilesService service,
                CancellationToken cancellationToken) =>
            {
                var report = await service.GenerateWordDerivativeAsync(
                    httpContext.User, revisionId, fileId, httpContext.TraceIdentifier, cancellationToken);
                return Results.Ok(report);
            })
            .WithCsrfValidation()
            .RequireGeneralPermission("files.upload", "WORD_DERIVATIVE_GENERATE_DENIED", "REVISION_FILE")
            .WithName("GenerateWordGovernanceDerivative")
            .Produces<WordDerivativeRecordDto>(StatusCodes.Status200OK);

        revisionGroup.MapGet("/{revisionId:guid}/files/{fileId:guid}/word-derivative", async (
                Guid revisionId,
                Guid fileId,
                HttpContext httpContext,
                IVersionsFilesService service,
                CancellationToken cancellationToken) =>
            {
                var report = await service.GetLatestWordDerivativeAsync(
                    httpContext.User, revisionId, fileId, httpContext.TraceIdentifier, cancellationToken);
                return report is null ? Results.NoContent() : Results.Ok(report);
            })
            .RequireGeneralPermission("files.view", "WORD_DERIVATIVE_READ_DENIED", "REVISION_FILE")
            .WithName("GetWordGovernanceDerivativeReport")
            .Produces<WordDerivativeRecordDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status204NoContent);

        revisionGroup.MapGet("/{revisionId:guid}/files/{fileId:guid}/word-derivative/download", async (
                Guid revisionId,
                Guid fileId,
                HttpContext httpContext,
                IVersionsFilesService service,
                CancellationToken cancellationToken) =>
            {
                var (stream, fileName, contentType, _, sha256) = await service.DownloadVerifiedWordDerivativeAsync(
                    httpContext.User, revisionId, fileId, cancellationToken);

                ApplyDownloadSecurityHeaders(httpContext.Response, sha256);
                return Results.File(stream, contentType, fileDownloadName: fileName, enableRangeProcessing: false);
            })
            .RequireGeneralPermission("files.download", "WORD_DERIVATIVE_DOWNLOAD_DENIED", "REVISION_FILE")
            .WithName("DownloadVerifiedWordDerivative");

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

    private static void ApplyDownloadSecurityHeaders(HttpResponse response, string sha256)
    {
        response.Headers.XContentTypeOptions = "nosniff";
        response.Headers.CacheControl = "no-store, no-cache";
        response.Headers.ContentSecurityPolicy = "sandbox";
        response.Headers.ETag = $"\"{sha256}\"";
        response.Headers["X-File-Sha256"] = sha256;
    }
}
