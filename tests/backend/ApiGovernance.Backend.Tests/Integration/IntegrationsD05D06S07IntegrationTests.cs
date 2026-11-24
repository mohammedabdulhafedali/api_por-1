using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ApiGovernance.Modules.IdentityAccess.Services;
using ApiGovernance.Shared.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace ApiGovernance.Backend.Tests.Integration;

[Collection("IsolatedTestDatabase")]
public sealed class IntegrationsD05D06S07IntegrationTests
{
    private sealed class CapturingDeliverySpy : IAuthTokenDeliveryChannel
    {
        public string ChannelCode => "TEST_S07_SPY";
        public List<AuthTokenDeliveryContext> Deliveries { get; } = [];

        public Task DeliverPasswordResetTokenAsync(
            AuthTokenDeliveryContext context,
            CancellationToken cancellationToken = default)
        {
            Deliveries.Add(context);
            return Task.CompletedTask;
        }
    }

    private static (WebApplicationFactory<Program> Factory, CapturingDeliverySpy Spy, string DpDir) CreateFactory()
    {
        var dpDir = Path.Combine(Path.GetTempPath(), "ApiGovTestDP", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dpDir);
        var spy = new CapturingDeliverySpy();

        var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.ConfigureAppConfiguration((_, configBuilder) =>
                {
                    configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:DefaultConnection"] = BackendContractIntegrationTests.GetIsolatedTestDatabaseConnectionString(),
                        ["AuthenticationPolicy:DataProtectionKeyDirectory"] = dpDir,
                        ["AuthenticationPolicy:LoginRateLimitPermitLimit"] = "150"
                    });
                });
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IAuthTokenDeliveryChannel>();
                    services.AddSingleton<IAuthTokenDeliveryChannel>(spy);
                });
            });

        return (factory, spy, dpDir);
    }

    private static HttpClient CreateCookieClient(WebApplicationFactory<Program> factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

    private static async Task<string> FetchCsrfAsync(HttpClient client)
    {
        using var res = await client.GetAsync("/api/v1/auth/csrf");
        res.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("token").GetString()!;
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

    private static async Task<(Guid AdminId, HttpClient Client, string Csrf)> BootstrapAndLoginAdminAsync(
        WebApplicationFactory<Program> factory,
        CapturingDeliverySpy spy)
    {
        const string adminPass = "Bootstr@pAdminS07!2026";
        var adminEmail = $"sysadmin-s07-{Guid.NewGuid():N}@api-governance.local";

        Guid adminId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var bootstrap = scope.ServiceProvider.GetRequiredService<IInitialAdminBootstrapService>();
            var res = await bootstrap.BootstrapFirstAdminAsync(new InitialAdminBootstrapRequest(adminEmail, "مدير النظام S07"));
            adminId = res.AdminUserId!.Value;
        }

        var client = CreateCookieClient(factory);
        var csrf = await FetchCsrfAsync(client);
        var token = spy.Deliveries.Last(d => d.TargetUserId == adminId).SingleUseToken;

        using (var actReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/password/reset/complete")
        {
            Content = JsonContent.Create(new { email = adminEmail, token, newPassword = adminPass })
        })
        {
            actReq.Headers.Add("X-CSRF-TOKEN", csrf);
            Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(actReq)).StatusCode);
        }

        using (var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new { email = adminEmail, password = adminPass })
        })
        {
            loginReq.Headers.Add("X-CSRF-TOKEN", csrf);
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(loginReq)).StatusCode);
        }

        csrf = await FetchCsrfAsync(client);
        return (adminId, client, csrf);
    }

    private static async Task<(Guid UserId, HttpClient Client, string Csrf)> CreateActivateAndLoginUserAsync(
        WebApplicationFactory<Program> factory,
        CapturingDeliverySpy spy,
        HttpClient adminClient,
        string adminCsrf,
        string fullName,
        string email,
        IReadOnlyCollection<Guid> roleIds)
    {
        Guid userId;
        using (var createReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/users")
        {
            Content = JsonContent.Create(new { fullName, email, roleIds, triggerActivationToken = true })
        })
        {
            createReq.Headers.Add("X-CSRF-TOKEN", adminCsrf);
            using var res = await adminClient.SendAsync(createReq);
            Assert.Equal(HttpStatusCode.Created, res.StatusCode);
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
            userId = doc.RootElement.GetProperty("id").GetGuid();
        }

        var client = CreateCookieClient(factory);
        var csrf = await FetchCsrfAsync(client);
        var token = spy.Deliveries.Last(d => d.TargetUserId == userId).SingleUseToken;
        const string pass = "IntDevS07Pass!2026#";

        using (var actReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/password/reset/complete")
        {
            Content = JsonContent.Create(new { email, token, newPassword = pass })
        })
        {
            actReq.Headers.Add("X-CSRF-TOKEN", csrf);
            Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(actReq)).StatusCode);
        }

        using (var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new { email, password = pass })
        })
        {
            loginReq.Headers.Add("X-CSRF-TOKEN", csrf);
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(loginReq)).StatusCode);
        }

        csrf = await FetchCsrfAsync(client);
        return (userId, client, csrf);
    }

    [Fact]
    public async Task Integrations_D05AndD06AndU01_EnforcesDeveloperScopeSeparationMultiTabDetailsAntiIdorAndLocalization()
    {
        await ResetIsolatedDatabaseAsync();
        var (factory, spy, dpDir) = CreateFactory();

        try
        {
            var (adminId, adminClient, adminCsrf) = await BootstrapAndLoginAdminAsync(factory, spy);

            async Task<(HttpStatusCode Status, JsonDocument? Doc)> SendAsync(
                HttpClient client,
                string csrf,
                HttpMethod method,
                string url,
                object? payload = null,
                string? acceptLanguage = null)
            {
                using var req = new HttpRequestMessage(method, url);
                if (payload is not null) req.Content = JsonContent.Create(payload);
                if (method != HttpMethod.Get) req.Headers.Add("X-CSRF-TOKEN", csrf);
                if (!string.IsNullOrWhiteSpace(acceptLanguage)) req.Headers.Add("Accept-Language", acceptLanguage);
                using var res = await client.SendAsync(req);
                var raw = await res.Content.ReadAsStringAsync();
                JsonDocument? doc = string.IsNullOrWhiteSpace(raw) ? null : JsonDocument.Parse(raw);
                return (res.StatusCode, doc);
            }

            // 1. Setup dependencies + document + 1:1 integration
            var extProvId = (await SendAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/external-providers",
                new { name = "PartnerGatewayS07" })).Doc!.RootElement.GetProperty("id").GetGuid();
            var catId = (await SendAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/consumer-categories",
                new { name = "شركات الدفع" })).Doc!.RootElement.GetProperty("id").GetGuid();
            var consId = (await SendAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/consumers",
                new { categoryId = catId, name = "PayPartner" })).Doc!.RootElement.GetProperty("id").GetGuid();
            var groupId = (await SendAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/api-groups",
                new { name = "TransfersAPI" })).Doc!.RootElement.GetProperty("id").GetGuid();

            var ftId = (await SendAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/file-types",
                new { catalogCode = "OPENAPI_SPEC", displayName = "SwaggerSpec", maxSizeBytes = 5 * 1024 * 1024 }))
                .Doc!.RootElement.GetProperty("id").GetGuid();
            var tplId = (await SendAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/requirement-templates",
                new { name = "S07ReqTpl", rules = new[] { new { fileTypeId = ftId, requirementMode = "REQUIRED", minCount = 1, maxCount = 2 } } }))
                .Doc!.RootElement.GetProperty("id").GetGuid();

            var roleId = (await SendAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/roles",
                new { name = "IntegrationViewerRole", isPinned = false })).Doc!.RootElement.GetProperty("id").GetGuid();
            var routeId = (await SendAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/approval-routes",
                new { name = "S07Route", stages = new[] { new { position = 1, stageName = "Architecture Review", responsibleRoleId = roleId } } }))
                .Doc!.RootElement.GetProperty("id").GetGuid();
            var changeTypeId = (await SendAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/change-types",
                new { name = "SchemaRevision", routeId })).Doc!.RootElement.GetProperty("id").GetGuid();
            var policyId = (await SendAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/change-policies",
                new { name = "S07Policy", changeTypeIds = new[] { changeTypeId } })).Doc!.RootElement.GetProperty("id").GetGuid();

            var docId = (await SendAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/api-documents", new
            {
                providerExternalSystemId = extProvId,
                consumerEntityId = consId,
                apiGroupId = groupId,
                requirementTemplateId = tplId,
                approvalRouteId = routeId,
                changePolicyId = policyId,
                publicationOption = "MANUAL",
                state = "ACTIVE"
            })).Doc!.RootElement.GetProperty("id").GetGuid();

            var (_, intDoc) = await SendAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/integrations", new
            {
                documentId = docId,
                referenceCode = "INT-S07-001"
            });
            var integrationId = intDoc!.RootElement.GetProperty("id").GetGuid();

            // Grant IntegrationViewerRole global integrations.view + api_documents.view ONLY
            var permMap = (await SendAsync(adminClient, adminCsrf, HttpMethod.Get, "/api/v1/permissions"))
                .Doc!.RootElement.GetProperty("permissions").EnumerateArray()
                .ToDictionary(p => p.GetProperty("code").GetString()!, p => p.GetProperty("id").GetGuid(), StringComparer.OrdinalIgnoreCase);

            await SendAsync(adminClient, adminCsrf, HttpMethod.Put, $"/api/v1/roles/{roleId}/permissions", new
            {
                permissionIds = new[] { permMap["integrations.view"], permMap["api_documents.view"] }
            });

            var (devUserId, devClient, devCsrf) = await CreateActivateAndLoginUserAsync(
                factory, spy, adminClient, adminCsrf, "مطور التكامل D05", $"dev-s07-{Guid.NewGuid():N}@api-governance.local", [roleId]);

            var (_, outsiderClient, outsiderCsrf) = await CreateActivateAndLoginUserAsync(
                factory, spy, adminClient, adminCsrf, "مستخدم خارج النطاق", $"out-s07-{Guid.NewGuid():N}@api-governance.local", [roleId]);

            // 2. D05 Assign devUser to integration_developers
            var (putDevCode, putDevDoc) = await SendAsync(adminClient, adminCsrf, HttpMethod.Put,
                $"/api/v1/integrations/{integrationId}/developers",
                new { developerUserIds = new[] { devUserId } });
            Assert.Equal(HttpStatusCode.OK, putDevCode);
            var assignedEntry = putDevDoc!.RootElement.GetProperty("integrationDevelopers").EnumerateArray().Single();
            Assert.Equal(devUserId, assignedEntry.GetProperty("userId").GetGuid());

            // Strict separation: developer assignment alone does NOT grant doc.view ACL!
            Assert.False(assignedEntry.GetProperty("hasDocView").GetBoolean());
            Assert.False(assignedEntry.GetProperty("hasDocFilesManage").GetBoolean());
            Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(devClient, devCsrf, HttpMethod.Get, $"/api/v1/integrations/{integrationId}")).Status);

            // 3. Grant ONLY doc.view on the document to devUser (still without doc.assign or integrations.assign_developer)
            var creatorAndDevAcl = ApiGovernance.Shared.Security.DocumentPermissionCatalog.All
                .Select(p => new { userId = adminId, permissionCode = p.Code, decision = "GRANT" })
                .Concat([new { userId = devUserId, permissionCode = "doc.view", decision = "GRANT" }])
                .ToList();

            await SendAsync(adminClient, adminCsrf, HttpMethod.Put, $"/api/v1/api-documents/{docId}/access", new
            {
                rolePermissions = Array.Empty<object>(),
                userPermissions = creatorAndDevAcl
            });

            // devUser sees integration in assignedToMe=true list and full D06 multi-tab details!
            var (listCode, listDoc) = await SendAsync(devClient, devCsrf, HttpMethod.Get, "/api/v1/integrations?assignedToMe=true");
            Assert.Equal(HttpStatusCode.OK, listCode);
            Assert.Equal(1, listDoc!.RootElement.GetProperty("totalCount").GetInt32());
            Assert.True(listDoc.RootElement.GetProperty("items")[0].GetProperty("isCurrentUserAssignedDeveloper").GetBoolean());

            var (detCode, detDoc) = await SendAsync(devClient, devCsrf, HttpMethod.Get, $"/api/v1/integrations/{integrationId}");
            Assert.Equal(HttpStatusCode.OK, detCode);
            Assert.Equal("INT-S07-001", detDoc!.RootElement.GetProperty("summary").GetProperty("referenceCode").GetString());
            Assert.Equal(1, detDoc.RootElement.GetProperty("requirementRules").GetArrayLength());
            Assert.Equal(1, detDoc.RootElement.GetProperty("approvalStages").GetArrayLength());
            Assert.Equal(1, detDoc.RootElement.GetProperty("allowedChangeTypes").GetArrayLength());
            Assert.True(detDoc.RootElement.GetProperty("recentAuditEvents").GetArrayLength() >= 2);

            // devUser cannot modify integration developers (403 Forbidden)
            var (devMutateDevs, _) = await SendAsync(devClient, devCsrf, HttpMethod.Put,
                $"/api/v1/integrations/{integrationId}/developers",
                new { developerUserIds = new[] { devUserId } });
            Assert.Equal(HttpStatusCode.Forbidden, devMutateDevs);

            // outsiderClient without doc.view gets 404 anti-IDOR
            Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(outsiderClient, outsiderCsrf, HttpMethod.Get, $"/api/v1/integrations/{integrationId}")).Status);
            Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(outsiderClient, outsiderCsrf, HttpMethod.Get, $"/api/v1/integrations/{integrationId}/developers")).Status);

            // 4. U01 Localization verification: Accept-Language ar vs en on ProblemDetails
            var (arErrCode, arErrDoc) = await SendAsync(adminClient, adminCsrf, HttpMethod.Put,
                $"/api/v1/integrations/{integrationId}/developers",
                new { developerUserIds = new[] { Guid.NewGuid() } },
                acceptLanguage: "ar");
            Assert.Equal(HttpStatusCode.UnprocessableEntity, arErrCode);
            Assert.Contains("المطورين", arErrDoc!.RootElement.GetProperty("message").GetString());

            var (enErrCode, enErrDoc) = await SendAsync(adminClient, adminCsrf, HttpMethod.Put,
                $"/api/v1/integrations/{integrationId}/developers",
                new { developerUserIds = new[] { Guid.NewGuid() } },
                acceptLanguage: "en");
            Assert.Equal(HttpStatusCode.UnprocessableEntity, enErrCode);
            Assert.Contains("developers", enErrDoc!.RootElement.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);

            // 5. Verify OP-01 audit event
            await using var scope = factory.Services.CreateAsyncScope();
            var verifyDb = scope.ServiceProvider.GetRequiredService<ApiGovernanceDbContext>();
            var ops = await verifyDb.AuditEvents
                .Where(a => a.ResourceId == integrationId)
                .Select(a => a.Operation)
                .ToListAsync();
            Assert.Contains("INTEGRATION_CREATED", ops);
            Assert.Contains("INTEGRATION_DEVELOPERS_REPLACED", ops);
        }
        finally
        {
            await factory.DisposeAsync();
            if (Directory.Exists(dpDir)) Directory.Delete(dpDir, recursive: true);
        }
    }
}

