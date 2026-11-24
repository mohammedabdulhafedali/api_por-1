using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ApiGovernance.Shared.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ApiGovernance.Backend.Tests.Integration;

[Collection("IsolatedTestDatabase")]
public sealed class BackendContractIntegrationTests
{
    internal static string GetIsolatedTestDatabaseConnectionString()
    {
        var config = new ConfigurationBuilder()
            .AddUserSecrets<Program>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        var testConn = config.GetConnectionString("TestConnection")
                       ?? Environment.GetEnvironmentVariable("API_GOVERNANCE_TEST_DB_CONNECTION", EnvironmentVariableTarget.Process)
                       ?? Environment.GetEnvironmentVariable("API_GOVERNANCE_TEST_DB_CONNECTION", EnvironmentVariableTarget.User);

        if (!string.IsNullOrWhiteSpace(testConn))
        {
            return testConn;
        }

        var defaultConn = config.GetConnectionString("DefaultConnection")
                          ?? Environment.GetEnvironmentVariable("API_GOVERNANCE_DB_CONNECTION", EnvironmentVariableTarget.Process)
                          ?? Environment.GetEnvironmentVariable("API_GOVERNANCE_DB_CONNECTION", EnvironmentVariableTarget.User);

        if (!string.IsNullOrWhiteSpace(defaultConn))
        {
            return defaultConn.Replace("Database=api_governance_dev", "Database=api_governance_test", StringComparison.OrdinalIgnoreCase);
        }

        throw new InvalidOperationException("Isolated test database connection string is not configured in User Secrets or Environment Variables.");
    }

    internal static string GetIsolatedTestMigrationsConnectionString()
    {
        var config = new ConfigurationBuilder()
            .AddUserSecrets<Program>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        var testMigratorConn = config.GetConnectionString("TestMigrationsConnection")
                               ?? Environment.GetEnvironmentVariable("API_GOVERNANCE_TEST_MIGRATIONS_DB_CONNECTION", EnvironmentVariableTarget.Process)
                               ?? Environment.GetEnvironmentVariable("API_GOVERNANCE_TEST_MIGRATIONS_DB_CONNECTION", EnvironmentVariableTarget.User);

        if (!string.IsNullOrWhiteSpace(testMigratorConn))
        {
            return testMigratorConn;
        }

        var defaultMigratorConn = config.GetConnectionString("MigrationsConnection")
                                  ?? Environment.GetEnvironmentVariable("API_GOVERNANCE_MIGRATIONS_DB_CONNECTION", EnvironmentVariableTarget.Process)
                                  ?? Environment.GetEnvironmentVariable("API_GOVERNANCE_MIGRATIONS_DB_CONNECTION", EnvironmentVariableTarget.User);

        if (!string.IsNullOrWhiteSpace(defaultMigratorConn))
        {
            return defaultMigratorConn.Replace("Database=api_governance_dev", "Database=api_governance_test", StringComparison.OrdinalIgnoreCase);
        }

        throw new InvalidOperationException("Isolated test migrations connection string is not configured in User Secrets or Environment Variables.");
    }

    internal static WebApplicationFactory<Program> CreateFactory(
        string environmentName,
        string? overrideConnectionString = null)
    {
        var effectiveConnectionString = overrideConnectionString ?? GetIsolatedTestDatabaseConnectionString();

        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(environmentName);
                builder.ConfigureAppConfiguration((_, configBuilder) =>
                {
                    configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:DefaultConnection"] = effectiveConnectionString
                    });
                });
            });
    }

    [Fact]
    public async Task HealthCheck_InDevelopment_ReturnsDetailedHealthyReportWithDatabaseAnd10Modules()
    {
        await using var factory = CreateFactory("Development");
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("1.0", response.Headers.GetValues(ApiContractDefaults.ApiVersionHeaderName).Single());
        Assert.True(response.Headers.Contains(ApiContractDefaults.CorrelationIdHeaderName));
        Assert.False(response.Headers.Contains("Server"));

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        Assert.Equal("Healthy", root.GetProperty("status").GetString());
        Assert.Equal("ModularMonolith", root.GetProperty("architecture").GetString());
        Assert.Equal("Development", root.GetProperty("environment").GetString());
        Assert.Equal(10, root.GetProperty("moduleCount").GetInt32());
        Assert.Equal(10, root.GetProperty("modules").GetArrayLength());

        var checks = root.GetProperty("checks").EnumerateArray().ToList();
        Assert.Contains(checks, c => c.GetProperty("name").GetString() == "self" && c.GetProperty("status").GetString() == "Healthy");
        Assert.Contains(checks, c => c.GetProperty("name").GetString() == "database" && c.GetProperty("status").GetString() == "Healthy");
    }

    [Fact]
    public async Task HealthCheck_InProduction_ReturnsMinimalPayloadWithoutInternalDetails()
    {
        await using var factory = CreateFactory("Production");
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        Assert.Equal("Healthy", root.GetProperty("status").GetString());
        Assert.True(root.TryGetProperty("timestampUtc", out _));
        Assert.False(root.TryGetProperty("modules", out _));
        Assert.False(root.TryGetProperty("environment", out _));
        Assert.False(root.TryGetProperty("architecture", out _));
        Assert.False(root.TryGetProperty("checks", out _));
    }

    [Fact]
    public async Task OpenApiAndDiagnosticEndpoints_AreAvailableInDevelopment_AndDisabledInProduction()
    {
        await using (var devFactory = CreateFactory("Development"))
        {
            using var devClient = devFactory.CreateClient();

            using var openApiRes = await devClient.GetAsync("/openapi/v1.json");
            Assert.Equal(HttpStatusCode.OK, openApiRes.StatusCode);
            using var openApiDoc = JsonDocument.Parse(await openApiRes.Content.ReadAsStringAsync());
            Assert.StartsWith("3.", openApiDoc.RootElement.GetProperty("openapi").GetString());

            using var metaRes = await devClient.GetAsync("/api/v1/contracts/metadata");
            Assert.Equal(HttpStatusCode.OK, metaRes.StatusCode);
        }

        await using (var prodFactory = CreateFactory("Production"))
        {
            using var prodClient = prodFactory.CreateClient();

            using var openApiProd = await prodClient.GetAsync("/openapi/v1.json");
            Assert.Equal(HttpStatusCode.NotFound, openApiProd.StatusCode);

            using var metaProd = await prodClient.GetAsync("/api/v1/contracts/metadata");
            Assert.Equal(HttpStatusCode.NotFound, metaProd.StatusCode);

            using var probeProd = await prodClient.PostAsJsonAsync("/api/v1/contracts/validate-probe", new
            {
                code = "DOC-01",
                displayTitle = "Test Probe",
                pageSize = 10,
                simulateOutcome = "none"
            });
            Assert.Equal(HttpStatusCode.NotFound, probeProd.StatusCode);
        }
    }

    [Fact]
    public async Task ValidateProbe_WithValidInput_Returns200AndPropagatesCorrelationId()
    {
        await using var factory = CreateFactory("Development");
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/contracts/validate-probe")
        {
            Content = JsonContent.Create(new
            {
                code = "doc-api-10",
                displayTitle = "Payment Integration Spec",
                pageSize = 25,
                simulateOutcome = "none"
            })
        };
        request.Headers.Add(ApiContractDefaults.CorrelationIdHeaderName, "corr-f06-test-200");

        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("corr-f06-test-200", response.Headers.GetValues(ApiContractDefaults.CorrelationIdHeaderName).Single());

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.Equal("VALIDATED", root.GetProperty("status").GetString());
        Assert.Equal("DOC-API-10", root.GetProperty("normalizedCode").GetString());
        Assert.Equal("corr-f06-test-200", root.GetProperty("traceId").GetString());
    }

    [Theory]
    [InlineData("ar", "فشل التحقق من المدخلات")]
    [InlineData("en-US", "Input Validation Failed")]
    public async Task ValidateProbe_WithInvalidPayload_Returns400ProblemDetailsWithFieldErrors(
        string acceptLanguage,
        string expectedTitle)
    {
        await using var factory = CreateFactory("Development");
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/contracts/validate-probe")
        {
            Content = JsonContent.Create(new
            {
                code = "A",
                displayTitle = "",
                pageSize = 999,
                simulateOutcome = "none"
            })
        };
        request.Headers.Add("Accept-Language", acceptLanguage);
        request.Headers.Add(ApiContractDefaults.CorrelationIdHeaderName, "corr-f06-val-400");

        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        Assert.Equal(400, root.GetProperty("status").GetInt32());
        Assert.Equal(expectedTitle, root.GetProperty("title").GetString());
        Assert.Equal(ApiErrorCodes.ValidationFailed, root.GetProperty("code").GetString());
        Assert.Equal("corr-f06-val-400", root.GetProperty("traceId").GetString());

        var details = root.GetProperty("details");
        Assert.True(details.TryGetProperty("code", out _));
        Assert.True(details.TryGetProperty("displayTitle", out _));
        Assert.True(details.TryGetProperty("pageSize", out _));
    }

    [Theory]
    [InlineData("unauthorized", HttpStatusCode.Unauthorized, ApiErrorCodes.Unauthorized)]
    [InlineData("forbidden", HttpStatusCode.Forbidden, ApiErrorCodes.Forbidden)]
    [InlineData("notfound", HttpStatusCode.NotFound, ApiErrorCodes.NotFound)]
    [InlineData("conflict", HttpStatusCode.Conflict, ApiErrorCodes.Conflict)]
    [InlineData("payloadtoolarge", HttpStatusCode.RequestEntityTooLarge, ApiErrorCodes.PayloadTooLarge)]
    [InlineData("unprocessable", HttpStatusCode.UnprocessableEntity, ApiErrorCodes.UnprocessableEntity)]
    [InlineData("toomanyrequests", HttpStatusCode.TooManyRequests, ApiErrorCodes.TooManyRequests)]
    [InlineData("error", HttpStatusCode.InternalServerError, ApiErrorCodes.InternalError)]
    public async Task ValidateProbe_AllErrorStatuses_ReturnConsistentProblemDetailsWithoutLeakingInternals(
        string simulatedOutcome,
        HttpStatusCode expectedHttpStatus,
        string expectedErrorCode)
    {
        await using var factory = CreateFactory("Development");
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/contracts/validate-probe")
        {
            Content = JsonContent.Create(new
            {
                code = "TEST-01",
                displayTitle = "Outcome Check",
                pageSize = 20,
                simulateOutcome = simulatedOutcome
            })
        };
        request.Headers.Add("Accept-Language", "ar");
        request.Headers.Add(ApiContractDefaults.CorrelationIdHeaderName, $"corr-f06-{simulatedOutcome}");

        using var response = await client.SendAsync(request);
        Assert.Equal(expectedHttpStatus, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var rawBody = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("SENSITIVE_INTERNAL_DETAIL", rawBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("InvalidOperationException", rawBody, StringComparison.OrdinalIgnoreCase);

        using var doc = JsonDocument.Parse(rawBody);
        var root = doc.RootElement;

        Assert.Equal((int)expectedHttpStatus, root.GetProperty("status").GetInt32());
        Assert.Equal(expectedErrorCode, root.GetProperty("code").GetString());
        Assert.Equal($"corr-f06-{simulatedOutcome}", root.GetProperty("traceId").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("message").GetString()));
    }
}
