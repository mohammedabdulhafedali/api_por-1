using System.Text.Json;
using ApiGovernance.Modules.AuditNotifications;
using ApiGovernance.Modules.Changes;
using ApiGovernance.Modules.Configuration;
using ApiGovernance.Modules.Documents;
using ApiGovernance.Modules.IdentityAccess;
using ApiGovernance.Modules.IdentityAccess.Configuration;
using ApiGovernance.Modules.IdentityAccess.Services;
using ApiGovernance.Modules.Integrations;
using ApiGovernance.Modules.MasterData;
using ApiGovernance.Modules.PublishingPortal;
using ApiGovernance.Modules.VersionsFiles;
using ApiGovernance.Modules.Workflow;
using ApiGovernance.Shared.Contracts;
using ApiGovernance.Shared.Modularity;
using ApiGovernance.Shared.Persistence;
using ApiGovernance.Shared.Security;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

// Ensure User Secrets are loaded in Development and Testing environments (outside Git)
if (builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing"))
{
    builder.Configuration.AddUserSecrets<Program>(optional: true);
}

// Suppress Kestrel Server header fingerprinting in all environments
builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
});

// Register EF Core 10 + Npgsql 10 Persistence Layer & Database Connectivity Probe (Task F04)
builder.Services.AddApiGovernancePersistence(builder.Configuration);

// Register Central Server Authorization Component (Task I05) & Document ACL Engine (Task I07) across all Modular Monolith modules
builder.Services.AddScoped<IServerAuthorizationService, ServerAuthorizationService>();
builder.Services.AddScoped<IDocumentAuthorizationService, DocumentAuthorizationService>();

// Register the 10 approved Modular Monolith domain modules
IBackendModule[] modules =
[
    new IdentityAccessModule(),
    new MasterDataModule(),
    new ConfigurationModule(),
    new DocumentsModule(),
    new IntegrationsModule(),
    new VersionsFilesModule(),
    new ChangesModule(),
    new WorkflowModule(),
    new PublishingPortalModule(),
    new AuditNotificationsModule()
];

builder.Services.AddSingleton<IReadOnlyList<IBackendModule>>(modules);

foreach (var module in modules)
{
    module.RegisterServices(builder.Services, builder.Configuration);
}

// Standardized Problem Details & centralized exception handling (F07)
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = ctx =>
    {
        var statusCode = ctx.ProblemDetails.Status ?? StatusCodes.Status500InternalServerError;
        var isArabic = ApiErrorLocalizer.PrefersArabic(ctx.HttpContext);
        var errorCode = ctx.ProblemDetails.Extensions.TryGetValue(ApiContractDefaults.CodeExtensionKey, out var existingCode) && existingCode is string codeStr
            ? codeStr
            : ApiErrorCodes.FromStatusCode(statusCode);

        var (title, message) = ApiErrorLocalizer.GetLocalizedDefaults(statusCode, isArabic, errorCode);

        ctx.ProblemDetails.Type ??= $"https://urn.api-governance.local/problems/{errorCode.ToLowerInvariant()}";
        ctx.ProblemDetails.Title = title;
        ctx.ProblemDetails.Detail ??= message;
        ctx.ProblemDetails.Instance ??= ctx.HttpContext.Request.Path.Value;

        ctx.ProblemDetails.Extensions[ApiContractDefaults.CodeExtensionKey] = errorCode;
        ctx.ProblemDetails.Extensions[ApiContractDefaults.MessageExtensionKey] = ctx.ProblemDetails.Detail;
        if (!ctx.ProblemDetails.Extensions.ContainsKey(ApiContractDefaults.DetailsExtensionKey))
        {
            ctx.ProblemDetails.Extensions[ApiContractDefaults.DetailsExtensionKey] = null;
        }
        ctx.ProblemDetails.Extensions[ApiContractDefaults.TraceIdExtensionKey] = ctx.HttpContext.TraceIdentifier;
    };
});

builder.Services.AddExceptionHandler<GlobalProblemDetailsExceptionHandler>();

// OpenAPI v1 specification generation per approved architecture (F07)
builder.Services.AddOpenApi("v1", options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info.Title = "API Governance Platform — Unified HTTP API Contract";
        document.Info.Version = "v1.2 (API v1.0)";
        document.Info.Description =
            "Standardized REST API contract for the API Documentation & Integrations Governance Platform (Modular Monolith). " +
            "All error responses adhere to RFC 9457 Problem Details with { code, message, details, traceId }. " +
            "Catalog baseline: 99 planned endpoints across 10 bounded modules.";
        return Task.CompletedTask;
    });
});

builder.Services
    .AddHealthChecks()
    .AddCheck(
        name: "self",
        check: () => HealthCheckResult.Healthy("Operational"),
        tags: ["live", "ready"])
    .AddCheck<PostgreSqlReadinessHealthCheck>(
        name: "database",
        failureStatus: HealthStatus.Unhealthy,
        tags: ["ready", "db"]);

var app = builder.Build();

// Optional one-time CLI operator command for creating the initial administrator safely:
// dotnet run --project src/ApiGovernance.Host/ApiGovernance.Host.csproj -- bootstrap-admin --email <email> --full-name "<name>" [--password-env <ENV_VAR>]
if (args.Length > 0 && string.Equals(args[0], "bootstrap-admin", StringComparison.OrdinalIgnoreCase))
{
    var bootstrapConfig = app.Configuration.GetSection(BootstrapAdminOptions.SectionName).Get<BootstrapAdminOptions>()
                          ?? new BootstrapAdminOptions();

    var email = ReadCliArg(args, "--email") ?? bootstrapConfig.Email;
    var fullName = ReadCliArg(args, "--full-name") ?? bootstrapConfig.FullName;
    var passwordEnvName = ReadCliArg(args, "--password-env");
    var initialPassword = !string.IsNullOrWhiteSpace(passwordEnvName)
        ? Environment.GetEnvironmentVariable(passwordEnvName)
        : bootstrapConfig.Password;

    if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(fullName))
    {
        Console.Error.WriteLine("Missing required --email and --full-name (or BootstrapAdmin config in User Secrets).");
        Environment.ExitCode = 1;
        return;
    }

    await using var scope = app.Services.CreateAsyncScope();
    var bootstrapService = scope.ServiceProvider.GetRequiredService<IInitialAdminBootstrapService>();
    var outcome = await bootstrapService.BootstrapFirstAdminAsync(
        new InitialAdminBootstrapRequest(email, fullName, initialPassword));

    if (!outcome.Created)
    {
        Console.Error.WriteLine(
            $"Bootstrap disabled ({outcome.StatusCode}): initial administrator has already been initialized.");
        Environment.ExitCode = 1;
        return;
    }

    Console.WriteLine(
        $"Bootstrap result: {outcome.StatusCode} | Created={outcome.Created} | AdminUserId={outcome.AdminUserId} | RequiresActivation={outcome.RequiresActivation}");
    return;
}

static string? ReadCliArg(string[] cliArgs, string flagName)
{
    for (var i = 1; i < cliArgs.Length - 1; i++)
    {
        if (string.Equals(cliArgs[i], flagName, StringComparison.OrdinalIgnoreCase))
        {
            return cliArgs[i + 1];
        }
    }

    return null;
}

// Guarded non-Git auto-bootstrap when explicitly enabled via User Secrets / Environment Variables on an empty DB
{
    var bootstrapSection = app.Configuration.GetSection(BootstrapAdminOptions.SectionName).Get<BootstrapAdminOptions>();
    if (bootstrapSection is { AutoRunOnEmptyDatabase: true } &&
        !string.IsNullOrWhiteSpace(bootstrapSection.Email) &&
        !string.IsNullOrWhiteSpace(bootstrapSection.FullName))
    {
        await using var scope = app.Services.CreateAsyncScope();
        var bootstrapService = scope.ServiceProvider.GetRequiredService<IInitialAdminBootstrapService>();
        await bootstrapService.BootstrapFirstAdminAsync(
            new InitialAdminBootstrapRequest(
                bootstrapSection.Email,
                bootstrapSection.FullName,
                bootstrapSection.Password));
    }
}

// 1. Correlation ID, API version headers, and server fingerprinting suppression
app.UseMiddleware<RequestCorrelationMiddleware>();

// 2. Centralized Problem Details exception handler and status code pages (404/405/etc.)
app.UseExceptionHandler();
app.UseStatusCodePages(async statusCodeContext =>
{
    var httpContext = statusCodeContext.HttpContext;
    if (httpContext.Response.HasStarted)
    {
        return;
    }

    var statusCode = httpContext.Response.StatusCode;
    var isArabic = ApiErrorLocalizer.PrefersArabic(httpContext);
    var errorCode = ApiErrorCodes.FromStatusCode(statusCode);
    var (title, message) = ApiErrorLocalizer.GetLocalizedDefaults(statusCode, isArabic, errorCode);

    var problem = GlobalProblemDetailsExceptionHandler.CreateProblemDetails(
        httpContext,
        statusCode,
        errorCode,
        title,
        message,
        details: null,
        traceId: httpContext.TraceIdentifier);

    httpContext.Response.ContentType = "application/problem+json; charset=utf-8";
    await httpContext.Response.WriteAsJsonAsync(
        problem,
        options: null,
        contentType: "application/problem+json; charset=utf-8",
        cancellationToken: httpContext.RequestAborted);
});

// 2b. Explicit Routing, Rate limiting, Cookie Session Authentication, and Authorization (I01)
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// 3. Versioned API route group (/api/v1) — Fail-closed by default (RequireAuthorization)
var apiV1 = app.MapGroup(ApiContractDefaults.BasePathV1).RequireAuthorization();
foreach (var module in modules)
{
    module.MapEndpoints(apiV1);
}

// 4. OpenAPI & F07 Contract diagnostic/probe endpoints:
// STRICTLY DISABLED in Production (mapped only in Development/Testing or when explicitly enabled for non-production contract tests).
var enableContractDiagnostics = !app.Environment.IsProduction() &&
                                (app.Environment.IsDevelopment() ||
                                 app.Environment.IsEnvironment("Testing") ||
                                 app.Configuration.GetValue<bool>("Platform:EnableContractDiagnostics"));

if (enableContractDiagnostics)
{
    app.MapOpenApi("/openapi/{documentName}.json").AllowAnonymous();
    app.MapOpenApi("/api/v1/openapi/{documentName}.json").AllowAnonymous();

    var contractsGroup = apiV1.MapGroup("/contracts")
        .WithTags("API Contracts & Diagnostics (Non-Production Only)")
        .AllowAnonymous();

    contractsGroup.MapGet("/metadata", (HttpContext httpContext) =>
        {
            var response = new ApiContractMetadataResponse(
                ApiVersion: ApiContractDefaults.CurrentVersion,
                BasePath: ApiContractDefaults.BasePathV1,
                ErrorFormat: "application/problem+json (RFC 9457 + code, message, details, traceId)",
                CorrelationHeader: ApiContractDefaults.CorrelationIdHeaderName,
                CatalogEndpointCount: 99,
                ModuleCount: modules.Length,
                SupportedErrorStatusCodes:
                [
                    StatusCodes.Status400BadRequest,
                    StatusCodes.Status401Unauthorized,
                    StatusCodes.Status403Forbidden,
                    StatusCodes.Status404NotFound,
                    StatusCodes.Status409Conflict,
                    StatusCodes.Status413PayloadTooLarge,
                    StatusCodes.Status422UnprocessableEntity,
                    StatusCodes.Status429TooManyRequests,
                    StatusCodes.Status500InternalServerError
                ],
                TraceId: httpContext.TraceIdentifier);

            return Results.Ok(response);
        })
        .WithName("GetApiContractMetadata")
        .WithSummary("Returns the unified API v1 contract metadata, error format standard, and supported status codes (Non-Production Only).")
        .Produces<ApiContractMetadataResponse>(StatusCodes.Status200OK);

    contractsGroup.MapPost("/validate-probe", (ContractValidationProbeRequest request, HttpContext httpContext) =>
        {
            switch (request.SimulateOutcome.ToLowerInvariant())
            {
                case "unauthorized":
                    throw ApiContractException.Unauthorized();
                case "forbidden":
                    throw ApiContractException.Forbidden();
                case "notfound":
                    throw ApiContractException.NotFound();
                case "conflict":
                    throw ApiContractException.Conflict(
                        messageAr: "تعارض في حالة أو نسخة المورد المطلوب.",
                        messageEn: "The requested operation conflicts with the current resource state or version.",
                        details: new { targetCode = request.Code });
                case "payloadtoolarge":
                    throw ApiContractException.PayloadTooLarge();
                case "unprocessable":
                    throw ApiContractException.UnprocessableEntity(
                        messageAr: "لا يمكن إتمام العملية لعدم تحقق شروط العمل المعتمدة.",
                        messageEn: "The operation cannot be completed because business preconditions were not satisfied.",
                        details: new { rule = "CONTRACT_PRECONDITION_CHECK" });
                case "toomanyrequests":
                    throw ApiContractException.TooManyRequests();
                case "error":
                    throw new InvalidOperationException("SENSITIVE_INTERNAL_DETAIL: Simulated failure at internal component.");
            }

            var response = new ContractValidationProbeResponse(
                ApiVersion: ApiContractDefaults.CurrentVersion,
                Status: "VALIDATED",
                NormalizedCode: request.Code!.Trim().ToUpperInvariant(),
                DisplayTitle: request.DisplayTitle!.Trim(),
                PageSize: request.PageSize,
                TraceId: httpContext.TraceIdentifier,
                TimestampUtc: DateTimeOffset.UtcNow);

            return Results.Ok(response);
        })
        .WithName("ValidateApiContractProbe")
        .WithSummary("Validates a sample contract payload and demonstrates deterministic RFC 9457 Problem Details responses in non-production environments.")
        .WithRequestValidation<ContractValidationProbeRequest>()
        .Produces<ContractValidationProbeResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .ProducesProblem(StatusCodes.Status429TooManyRequests)
        .ProducesProblem(StatusCodes.Status500InternalServerError);
}

// 5. Environment-aware Health Check response writer (includes PostgreSQL 18 readiness without leaking secrets):
// In Production, returns only minimal { status, timestampUtc } without internal module, check, or environment details.
// In Development/Testing, includes module descriptors and non-sensitive check statuses.
var healthCheckOptions = new HealthCheckOptions
{
    ResultStatusCodes =
    {
        [HealthStatus.Healthy] = StatusCodes.Status200OK,
        [HealthStatus.Degraded] = StatusCodes.Status200OK,
        [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
    },
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing"))
        {
            var minimalPayload = new
            {
                status = report.Status.ToString(),
                timestampUtc = DateTimeOffset.UtcNow
            };

            await JsonSerializer.SerializeAsync(
                context.Response.Body,
                minimalPayload,
                jsonOptions,
                context.RequestAborted);
            return;
        }

        var detailedPayload = new
        {
            status = report.Status.ToString(),
            service = app.Configuration["Platform:ServiceName"] ?? "API Governance Platform Backend",
            architecture = app.Configuration["Platform:Architecture"] ?? "ModularMonolith",
            apiVersion = ApiContractDefaults.CurrentVersion,
            baselineVersion = app.Configuration["Platform:BaselineVersion"] ?? "1.2",
            sprint = app.Configuration["Platform:Sprint"] ?? "S14",
            task = app.Configuration["Platform:Task"] ?? "Q07",
            environment = app.Environment.EnvironmentName,
            timestampUtc = DateTimeOffset.UtcNow,
            moduleCount = modules.Length,
            modules = modules.Select(m => new
            {
                code = m.Descriptor.Code,
                name = m.Descriptor.Name,
                plannedTasks = m.Descriptor.PlannedTasks
            }),
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                description = entry.Value.Description,
                durationMs = Math.Round(entry.Value.Duration.TotalMilliseconds, 3)
            })
        };

        await JsonSerializer.SerializeAsync(
            context.Response.Body,
            detailedPayload,
            jsonOptions,
            context.RequestAborted);
    }
};

app.MapHealthChecks("/health", healthCheckOptions).AllowAnonymous();
apiV1.MapHealthChecks("/health", healthCheckOptions).AllowAnonymous();

app.Run();

public partial class Program;
