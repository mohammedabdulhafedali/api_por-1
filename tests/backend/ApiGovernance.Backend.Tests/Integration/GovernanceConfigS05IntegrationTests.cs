using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
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
public sealed class GovernanceConfigS05IntegrationTests
{
    private sealed class CapturingDeliverySpy : IAuthTokenDeliveryChannel
    {
        public string ChannelCode => "TEST_S05_SPY";
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
        const string adminPass = "Bootstr@pAdminS05!2026";
        var adminEmail = $"sysadmin-s05-{Guid.NewGuid():N}@api-governance.local";

        Guid adminId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var bootstrap = scope.ServiceProvider.GetRequiredService<IInitialAdminBootstrapService>();
            var res = await bootstrap.BootstrapFirstAdminAsync(new InitialAdminBootstrapRequest(adminEmail, "مدير النظام S05"));
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
    public async Task RequirementTemplates_M05_EnforcesRulesHistoricalSnapshotsAndActiveDocProtection()
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

            // 1. Seed two active file types via M04 API
            var (ft1Code, ft1Doc) = await SendAsync(HttpMethod.Post, "/api/v1/file-types", new
            {
                catalogCode = "OPENAPI_SPEC",
                displayName = "مواصفة OpenAPI",
                maxSizeBytes = 10 * 1024 * 1024
            });
            Assert.Equal(HttpStatusCode.Created, ft1Code);
            var openApiTypeId = ft1Doc!.RootElement.GetProperty("id").GetGuid();

            var (ft2Code, ft2Doc) = await SendAsync(HttpMethod.Post, "/api/v1/file-types", new
            {
                catalogCode = "PDF_DOC",
                displayName = "مستند PDF",
                maxSizeBytes = 20 * 1024 * 1024
            });
            Assert.Equal(HttpStatusCode.Created, ft2Code);
            var pdfTypeId = ft2Doc!.RootElement.GetProperty("id").GetGuid();

            // 2. Reject invalid rule (REQUIRED with minCount = 0) -> 400
            var (badRuleCode, _) = await SendAsync(HttpMethod.Post, "/api/v1/requirement-templates", new
            {
                name = "InvalidRuleTemplate",
                rules = new[]
                {
                    new { fileTypeId = openApiTypeId, requirementMode = "REQUIRED", minCount = 0, maxCount = 1 }
                }
            });
            Assert.Equal(HttpStatusCode.BadRequest, badRuleCode);

            // 3. Create valid requirement template -> snapshotCount = 1
            var (tplCreateCode, tplCreateDoc) = await SendAsync(HttpMethod.Post, "/api/v1/requirement-templates", new
            {
                name = "قالب التوثيق القياسي",
                rules = new[]
                {
                    new { fileTypeId = openApiTypeId, requirementMode = "REQUIRED", minCount = 1, maxCount = 2 }
                }
            });
            Assert.Equal(HttpStatusCode.Created, tplCreateCode);
            var templateId = tplCreateDoc!.RootElement.GetProperty("id").GetGuid();
            Assert.Equal(1, tplCreateDoc.RootElement.GetProperty("snapshotCount").GetInt32());

            // 4. Update template rules before use -> preserves immutable snapshot #1 & records snapshot #2
            var (tplPatchCode, tplPatchDoc) = await SendAsync(HttpMethod.Patch, $"/api/v1/requirement-templates/{templateId}", new
            {
                name = "قالب التوثيق القياسي المحدّث",
                rules = new object[]
                {
                    new { fileTypeId = openApiTypeId, requirementMode = "REQUIRED", minCount = 1, maxCount = 2 },
                    new { fileTypeId = pdfTypeId, requirementMode = "OPTIONAL", minCount = 0, maxCount = 3 }
                }
            });
            Assert.Equal(HttpStatusCode.OK, tplPatchCode);
            Assert.Equal(2, tplPatchDoc!.RootElement.GetProperty("snapshotCount").GetInt32());
            Assert.Equal(2, tplPatchDoc.RootElement.GetProperty("rules").GetArrayLength());

            var (snapsCode, snapsDoc) = await SendAsync(HttpMethod.Get, $"/api/v1/requirement-templates/{templateId}/snapshots");
            Assert.Equal(HttpStatusCode.OK, snapsCode);
            var snaps = snapsDoc!.RootElement.EnumerateArray().ToList();
            Assert.Equal(2, snaps.Count);
            Assert.Equal(2, snaps[0].GetProperty("versionNumber").GetInt32());
            Assert.Equal(2, snaps[0].GetProperty("rules").GetArrayLength());
            Assert.Equal(1, snaps[1].GetProperty("versionNumber").GetInt32());
            Assert.Equal(1, snaps[1].GetProperty("rules").GetArrayLength());
            Assert.Equal("قالب التوثيق القياسي", snaps[1].GetProperty("templateName").GetString());

            // 5. Link template to active ApiDocument -> structural updates & deactivation blocked (422)
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiGovernanceDbContext>();
                var extProv = new ExternalProviderSystemEntity { Id = Guid.NewGuid(), Name = "ExtP-M05", IsActive = true };
                var cat = new ConsumerCategoryEntity { Id = Guid.NewGuid(), Name = "Cat-M05", IsActive = true };
                var cons = new ConsumerEntityRecord { Id = Guid.NewGuid(), CategoryId = cat.Id, Name = "Cons-M05", IsActive = true };
                var grp = new ApiGroupEntity { Id = Guid.NewGuid(), Name = "Grp-M05", IsActive = true };
                var role = new RoleEntity { Id = Guid.NewGuid(), Name = "ReviewerRole-M05", IsActive = true, IsPinned = false };
                var route = new ApprovalRouteEntity { Id = Guid.NewGuid(), Name = "Route-M05", IsActive = true };
                var ct = new ChangeTypeEntity { Id = Guid.NewGuid(), Name = "CT-M05", RouteId = route.Id, IsActive = true };
                var cp = new ChangePolicyEntity { Id = Guid.NewGuid(), Name = "CP-M05", IsActive = true };
                db.ExternalProviderSystems.Add(extProv);
                db.ConsumerCategories.Add(cat);
                db.ConsumerEntities.Add(cons);
                db.ApiGroups.Add(grp);
                db.Roles.Add(role);
                db.ApprovalRoutes.Add(route);
                db.ApprovalRouteStages.Add(new ApprovalRouteStageEntity { Id = Guid.NewGuid(), RouteId = route.Id, Position = 1, StageName = "S1", ResponsibleRoleId = role.Id });
                db.ChangeTypes.Add(ct);
                db.ChangePolicies.Add(cp);
                db.ChangePolicyTypes.Add(new ChangePolicyTypeEntity { Id = Guid.NewGuid(), PolicyId = cp.Id, ChangeTypeId = ct.Id });
                db.ApiDocuments.Add(new ApiDocumentEntity
                {
                    Id = Guid.NewGuid(),
                    ProviderExternalSystemId = extProv.Id,
                    ConsumerEntityId = cons.Id,
                    ApiGroupId = grp.Id,
                    RequirementTemplateId = templateId,
                    ApprovalRouteId = route.Id,
                    ChangePolicyId = cp.Id,
                    PublicationOption = "MANUAL",
                    CreatedBy = adminId,
                    State = "ACTIVE",
                    CreatedAt = DateTimeOffset.UtcNow
                });
                await db.SaveChangesAsync();
            }

            var (lockedRuleCode, _) = await SendAsync(HttpMethod.Patch, $"/api/v1/requirement-templates/{templateId}", new
            {
                name = "محاولة تعديل بعد الاستخدام"
            });
            Assert.Equal(HttpStatusCode.UnprocessableEntity, lockedRuleCode);

            var (lockedDeactCode, _) = await SendAsync(HttpMethod.Patch, $"/api/v1/requirement-templates/{templateId}", new
            {
                isActive = false
            });
            Assert.Equal(HttpStatusCode.UnprocessableEntity, lockedDeactCode);
        }
        finally
        {
            await factory.DisposeAsync();
            if (Directory.Exists(dpDir)) Directory.Delete(dpDir, recursive: true);
        }
    }

    [Fact]
    public async Task ApprovalRoutesAndChangeConfig_M06AndM07_EnforcesOrderedRoleStagesNoStageTypeOrUserInvalidRouteRejectionChangeLinksAndPermissions()
    {
        await ResetIsolatedDatabaseAsync();
        var (factory, spy, dpDir) = CreateFactory();

        try
        {
            var (_, adminClient, adminCsrf) = await BootstrapAndLoginAdminAsync(factory, spy);

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

            // 1. Create two assignable roles for M06 stages
            var (r1Code, r1Doc) = await SendAsync(HttpMethod.Post, "/api/v1/roles", new { name = "مراجع معماري", isPinned = false });
            Assert.Equal(HttpStatusCode.Created, r1Code);
            var archRoleId = r1Doc!.RootElement.GetProperty("id").GetGuid();

            var (r2Code, r2Doc) = await SendAsync(HttpMethod.Post, "/api/v1/roles", new { name = "اعتماد الأمن السيبراني", isPinned = false });
            Assert.Equal(HttpStatusCode.Created, r2Code);
            var secRoleId = r2Doc!.RootElement.GetProperty("id").GetGuid();

            // 2. Reject unapproved stageType / assignedUserId JSON fields cleanly (400 Bad Request)
            using (var rawBadStageReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/approval-routes"))
            {
                rawBadStageReq.Headers.Add("X-CSRF-TOKEN", adminCsrf);
                rawBadStageReq.Content = new StringContent(
                    JsonSerializer.Serialize(new
                    {
                        name = "Invalid Stage Props Route",
                        stages = new[]
                        {
                            new
                            {
                                position = 1,
                                stageName = "Stage 1",
                                responsibleRoleId = archRoleId,
                                stageType = "REVIEW",
                                assignedUserId = Guid.NewGuid()
                            }
                        }
                    }),
                    Encoding.UTF8,
                    "application/json");
                using var badStageRes = await adminClient.SendAsync(rawBadStageReq);
                Assert.Equal(HttpStatusCode.BadRequest, badStageRes.StatusCode);
            }

            // 3. Reject empty stage route activation (422) & non-contiguous positions (422)
            var (draftCode, draftDoc) = await SendAsync(HttpMethod.Post, "/api/v1/approval-routes", new { name = "مسار مبدئي فارغ" });
            Assert.Equal(HttpStatusCode.Created, draftCode);
            var draftRouteId = draftDoc!.RootElement.GetProperty("id").GetGuid();
            Assert.False(draftDoc.RootElement.GetProperty("isActive").GetBoolean());

            var (emptyActCode, _) = await SendAsync(HttpMethod.Patch, $"/api/v1/approval-routes/{draftRouteId}", new { isActive = true });
            Assert.Equal(HttpStatusCode.UnprocessableEntity, emptyActCode);

            var (gapCode, _) = await SendAsync(HttpMethod.Put, $"/api/v1/approval-routes/{draftRouteId}/stages", new
            {
                stages = new[]
                {
                    new { position = 1, stageName = "أولى", responsibleRoleId = archRoleId },
                    new { position = 3, stageName = "ثالثة", responsibleRoleId = secRoleId }
                }
            });
            Assert.Equal(HttpStatusCode.UnprocessableEntity, gapCode);

            // 4. Create valid 2-stage Approval Route & verify reordering via PUT /stages
            var (routeCode, routeDoc) = await SendAsync(HttpMethod.Post, "/api/v1/approval-routes", new
            {
                name = "مسار الحوكمة القياسي",
                stages = new[]
                {
                    new { position = 1, stageName = "المراجعة المعمارية", responsibleRoleId = archRoleId },
                    new { position = 2, stageName = "الاعتماد الأمني", responsibleRoleId = secRoleId }
                }
            });
            Assert.Equal(HttpStatusCode.Created, routeCode);
            var routeId = routeDoc!.RootElement.GetProperty("id").GetGuid();
            Assert.True(routeDoc.RootElement.GetProperty("isActive").GetBoolean());
            Assert.Equal(2, routeDoc.RootElement.GetProperty("stages").GetArrayLength());

            var (reorderCode, reorderDoc) = await SendAsync(HttpMethod.Put, $"/api/v1/approval-routes/{routeId}/stages", new
            {
                stages = new[]
                {
                    new { position = 1, stageName = "الاعتماد الأمني", responsibleRoleId = secRoleId },
                    new { position = 2, stageName = "المراجعة المعمارية", responsibleRoleId = archRoleId }
                }
            });
            Assert.Equal(HttpStatusCode.OK, reorderCode);
            var reorderedStages = reorderDoc!.RootElement.GetProperty("stages").EnumerateArray().ToList();
            Assert.Equal("الاعتماد الأمني", reorderedStages[0].GetProperty("stageName").GetString());
            Assert.Equal(1, reorderedStages[0].GetProperty("position").GetInt32());

            // 5. M07: Change Types & Policies + Reference Protection
            // Reject linking Change Type to empty/inactive route -> 422
            var (badCtCode, _) = await SendAsync(HttpMethod.Post, "/api/v1/change-types", new
            {
                name = "تغيير غير صالح",
                routeId = draftRouteId
            });
            Assert.Equal(HttpStatusCode.UnprocessableEntity, badCtCode);

            var (ctCode, ctDoc) = await SendAsync(HttpMethod.Post, "/api/v1/change-types", new
            {
                name = "تغيير جوهري في العقد",
                routeId
            });
            Assert.Equal(HttpStatusCode.Created, ctCode);
            var changeTypeId = ctDoc!.RootElement.GetProperty("id").GetGuid();

            var (cpCode, cpDoc) = await SendAsync(HttpMethod.Post, "/api/v1/change-policies", new
            {
                name = "سياسة التغييرات الأساسية",
                changeTypeIds = new[] { changeTypeId }
            });
            Assert.Equal(HttpStatusCode.Created, cpCode);
            var policyId = cpDoc!.RootElement.GetProperty("id").GetGuid();
            Assert.Equal(1, cpDoc.RootElement.GetProperty("changeTypes").GetArrayLength());

            // Cannot deactivate Change Type while referenced by active Change Policy
            var (blockCtDeact, _) = await SendAsync(HttpMethod.Patch, $"/api/v1/change-types/{changeTypeId}", new { isActive = false });
            Assert.Equal(HttpStatusCode.UnprocessableEntity, blockCtDeact);

            // Cannot deactivate Approval Route while referenced by active Change Type
            var (blockRouteDeact, _) = await SendAsync(HttpMethod.Patch, $"/api/v1/approval-routes/{routeId}", new { isActive = false });
            Assert.Equal(HttpStatusCode.UnprocessableEntity, blockRouteDeact);

            // 6. Central Auth 403 check for unprivileged user + OP-01 audit logs
            var unprivEmail = $"no-s05-perms-{Guid.NewGuid():N}@api-governance.local";
            Guid unprivId;
            var (uCode, uDoc) = await SendAsync(HttpMethod.Post, "/api/v1/users", new
            {
                fullName = "مستخدم بلا صلاحيات إعدادات",
                email = unprivEmail,
                triggerActivationToken = true
            });
            Assert.Equal(HttpStatusCode.Created, uCode);
            unprivId = uDoc!.RootElement.GetProperty("id").GetGuid();

            var unprivClient = CreateCookieClient(factory);
            var unprivCsrf = await FetchCsrfAsync(unprivClient);
            var resetTok = spy.Deliveries.Last(d => d.TargetUserId == unprivId).SingleUseToken;

            using (var actReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/password/reset/complete")
            {
                Content = JsonContent.Create(new { email = unprivEmail, token = resetTok, newPassword = "UnprivUserS05!2026#" })
            })
            {
                actReq.Headers.Add("X-CSRF-TOKEN", unprivCsrf);
                Assert.Equal(HttpStatusCode.NoContent, (await unprivClient.SendAsync(actReq)).StatusCode);
            }

            using (var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
            {
                Content = JsonContent.Create(new { email = unprivEmail, password = "UnprivUserS05!2026#" })
            })
            {
                loginReq.Headers.Add("X-CSRF-TOKEN", unprivCsrf);
                Assert.Equal(HttpStatusCode.OK, (await unprivClient.SendAsync(loginReq)).StatusCode);
            }

            Assert.Equal(HttpStatusCode.Forbidden, (await unprivClient.GetAsync("/api/v1/requirement-templates")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await unprivClient.GetAsync("/api/v1/approval-routes")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await unprivClient.GetAsync("/api/v1/change-types")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await unprivClient.GetAsync("/api/v1/change-policies")).StatusCode);

            await using var verifyScope = factory.Services.CreateAsyncScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApiGovernanceDbContext>();
            var auditOps = await verifyDb.AuditEvents
                .Where(a => a.ResourceId == routeId || a.ResourceId == changeTypeId || a.ResourceId == policyId)
                .Select(a => a.Operation)
                .ToListAsync();
            Assert.Contains("APPROVAL_ROUTE_CREATED", auditOps);
            Assert.Contains("APPROVAL_ROUTE_STAGES_REPLACED", auditOps);
            Assert.Contains("CHANGE_TYPE_CREATED", auditOps);
            Assert.Contains("CHANGE_POLICY_CREATED", auditOps);
        }
        finally
        {
            await factory.DisposeAsync();
            if (Directory.Exists(dpDir)) Directory.Delete(dpDir, recursive: true);
        }
    }
}

