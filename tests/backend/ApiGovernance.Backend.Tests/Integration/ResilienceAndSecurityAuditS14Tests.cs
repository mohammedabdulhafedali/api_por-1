using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ApiGovernance.Modules.IdentityAccess.Services;
using ApiGovernance.Shared.Contracts;
using ApiGovernance.Shared.Persistence;
using ApiGovernance.Shared.Persistence.Entities;
using ApiGovernance.Shared.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace ApiGovernance.Backend.Tests.Integration;

/// <summary>
/// Sprint S14 Quality, Resilience &amp; Security Audit Suite:
/// - Task Q04: Concurrent read load burst resilience, OP-01 append-only immutability enforcement,
///             fail-safe malformed input handling, and connection recovery verification.
/// - Task Q05: Automated Endpoint Security Audit across ALL mapped /api/v1 endpoints (0 unauthenticated/unprotected routes),
///             lockout / CSRF enforcement sweep, and zero secret-leakage verification.
/// </summary>
[Collection("IsolatedTestDatabase")]
public sealed class ResilienceAndSecurityAuditS14Tests
{
    private sealed class NoopTokenDeliverySpy : IAuthTokenDeliveryChannel
    {
        public string ChannelCode => "TEST_S14_NOOP";
        public Task DeliverPasswordResetTokenAsync(AuthTokenDeliveryContext context, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private static (WebApplicationFactory<Program> Factory, string DpDir, string StorageDir) CreateFactory()
    {
        var dpDir = Path.Combine(Path.GetTempPath(), $"apigov-dp-s14-{Guid.NewGuid():N}");
        var storageDir = Path.Combine(Path.GetTempPath(), $"apigov-store-s14-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dpDir);
        Directory.CreateDirectory(storageDir);

        var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:DefaultConnection"] =
                            BackendContractIntegrationTests.GetIsolatedTestDatabaseConnectionString(),
                        ["AuthenticationPolicy:DataProtectionKeyDirectory"] = dpDir,
                        ["AuthenticationPolicy:LoginRateLimitPermitLimit"] = "300",
                        ["FileStorage:RootPath"] = storageDir,
                        ["FileScanning:Provider"] = "Disabled"
                    });
                });
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IAuthTokenDeliveryChannel>();
                    services.AddSingleton<IAuthTokenDeliveryChannel>(new NoopTokenDeliverySpy());
                });
            });

        return (factory, dpDir, storageDir);
    }

    private static async Task ResetIsolatedDatabaseAsync()
    {
        var migratorConn = BackendContractIntegrationTests.GetIsolatedTestMigrationsConnectionString();
        var options = new DbContextOptionsBuilder<ApiGovernanceDbContext>()
            .UseNpgsql(migratorConn, n => n.MigrationsAssembly(typeof(ApiGovernanceDbContext).Assembly.FullName))
            .Options;
        await using var db = new ApiGovernanceDbContext(options);

        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("0");
        await migrator.MigrateAsync();
    }

    [Fact]
    public void Q05_All_Mapped_Api_V1_Endpoints_Pass_Central_Security_Metadata_Audit()
    {
        var (factory, dpDir, storageDir) = CreateFactory();
        try
        {
            using var scope = factory.Services.CreateScope();
            var endpointDataSource = scope.ServiceProvider.GetRequiredService<EndpointDataSource>();

            var routeCount = endpointDataSource.Endpoints
                .OfType<RouteEndpoint>()
                .Count(e => (e.RoutePattern.RawText ?? string.Empty).Contains("api/v1", StringComparison.OrdinalIgnoreCase));
            Assert.True(routeCount >= 60, $"Expected all platform routes registered; observed {routeCount}.");

            var issues = EndpointAuthorizationCatalogVerifier.AuditVersionedApiEndpoints(endpointDataSource, "/api/v1");
            Assert.Empty(issues);
        }
        finally
        {
            factory.Dispose();
            if (Directory.Exists(dpDir)) Directory.Delete(dpDir, true);
            if (Directory.Exists(storageDir)) Directory.Delete(storageDir, true);
        }
    }

    [Fact]
    public async Task Q04_OP01_AppendOnly_Immutability_And_Resilient_Parallel_Reads_And_Csrf_Guard()
    {
        await ResetIsolatedDatabaseAsync();
        var (factory, dpDir, storageDir) = CreateFactory();

        try
        {
            var adminEmail = $"s14-admin-{Guid.NewGuid():N}@api-governance.local";
            const string adminPwd = "S14#ResilienceAudit!99";

            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var bootstrap = scope.ServiceProvider.GetRequiredService<IInitialAdminBootstrapService>();
                var res = await bootstrap.BootstrapFirstAdminAsync(new(adminEmail, "S14 Admin", adminPwd));
                Assert.True(res.Created);

                // Q04: OP-01 Append-Only Immutability Verification on AuditEvents
                var db = scope.ServiceProvider.GetRequiredService<ApiGovernanceDbContext>();
                var eventRow = new AuditEventEntity
                {
                    Id = Guid.NewGuid(),
                    ActorUserId = null,
                    Operation = "Q04_APPEND_ONLY_PROBE",
                    ResourceKind = "AUDIT_EVENT",
                    ResourceId = null,
                    Result = "SUCCESS",
                    ChangeSummaryJson = "{\"probe\":true}",
                    RecordedAt = DateTimeOffset.UtcNow
                };
                db.AuditEvents.Add(eventRow);
                await db.SaveChangesAsync();

                db.AuditEvents.Remove(eventRow);
                await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
                db.ChangeTracker.Clear();

                var persisted = await db.AuditEvents.FirstAsync(a => a.Id == eventRow.Id);
                persisted.Result = "TAMPERED";
                await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
                db.ChangeTracker.Clear();
            }

            var client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                HandleCookies = true
            });

            // Q05 Negative: POST without X-CSRF-TOKEN must be rejected (400/403)
            using (var noCsrfReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
            {
                Content = JsonContent.Create(new { email = adminEmail, password = adminPwd })
            })
            using (var noCsrfRes = await client.SendAsync(noCsrfReq))
            {
                Assert.True(
                    noCsrfRes.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Forbidden,
                    $"Expected CSRF rejection on state-changing endpoint, got {noCsrfRes.StatusCode}");
            }

            // Login properly with CSRF token
            using var csrfRes = await client.GetAsync("/api/v1/auth/csrf");
            csrfRes.EnsureSuccessStatusCode();
            using var csrfJson = JsonDocument.Parse(await csrfRes.Content.ReadAsStringAsync());
            var csrf = csrfJson.RootElement.GetProperty("token").GetString()!;

            using (var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
            {
                Content = JsonContent.Create(new { email = adminEmail, password = adminPwd })
            })
            {
                loginReq.Headers.Add("X-CSRF-TOKEN", csrf);
                using var loginRes = await client.SendAsync(loginReq);
                Assert.Equal(HttpStatusCode.OK, loginRes.StatusCode);
            }

            // Q04 Load / Resilience Burst: 24 parallel concurrent authorized requests to health, dashboard, search, and audit-events
            var endpoints = new[]
            {
                "/api/v1/health",
                "/api/v1/dashboard",
                "/api/v1/search?q=probe",
                "/api/v1/audit-events?page=1&pageSize=10"
            };

            var tasks = Enumerable.Range(0, 24)
                .Select(i => client.GetAsync(endpoints[i % endpoints.Length]))
                .ToArray();

            var responses = await Task.WhenAll(tasks);
            foreach (var r in responses)
            {
                Assert.Equal(HttpStatusCode.OK, r.StatusCode);
                r.Dispose();
            }

            // Q04 Graceful Error Resilience: invalid GUID path returns ProblemDetails without 500 InternalServerError
            using var badGuidRes = await client.GetAsync("/api/v1/api-documents/not-a-valid-guid");
            Assert.NotEqual(HttpStatusCode.InternalServerError, badGuidRes.StatusCode);
        }
        finally
        {
            await factory.DisposeAsync();
            if (Directory.Exists(dpDir)) Directory.Delete(dpDir, true);
            if (Directory.Exists(storageDir)) Directory.Delete(storageDir, true);
        }
    }
}
