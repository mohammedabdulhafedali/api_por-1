using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ApiGovernance.Modules.Configuration.Contracts;
using ApiGovernance.Modules.IdentityAccess.Services;
using ApiGovernance.Shared.Persistence;
using ApiGovernance.Shared.Persistence.Entities;
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
public sealed class MasterDataAndFileTypesIntegrationTests
{
    private sealed class CapturingDeliverySpy : IAuthTokenDeliveryChannel
    {
        public string ChannelCode => "TEST_S04_SPY";
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
        const string adminPass = "Bootstr@pAdminS04!2026";
        var adminEmail = $"sysadmin-s04-{Guid.NewGuid():N}@api-governance.local";

        Guid adminId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var bootstrap = scope.ServiceProvider.GetRequiredService<IInitialAdminBootstrapService>();
            var res = await bootstrap.BootstrapFirstAdminAsync(new InitialAdminBootstrapRequest(adminEmail, "مدير النظام S04"));
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

    [Fact]
    public async Task CompanySystemsProvidersConsumersAndGroups_EnforceComputedUsageNoDuplicatesAndReferenceProtection()
    {
        await ResetIsolatedDatabaseAsync();
        var (factory, spy, dpDir) = CreateFactory();

        try
        {
            var (adminId, adminClient, adminCsrf) = await BootstrapAndLoginAdminAsync(factory, spy);

            async Task<(HttpStatusCode Status, JsonDocument? Doc)> SendAsync(HttpMethod method, string url, object? payload = null)
            {
                using var req = new HttpRequestMessage(method, url);
                if (payload is not null) req.Content = JsonContent.Create(payload);
                if (method != HttpMethod.Get) req.Headers.Add("X-CSRF-TOKEN", adminCsrf);
                using var res = await adminClient.SendAsync(req);
                var raw = await res.Content.ReadAsStringAsync();
                JsonDocument? doc = string.IsNullOrWhiteSpace(raw) ? null : JsonDocument.Parse(raw);
                return (res.StatusCode, doc);
            }

            // 1. M01: Create two company systems -> initially NONE usage
            var (s1Code, s1Doc) = await SendAsync(HttpMethod.Post, "/api/v1/company-systems", new { name = "BillingCore", description = "Billing" });
            Assert.Equal(HttpStatusCode.Created, s1Code);
            var billingId = s1Doc!.RootElement.GetProperty("id").GetGuid();
            Assert.Equal("NONE", s1Doc.RootElement.GetProperty("computedUsageType").GetString());

            // Duplicate company system name -> 409 Conflict
            var (dupSysCode, _) = await SendAsync(HttpMethod.Post, "/api/v1/company-systems", new { name = "billingcore" });
            Assert.Equal(HttpStatusCode.Conflict, dupSysCode);

            var (s2Code, s2Doc) = await SendAsync(HttpMethod.Post, "/api/v1/company-systems", new { name = "PortalConsumerApp" });
            Assert.Equal(HttpStatusCode.Created, s2Code);
            var portalSysId = s2Doc!.RootElement.GetProperty("id").GetGuid();

            // 2. M02: Activate BillingCore as internal provider -> prevents duplicate (409); deactivating preserves CompanySystem
            var (ipCode, ipDoc) = await SendAsync(HttpMethod.Post, "/api/v1/internal-providers", new { companySystemId = billingId });
            Assert.Equal(HttpStatusCode.Created, ipCode);
            var billingProviderId = ipDoc!.RootElement.GetProperty("id").GetGuid();

            var (dupIpCode, _) = await SendAsync(HttpMethod.Post, "/api/v1/internal-providers", new { companySystemId = billingId });
            Assert.Equal(HttpStatusCode.Conflict, dupIpCode);

            // External provider has no companySystemId and enforces uniqueness
            var (epCode, epDoc) = await SendAsync(HttpMethod.Post, "/api/v1/external-providers", new { name = "BankSwitchGateway", description = "External" });
            Assert.Equal(HttpStatusCode.Created, epCode);
            var extProviderId = epDoc!.RootElement.GetProperty("id").GetGuid();

            // Deactivate BillingCore while active InternalProvider exists -> 422 reference protection!
            var (deactSysBlocked, _) = await SendAsync(HttpMethod.Patch, $"/api/v1/company-systems/{billingId}", new { isActive = false });
            Assert.Equal(HttpStatusCode.UnprocessableEntity, deactSysBlocked);

            // 3. M03: Flat consumer category & consumer entity
            var (catCode, catDoc) = await SendAsync(HttpMethod.Post, "/api/v1/consumer-categories", new { name = "Banks" });
            Assert.Equal(HttpStatusCode.Created, catCode);
            var banksCategoryId = catDoc!.RootElement.GetProperty("id").GetGuid();

            var (consCode, consDoc) = await SendAsync(HttpMethod.Post, "/api/v1/consumers", new { categoryId = banksCategoryId, name = "AlAhliBank" });
            Assert.Equal(HttpStatusCode.Created, consCode);
            var bankConsumerId = consDoc!.RootElement.GetProperty("id").GetGuid();

            // Category cannot be deactivated while active consumer belongs to it -> 422!
            var (deactCatBlocked, _) = await SendAsync(HttpMethod.Patch, $"/api/v1/consumer-categories/{banksCategoryId}", new { isActive = false });
            Assert.Equal(HttpStatusCode.UnprocessableEntity, deactCatBlocked);

            // 4. M04: API Group
            var (grpCode, grpDoc) = await SendAsync(HttpMethod.Post, "/api/v1/api-groups", new { name = "CorePayments", description = "v1" });
            Assert.Equal(HttpStatusCode.Created, grpCode);
            var apiGroupId = grpDoc!.RootElement.GetProperty("id").GetGuid();

            // 5. Seed minimal ApiDocument references in DB to verify ComputedUsageType (PROVIDER -> BOTH) & Reference Protection
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiGovernanceDbContext>();
                var tpl = new FileRequirementTemplateEntity { Id = Guid.NewGuid(), Name = "TPL-S04", IsActive = true };
                var route = new ApprovalRouteEntity { Id = Guid.NewGuid(), Name = "ROUTE-S04", IsActive = true };
                var policy = new ChangePolicyEntity { Id = Guid.NewGuid(), Name = "POLICY-S04", IsActive = true };
                db.FileRequirementTemplates.Add(tpl);
                db.ApprovalRoutes.Add(route);
                db.ChangePolicies.Add(policy);
                await db.SaveChangesAsync();

                // Doc 1: BillingCore (provider) -> PortalConsumerApp (consumer)
                db.ApiDocuments.Add(new ApiDocumentEntity
                {
                    Id = Guid.NewGuid(),
                    ProviderInternalSystemId = billingId,
                    ConsumerCompanySystemId = portalSysId,
                    ApiGroupId = apiGroupId,
                    RequirementTemplateId = tpl.Id,
                    ApprovalRouteId = route.Id,
                    ChangePolicyId = policy.Id,
                    PublicationOption = "INTERNAL",
                    CreatedBy = adminId,
                    State = "ACTIVE",
                    CreatedAt = DateTimeOffset.UtcNow
                });
                await db.SaveChangesAsync();

                // Doc 2: BankSwitchGateway (external provider) -> BillingCore (consumer) AND PortalConsumerApp -> AlAhliBank
                db.ApiDocuments.Add(new ApiDocumentEntity
                {
                    Id = Guid.NewGuid(),
                    ProviderExternalSystemId = extProviderId,
                    ConsumerCompanySystemId = billingId,
                    ApiGroupId = apiGroupId,
                    RequirementTemplateId = tpl.Id,
                    ApprovalRouteId = route.Id,
                    ChangePolicyId = policy.Id,
                    PublicationOption = "EXTERNAL",
                    CreatedBy = adminId,
                    State = "ACTIVE",
                    CreatedAt = DateTimeOffset.UtcNow
                });
                db.ApiDocuments.Add(new ApiDocumentEntity
                {
                    Id = Guid.NewGuid(),
                    ProviderExternalSystemId = extProviderId,
                    ConsumerEntityId = bankConsumerId,
                    ApiGroupId = apiGroupId,
                    RequirementTemplateId = tpl.Id,
                    ApprovalRouteId = route.Id,
                    ChangePolicyId = policy.Id,
                    PublicationOption = "EXTERNAL",
                    CreatedBy = adminId,
                    State = "ACTIVE",
                    CreatedAt = DateTimeOffset.UtcNow
                });
                await db.SaveChangesAsync();
            }

            // Verify Computed Usage Types: BillingCore = BOTH, PortalConsumerApp = CONSUMER
            var (listSysCode, listSysDoc) = await SendAsync(HttpMethod.Get, "/api/v1/company-systems?page=1&pageSize=20");
            Assert.Equal(HttpStatusCode.OK, listSysCode);
            var sysMap = listSysDoc!.RootElement.GetProperty("items").EnumerateArray()
                .ToDictionary(e => e.GetProperty("id").GetGuid(), e => e.GetProperty("computedUsageType").GetString());
            Assert.Equal("BOTH", sysMap[billingId]);
            Assert.Equal("CONSUMER", sysMap[portalSysId]);

            // Reference protection blocks deactivating linked InternalProvider, ExternalProvider, ConsumerEntity, and ApiGroup -> 422!
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await SendAsync(HttpMethod.Patch, $"/api/v1/internal-providers/{billingProviderId}", new { isActive = false })).Status);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await SendAsync(HttpMethod.Patch, $"/api/v1/external-providers/{extProviderId}", new { isActive = false })).Status);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await SendAsync(HttpMethod.Patch, $"/api/v1/consumers/{bankConsumerId}", new { isActive = false })).Status);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await SendAsync(HttpMethod.Patch, $"/api/v1/api-groups/{apiGroupId}", new { isActive = false })).Status);

            // Unused InternalProvider deactivation succeeds and preserves CompanySystem active!
            var (_, portalProvDoc) = await SendAsync(HttpMethod.Post, "/api/v1/internal-providers", new { companySystemId = portalSysId });
            var portalProvId = portalProvDoc!.RootElement.GetProperty("id").GetGuid();
            Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Patch, $"/api/v1/internal-providers/{portalProvId}", new { isActive = false })).Status);
            Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Get, $"/api/v1/company-systems?search=PortalConsumerApp")).Status);
        }
        finally
        {
            await factory.DisposeAsync();
            if (Directory.Exists(dpDir)) Directory.Delete(dpDir, recursive: true);
        }
    }

    [Fact]
    public async Task FileTypesCatalogAndCentralAuth_EnforcesExtensionVerification_WordTemplateRule_403_AndAuditEvents()
    {
        await ResetIsolatedDatabaseAsync();
        var (factory, spy, dpDir) = CreateFactory();

        try
        {
            var (_, adminClient, adminCsrf) = await BootstrapAndLoginAdminAsync(factory, spy);

            // 1. Common catalog endpoint returns approved catalog
            using (var catRes = await adminClient.GetAsync("/api/v1/file-types/catalog"))
            {
                Assert.Equal(HttpStatusCode.OK, catRes.StatusCode);
                using var catDoc = JsonDocument.Parse(await catRes.Content.ReadAsStringAsync());
                Assert.True(catDoc.RootElement.GetArrayLength() >= CommonFileTypeCatalog.Entries.Count);
            }

            // 2. Reject unapproved/dangerous extension (.exe or .pdf under OPENAPI_SPEC) -> 422
            using (var badExtReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/file-types")
            {
                Content = JsonContent.Create(new
                {
                    catalogCode = "OPENAPI_SPEC",
                    displayName = "Bad Spec",
                    maxSizeBytes = 1048576,
                    extensions = new[] { ".exe" }
                })
            })
            {
                badExtReq.Headers.Add("X-CSRF-TOKEN", adminCsrf);
                Assert.Equal(HttpStatusCode.UnprocessableEntity, (await adminClient.SendAsync(badExtReq)).StatusCode);
            }

            // 3. Reject WordTemplateObjectId on non-Word catalog type (PDF_DOC) -> 422
            using (var badWordReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/file-types")
            {
                Content = JsonContent.Create(new
                {
                    catalogCode = "PDF_DOC",
                    displayName = "Bad PDF",
                    maxSizeBytes = 1048576,
                    wordTemplateObjectId = Guid.NewGuid()
                })
            })
            {
                badWordReq.Headers.Add("X-CSRF-TOKEN", adminCsrf);
                Assert.Equal(HttpStatusCode.UnprocessableEntity, (await adminClient.SendAsync(badWordReq)).StatusCode);
            }

            // 4. Create OPENAPI_SPEC with omitted extensions -> auto-populates [.json, .yaml, .yml]
            Guid openApiId;
            using (var okSpecReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/file-types")
            {
                Content = JsonContent.Create(new
                {
                    catalogCode = "OPENAPI_SPEC",
                    displayName = "OpenAPI Spec",
                    maxSizeBytes = 10 * 1024 * 1024
                })
            })
            {
                okSpecReq.Headers.Add("X-CSRF-TOKEN", adminCsrf);
                using var res = await adminClient.SendAsync(okSpecReq);
                Assert.Equal(HttpStatusCode.Created, res.StatusCode);
                using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
                openApiId = doc.RootElement.GetProperty("id").GetGuid();
                var exts = doc.RootElement.GetProperty("extensions").EnumerateArray().Select(e => e.GetString()!).ToList();
                Assert.Contains(".yaml", exts);
                Assert.Contains(".yml", exts);
                Assert.Contains(".json", exts);
            }

            // 5. Create user with zero permissions -> Central Auth rejects S04 endpoints with 403 Forbidden
            var viewerEmail = $"no-s04-perms-{Guid.NewGuid():N}@api-governance.local";
            Guid viewerId;
            using (var createUserReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/users")
            {
                Content = JsonContent.Create(new { fullName = "بدون صلاحيات", email = viewerEmail, triggerActivationToken = true })
            })
            {
                createUserReq.Headers.Add("X-CSRF-TOKEN", adminCsrf);
                using var uRes = await adminClient.SendAsync(createUserReq);
                uRes.EnsureSuccessStatusCode();
                using var uDoc = JsonDocument.Parse(await uRes.Content.ReadAsStringAsync());
                viewerId = uDoc.RootElement.GetProperty("id").GetGuid();
            }

            var viewerClient = CreateCookieClient(factory);
            var viewerCsrf = await FetchCsrfAsync(viewerClient);
            var viewerToken = spy.Deliveries.Last(d => d.TargetUserId == viewerId).SingleUseToken;

            using (var actReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/password/reset/complete")
            {
                Content = JsonContent.Create(new { email = viewerEmail, token = viewerToken, newPassword = "ViewerNoPerm!2026#" })
            })
            {
                actReq.Headers.Add("X-CSRF-TOKEN", viewerCsrf);
                Assert.Equal(HttpStatusCode.NoContent, (await viewerClient.SendAsync(actReq)).StatusCode);
            }

            using (var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
            {
                Content = JsonContent.Create(new { email = viewerEmail, password = "ViewerNoPerm!2026#" })
            })
            {
                loginReq.Headers.Add("X-CSRF-TOKEN", viewerCsrf);
                Assert.Equal(HttpStatusCode.OK, (await viewerClient.SendAsync(loginReq)).StatusCode);
            }

            Assert.Equal(HttpStatusCode.Forbidden, (await viewerClient.GetAsync("/api/v1/company-systems")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await viewerClient.GetAsync("/api/v1/internal-providers")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await viewerClient.GetAsync("/api/v1/consumers")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await viewerClient.GetAsync("/api/v1/api-groups")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await viewerClient.GetAsync("/api/v1/file-types")).StatusCode);

            // 6. Verify OP-01 audit event recorded
            await using var verifyScope = factory.Services.CreateAsyncScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApiGovernanceDbContext>();
            var ops = await verifyDb.AuditEvents
                .Where(a => a.ResourceId == openApiId)
                .Select(a => a.Operation)
                .ToListAsync();
            Assert.Contains("FILE_TYPE_CREATED", ops);
        }
        finally
        {
            await factory.DisposeAsync();
            if (Directory.Exists(dpDir)) Directory.Delete(dpDir, recursive: true);
        }
    }
}
