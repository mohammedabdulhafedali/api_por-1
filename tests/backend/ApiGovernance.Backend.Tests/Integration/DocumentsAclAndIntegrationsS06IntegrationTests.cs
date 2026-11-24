using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ApiGovernance.Modules.IdentityAccess.Contracts;
using ApiGovernance.Modules.IdentityAccess.Services;
using ApiGovernance.Shared.Persistence;
using ApiGovernance.Shared.Persistence.Entities;
using ApiGovernance.Shared.Security;
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
public sealed class DocumentsAclAndIntegrationsS06IntegrationTests
{
    private sealed class CapturingDeliverySpy : IAuthTokenDeliveryChannel
    {
        public string ChannelCode => "TEST_S06_SPY";
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
        const string adminPass = "Bootstr@pAdminS06!2026";
        var adminEmail = $"sysadmin-s06-{Guid.NewGuid():N}@api-governance.local";

        Guid adminId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var bootstrap = scope.ServiceProvider.GetRequiredService<IInitialAdminBootstrapService>();
            var res = await bootstrap.BootstrapFirstAdminAsync(new InitialAdminBootstrapRequest(adminEmail, "مدير النظام S06"));
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
        const string pass = "DocUserS06Pass!2026#";

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
    public async Task DocumentsAndIntegrations_D01ToD04AndI07_EnforcesDerivedTitleSameSystemBlockDefaultRestrictedAntiIdorAdr01And1To1LinkProtection()
    {
        await ResetIsolatedDatabaseAsync();
        var (factory, spy, dpDir) = CreateFactory();

        try
        {
            var (_, adminClient, adminCsrf) = await BootstrapAndLoginAdminAsync(factory, spy);

            async Task<(HttpStatusCode Status, JsonDocument? Doc)> SendAsync(
                HttpClient client,
                string csrf,
                HttpMethod method,
                string url,
                object? payload = null)
            {
                using var req = new HttpRequestMessage(method, url);
                if (payload is not null) req.Content = JsonContent.Create(payload);
                if (method != HttpMethod.Get) req.Headers.Add("X-CSRF-TOKEN", csrf);
                using var res = await client.SendAsync(req);
                var raw = await res.Content.ReadAsStringAsync();
                JsonDocument? doc = string.IsNullOrWhiteSpace(raw) ? null : JsonDocument.Parse(raw);
                return (res.StatusCode, doc);
            }

            // 1. Seed S04/S05 master data & config entities
            var billingId = (await SendAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/company-systems",
                new { name = "BillingCore", description = "Billing" })).Doc!.RootElement.GetProperty("id").GetGuid();
            var (_, ipDoc) = await SendAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/internal-providers",
                new { companySystemId = billingId });
            Assert.NotNull(ipDoc);

            var catId = (await SendAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/consumer-categories",
                new { name = "البنوك" })).Doc!.RootElement.GetProperty("id").GetGuid();
            var consumerId = (await SendAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/consumers",
                new { categoryId = catId, name = "مصرف تجريبي أ" })).Doc!.RootElement.GetProperty("id").GetGuid();

            var groupId = (await SendAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/api-groups",
                new { name = "المدفوعات" })).Doc!.RootElement.GetProperty("id").GetGuid();
            var group2Id = (await SendAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/api-groups",
                new { name = "التسويات" })).Doc!.RootElement.GetProperty("id").GetGuid();

            var ftId = (await SendAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/file-types",
                new { catalogCode = "OPENAPI_SPEC", displayName = "OpenAPI Spec", maxSizeBytes = 5 * 1024 * 1024 })).Doc!.RootElement.GetProperty("id").GetGuid();
            var tplId = (await SendAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/requirement-templates",
                new { name = "StandardTemplate", rules = new[] { new { fileTypeId = ftId, requirementMode = "REQUIRED", minCount = 1, maxCount = 1 } } }))
                .Doc!.RootElement.GetProperty("id").GetGuid();

            var reviewerRoleId = (await SendAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/roles",
                new { name = "DocGrantRole", isPinned = false })).Doc!.RootElement.GetProperty("id").GetGuid();
            var restrictRoleId = (await SendAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/roles",
                new { name = "DocRestrictRole", isPinned = false })).Doc!.RootElement.GetProperty("id").GetGuid();

            var routeId = (await SendAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/approval-routes",
                new { name = "DocRoute", stages = new[] { new { position = 1, stageName = "Review", responsibleRoleId = reviewerRoleId } } }))
                .Doc!.RootElement.GetProperty("id").GetGuid();
            var changeTypeId = (await SendAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/change-types",
                new { name = "SpecUpdate", routeId })).Doc!.RootElement.GetProperty("id").GetGuid();
            var changePolicyId = (await SendAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/change-policies",
                new { name = "StandardChangePolicy", changeTypeIds = new[] { changeTypeId } })).Doc!.RootElement.GetProperty("id").GetGuid();

            // Fetch global permissions catalog and grant global document/integration screen permissions to DocGrantRole
            var (catCode, catDoc) = await SendAsync(adminClient, adminCsrf, HttpMethod.Get, "/api/v1/permissions");
            Assert.Equal(HttpStatusCode.OK, catCode);
            var permMap = catDoc!.RootElement.GetProperty("permissions").EnumerateArray()
                .ToDictionary(p => p.GetProperty("code").GetString()!, p => p.GetProperty("id").GetGuid(), StringComparer.OrdinalIgnoreCase);

            await SendAsync(adminClient, adminCsrf, HttpMethod.Put, $"/api/v1/roles/{reviewerRoleId}/permissions", new
            {
                permissionIds = new[]
                {
                    permMap["api_documents.view"],
                    permMap["api_documents.edit"],
                    permMap["integrations.view"],
                    permMap["integrations.create"]
                }
            });

            // Create two test users: devUser (assigned to doc1, holds BOTH DocGrantRole + DocRestrictRole) and otherAdmin (same global perms, no doc ACL)
            var (devUserId, devClient, devCsrf) = await CreateActivateAndLoginUserAsync(
                factory, spy, adminClient, adminCsrf, "مطور التكامل", $"dev-{Guid.NewGuid():N}@api-governance.local",
                [reviewerRoleId, restrictRoleId]);

            var (_, otherClient, otherCsrf) = await CreateActivateAndLoginUserAsync(
                factory, spy, adminClient, adminCsrf, "مراجع بدون إذن وثيقة", $"other-{Guid.NewGuid():N}@api-governance.local",
                [reviewerRoleId]);

            // 2. D01 validation: reject same internal company system as both provider and consumer
            var (samePartyCode, _) = await SendAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/api-documents", new
            {
                providerInternalSystemId = billingId,
                consumerCompanySystemId = billingId,
                apiGroupId = groupId,
                requirementTemplateId = tplId,
                approvalRouteId = routeId,
                changePolicyId,
                publicationOption = "MANUAL",
                state = "ACTIVE"
            });
            Assert.Equal(HttpStatusCode.BadRequest, samePartyCode);

            // 3. D01 & D02: Create document 1 with derived title and assigned devUser
            var (doc1CreateCode, doc1CreateDoc) = await SendAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/api-documents", new
            {
                providerInternalSystemId = billingId,
                consumerEntityId = consumerId,
                apiGroupId = groupId,
                requirementTemplateId = tplId,
                approvalRouteId = routeId,
                changePolicyId,
                publicationOption = "MANUAL",
                state = "ACTIVE",
                developerUserIds = new[] { devUserId }
            });
            Assert.Equal(HttpStatusCode.Created, doc1CreateCode);
            var doc1Id = doc1CreateDoc!.RootElement.GetProperty("id").GetGuid();
            Assert.Equal("BillingCore ← مصرف تجريبي أ", doc1CreateDoc.RootElement.GetProperty("derivedTitle").GetString());
            Assert.Equal(1, doc1CreateDoc.RootElement.GetProperty("assignedDevelopers").GetArrayLength());

            // Create document 2 (for conditional delete test later)
            var (doc2CreateCode, doc2CreateDoc) = await SendAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/api-documents", new
            {
                providerInternalSystemId = billingId,
                consumerEntityId = consumerId,
                apiGroupId = group2Id,
                requirementTemplateId = tplId,
                approvalRouteId = routeId,
                changePolicyId,
                publicationOption = "MANUAL",
                state = "DRAFT"
            });
            Assert.Equal(HttpStatusCode.Created, doc2CreateCode);
            var doc2Id = doc2CreateDoc!.RootElement.GetProperty("id").GetGuid();

            // 4. Default-Restricted & Separation Guarantee & Anti-IDOR checks:
            // Assigned developer devUser has NO Document ACL grants -> hidden from list, 404 by direct UUID
            var (_, devListDoc) = await SendAsync(devClient, devCsrf, HttpMethod.Get, "/api/v1/api-documents");
            Assert.Equal(0, devListDoc!.RootElement.GetProperty("totalCount").GetInt32());

            var (devDirectCode, _) = await SendAsync(devClient, devCsrf, HttpMethod.Get, $"/api/v1/api-documents/{doc1Id}");
            Assert.Equal(HttpStatusCode.NotFound, devDirectCode);

            var (otherDirectCode, _) = await SendAsync(otherClient, otherCsrf, HttpMethod.Get, $"/api/v1/api-documents/{doc1Id}");
            Assert.Equal(HttpStatusCode.NotFound, otherDirectCode);

            // Developer cannot create integration while lacking doc.view / doc.manage_integration (404 anti-IDOR)
            var (devCreateIntBlocked, _) = await SendAsync(devClient, devCsrf, HttpMethod.Post, "/api/v1/integrations", new { documentId = doc1Id });
            Assert.Equal(HttpStatusCode.NotFound, devCreateIntBlocked);

            // 5. I07 & U02 Matrix + ADR-01 Role REVOKE > Role GRANT conflict + User GRANT override:
            var (matrixCode, matrixDoc) = await SendAsync(adminClient, adminCsrf, HttpMethod.Get, $"/api/v1/api-documents/{doc1Id}/access");
            Assert.Equal(HttpStatusCode.OK, matrixCode);
            Assert.Equal(DocumentPermissionCatalog.All.Count, matrixDoc!.RootElement.GetProperty("permissions").GetArrayLength());

            // Preserve admin access while testing DocGrantRole (GRANT doc.view) vs DocRestrictRole (REVOKE doc.view) -> REVOKE wins (A06)
            var adminId = matrixDoc.RootElement.GetProperty("createdBy").GetGuid();
            var creatorAcl = DocumentPermissionCatalog.All.Select(p => new { userId = adminId, permissionCode = p.Code, decision = "GRANT" }).ToList();

            var (conflictSaveCode, conflictSaveDoc) = await SendAsync(adminClient, adminCsrf, HttpMethod.Put, $"/api/v1/api-documents/{doc1Id}/access", new
            {
                rolePermissions = new[]
                {
                    new { roleId = reviewerRoleId, permissionCode = "doc.view", decision = "GRANT" },
                    new { roleId = restrictRoleId, permissionCode = "doc.view", decision = "REVOKE" }
                },
                userPermissions = creatorAcl
            });
            Assert.Equal(HttpStatusCode.OK, conflictSaveCode);

            var devMatrixRow = conflictSaveDoc!.RootElement.GetProperty("users").EnumerateArray()
                .First(u => u.GetProperty("userId").GetGuid() == devUserId);
            Assert.True(devMatrixRow.GetProperty("isAssignedDeveloper").GetBoolean());
            Assert.False(devMatrixRow.GetProperty("cells").GetProperty("doc.view").GetProperty("allowed").GetBoolean());
            Assert.Equal("ROLE_REVOKE_CONFLICT", devMatrixRow.GetProperty("cells").GetProperty("doc.view").GetProperty("resolutionSource").GetString());

            // devUser still denied (404) due to Role REVOKE conflict (A06)
            Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(devClient, devCsrf, HttpMethod.Get, $"/api/v1/api-documents/{doc1Id}")).Status);

            // Now add explicit User GRANT on doc.view for devUser -> overrides Role REVOKE conflict (A07)!
            var withDevViewGrant = creatorAcl.Concat([new { userId = devUserId, permissionCode = "doc.view", decision = "GRANT" }]).ToList();
            var (overrideSaveCode, _) = await SendAsync(adminClient, adminCsrf, HttpMethod.Put, $"/api/v1/api-documents/{doc1Id}/access", new
            {
                rolePermissions = new[]
                {
                    new { roleId = reviewerRoleId, permissionCode = "doc.view", decision = "GRANT" },
                    new { roleId = restrictRoleId, permissionCode = "doc.view", decision = "REVOKE" }
                },
                userPermissions = withDevViewGrant
            });
            Assert.Equal(HttpStatusCode.OK, overrideSaveCode);

            // Now devUser CAN view doc1 (200 OK), but lacks doc.manage_integration -> 403 Forbidden!
            Assert.Equal(HttpStatusCode.OK, (await SendAsync(devClient, devCsrf, HttpMethod.Get, $"/api/v1/api-documents/{doc1Id}")).Status);
            var (devIntForbidden, _) = await SendAsync(devClient, devCsrf, HttpMethod.Post, "/api/v1/integrations", new { documentId = doc1Id });
            Assert.Equal(HttpStatusCode.Forbidden, devIntForbidden);

            // 6. D04 Create 1:1 Integration + prevent duplicate 1:1 link (409) + D03 lock parties & block delete (422)
            var (intCreateCode, intCreateDoc) = await SendAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/integrations", new
            {
                documentId = doc1Id,
                developerUserIds = new[] { devUserId }
            });
            Assert.Equal(HttpStatusCode.Created, intCreateCode);
            Assert.StartsWith("INT-", intCreateDoc!.RootElement.GetProperty("referenceCode").GetString());

            // 1:1 duplicate blocked -> 409 Conflict
            var (dupIntCode, _) = await SendAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/integrations", new { documentId = doc1Id });
            Assert.Equal(HttpStatusCode.Conflict, dupIntCode);

            // Updating ApiGroup after integration link -> 422 Unprocessable Entity
            var (partyEditLocked, _) = await SendAsync(adminClient, adminCsrf, HttpMethod.Patch, $"/api/v1/api-documents/{doc1Id}", new { apiGroupId = group2Id });
            Assert.Equal(HttpStatusCode.UnprocessableEntity, partyEditLocked);

            // Updating PublicationOption succeeds (200 OK)
            var (settingsEditOk, _) = await SendAsync(adminClient, adminCsrf, HttpMethod.Patch, $"/api/v1/api-documents/{doc1Id}", new { publicationOption = "POST_APPROVAL" });
            Assert.Equal(HttpStatusCode.OK, settingsEditOk);

            // Deleting linked doc1 blocked -> 422
            var (delDoc1Blocked, _) = await SendAsync(adminClient, adminCsrf, HttpMethod.Delete, $"/api/v1/api-documents/{doc1Id}");
            Assert.Equal(HttpStatusCode.UnprocessableEntity, delDoc1Blocked);

            // Deleting unlinked doc2 succeeds -> 204
            var (delDoc2Ok, _) = await SendAsync(adminClient, adminCsrf, HttpMethod.Delete, $"/api/v1/api-documents/{doc2Id}");
            Assert.Equal(HttpStatusCode.NoContent, delDoc2Ok);

            // 7. Verify OP-01 audit events
            await using var verifyScope = factory.Services.CreateAsyncScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApiGovernanceDbContext>();
            var auditOps = await verifyDb.AuditEvents
                .Where(a => a.ResourceId == doc1Id || a.ResourceId == doc2Id)
                .Select(a => a.Operation)
                .ToListAsync();
            Assert.Contains("API_DOCUMENT_CREATED", auditOps);
            Assert.Contains("API_DOCUMENT_ACL_REPLACED", auditOps);
            Assert.Contains("API_DOCUMENT_UPDATED", auditOps);
            Assert.Contains("API_DOCUMENT_DELETED", auditOps);
        }
        finally
        {
            await factory.DisposeAsync();
            if (Directory.Exists(dpDir)) Directory.Delete(dpDir, recursive: true);
        }
    }
}

