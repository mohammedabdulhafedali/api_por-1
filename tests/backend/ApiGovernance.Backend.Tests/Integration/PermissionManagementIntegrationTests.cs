using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ApiGovernance.Modules.IdentityAccess.Contracts;
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
public sealed class PermissionManagementIntegrationTests
{
    private sealed class CapturingDeliverySpy : IAuthTokenDeliveryChannel
    {
        public string ChannelCode => "TEST_I04_SPY";
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

    private static async Task<(Guid AdminId, HttpClient AdminClient)> BootstrapActivateAndLoginAdminAsync(
        WebApplicationFactory<Program> factory,
        CapturingDeliverySpy spy)
    {
        const string adminPass = "Bootstr@pAdminI04!2026";
        var adminEmail = $"sysadmin-i04-{Guid.NewGuid():N}@api-governance.local";

        Guid adminId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var bootstrap = scope.ServiceProvider.GetRequiredService<IInitialAdminBootstrapService>();
            var bootResult = await bootstrap.BootstrapFirstAdminAsync(
                new InitialAdminBootstrapRequest(adminEmail, "مدير النظام I04"));
            Assert.True(bootResult.Created);
            adminId = bootResult.AdminUserId!.Value;
        }

        var token = spy.Deliveries.Last(d => d.TargetUserId == adminId).SingleUseToken;
        var adminClient = CreateCookieClient(factory);
        var csrf = await FetchCsrfAsync(adminClient);

        using (var activateReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/password/reset/complete")
        {
            Content = JsonContent.Create(new { email = adminEmail, token, newPassword = adminPass })
        })
        {
            activateReq.Headers.Add("X-CSRF-TOKEN", csrf);
            using var activateRes = await adminClient.SendAsync(activateReq);
            Assert.Equal(HttpStatusCode.NoContent, activateRes.StatusCode);
        }

        using (var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new { email = adminEmail, password = adminPass })
        })
        {
            loginReq.Headers.Add("X-CSRF-TOKEN", csrf);
            using var loginRes = await adminClient.SendAsync(loginReq);
            Assert.Equal(HttpStatusCode.OK, loginRes.StatusCode);
        }

        return (adminId, adminClient);
    }

    private static async Task<(Guid UserId, HttpClient Client)> CreateActivateAndSignInUserAsync(
        WebApplicationFactory<Program> factory,
        CapturingDeliverySpy spy,
        HttpClient adminClient,
        string adminCsrf,
        string fullName,
        string email,
        string password,
        params Guid[] roleIds)
    {
        Guid userId;
        using (var createReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/users")
        {
            Content = JsonContent.Create(new
            {
                fullName,
                email,
                triggerActivationToken = true,
                roleIds
            })
        })
        {
            createReq.Headers.Add("X-CSRF-TOKEN", adminCsrf);
            using var createRes = await adminClient.SendAsync(createReq);
            Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
            using var doc = JsonDocument.Parse(await createRes.Content.ReadAsStringAsync());
            userId = doc.RootElement.GetProperty("id").GetGuid();
        }

        var token = spy.Deliveries.Last(d => d.TargetUserId == userId).SingleUseToken;
        var client = CreateCookieClient(factory);
        var csrf = await FetchCsrfAsync(client);

        using (var activateReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/password/reset/complete")
        {
            Content = JsonContent.Create(new { email, token, newPassword = password })
        })
        {
            activateReq.Headers.Add("X-CSRF-TOKEN", csrf);
            Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(activateReq)).StatusCode);
        }

        using (var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new { email, password })
        })
        {
            loginReq.Headers.Add("X-CSRF-TOKEN", csrf);
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(loginReq)).StatusCode);
        }

        return (userId, client);
    }

    [Fact]
    public async Task RolePermissionMatrix_EnforcesPermissionsCsrfAndAntiEscalation_PreservesDictionary_AndAuditsChanges()
    {
        await ResetIsolatedDatabaseAsync();
        var (factory, spy, dpDir) = CreateFactory();

        try
        {
            var (_, adminClient) = await BootstrapActivateAndLoginAdminAsync(factory, spy);
            var adminCsrf = await FetchCsrfAsync(adminClient);

            // 1. Catalog requires permissions.view (401 anon) and returns complete permanent dictionary + matrix layout
            using (var anonClient = CreateCookieClient(factory))
            {
                Assert.Equal(HttpStatusCode.Unauthorized, (await anonClient.GetAsync("/api/v1/permissions")).StatusCode);
            }

            Dictionary<string, Guid> permMap;
            int initialDictCount;
            using (var catRes = await adminClient.GetAsync("/api/v1/permissions"))
            {
                Assert.Equal(HttpStatusCode.OK, catRes.StatusCode);
                using var catDoc = JsonDocument.Parse(await catRes.Content.ReadAsStringAsync());
                var permsArray = catDoc.RootElement.GetProperty("permissions");
                initialDictCount = permsArray.GetArrayLength();
                Assert.True(initialDictCount >= PlatformPermissionCatalog.CanonicalEntries.Count);
                Assert.Equal(PlatformPermissionCatalog.ActionColumns.Count, catDoc.RootElement.GetProperty("columns").GetArrayLength());
                Assert.Equal(PlatformPermissionCatalog.ScreenRows.Count, catDoc.RootElement.GetProperty("screens").GetArrayLength());

                permMap = permsArray.EnumerateArray().ToDictionary(
                    e => e.GetProperty("code").GetString()!,
                    e => e.GetProperty("id").GetGuid(),
                    StringComparer.OrdinalIgnoreCase);
            }

            // 2. Create two roles: TargetReviewer and DelegatedPermManager (without company_systems.create!)
            async Task<Guid> CreateRoleAsync(string name)
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/roles")
                {
                    Content = JsonContent.Create(new { name, isPinned = false })
                };
                req.Headers.Add("X-CSRF-TOKEN", adminCsrf);
                using var res = await adminClient.SendAsync(req);
                Assert.Equal(HttpStatusCode.Created, res.StatusCode);
                using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
                return doc.RootElement.GetProperty("id").GetGuid();
            }

            var targetRoleId = await CreateRoleAsync("MatrixTargetRole");
            var delegatedRoleId = await CreateRoleAsync("DelegatedPermManager");

            // Grant permissions to DelegatedPermManager: ONLY permissions.view, permissions.manage, users.view
            using (var setDelegatedReq = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/roles/{delegatedRoleId}/permissions")
            {
                Content = JsonContent.Create(new
                {
                    permissionIds = new[]
                    {
                        permMap[UserPermissionCodes.PermissionsView],
                        permMap[UserPermissionCodes.PermissionsManage],
                        permMap[UserPermissionCodes.UsersView]
                    }
                })
            })
            {
                setDelegatedReq.Headers.Add("X-CSRF-TOKEN", adminCsrf);
                Assert.Equal(HttpStatusCode.OK, (await adminClient.SendAsync(setDelegatedReq)).StatusCode);
            }

            var (_, delegatedClient) = await CreateActivateAndSignInUserAsync(
                factory,
                spy,
                adminClient,
                adminCsrf,
                "مفوض صلاحيات محدود",
                $"delegated-i04-{Guid.NewGuid():N}@api-governance.local",
                "DelegatedManager!2026#",
                delegatedRoleId);
            var delegatedCsrf = await FetchCsrfAsync(delegatedClient);

            // 3. CSRF required on PUT /api/v1/roles/{roleId}/permissions -> 403
            using (var noCsrfReq = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/roles/{targetRoleId}/permissions")
            {
                Content = JsonContent.Create(new { permissionIds = new[] { permMap["dashboard.view"] } })
            })
            {
                Assert.Equal(HttpStatusCode.Forbidden, (await adminClient.SendAsync(noCsrfReq)).StatusCode);
            }

            // 4. Full admin grants dashboard.view + users.view to MatrixTargetRole, then removes users.view
            using (var grantReq = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/roles/{targetRoleId}/permissions")
            {
                Content = JsonContent.Create(new { permissionIds = new[] { permMap["dashboard.view"], permMap[UserPermissionCodes.UsersView] } })
            })
            {
                grantReq.Headers.Add("X-CSRF-TOKEN", adminCsrf);
                using var res = await adminClient.SendAsync(grantReq);
                Assert.Equal(HttpStatusCode.OK, res.StatusCode);
                using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
                Assert.Equal(2, doc.RootElement.GetProperty("permissionIds").GetArrayLength());
            }

            using (var shrinkReq = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/roles/{targetRoleId}/permissions")
            {
                Content = JsonContent.Create(new { permissionIds = new[] { permMap["dashboard.view"] } })
            })
            {
                shrinkReq.Headers.Add("X-CSRF-TOKEN", adminCsrf);
                Assert.Equal(HttpStatusCode.OK, (await adminClient.SendAsync(shrinkReq)).StatusCode);
            }

            // 5. Anti-privilege-escalation: DelegatedPermManager CANNOT grant 'company_systems.create' (which they do NOT hold!)
            using (var escalateReq = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/roles/{targetRoleId}/permissions")
            {
                Content = JsonContent.Create(new
                {
                    permissionIds = new[]
                    {
                        permMap["dashboard.view"],
                        permMap["company_systems.create"]
                    }
                })
            })
            {
                escalateReq.Headers.Add("X-CSRF-TOKEN", delegatedCsrf);
                Assert.Equal(HttpStatusCode.Forbidden, (await delegatedClient.SendAsync(escalateReq)).StatusCode);
            }

            // DelegatedPermManager CAN grant 'users.view' (which they DO hold!)
            using (var validDelegatedReq = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/roles/{targetRoleId}/permissions")
            {
                Content = JsonContent.Create(new
                {
                    permissionIds = new[]
                    {
                        permMap["dashboard.view"],
                        permMap[UserPermissionCodes.UsersView]
                    }
                })
            })
            {
                validDelegatedReq.Headers.Add("X-CSRF-TOKEN", delegatedCsrf);
                Assert.Equal(HttpStatusCode.OK, (await delegatedClient.SendAsync(validDelegatedReq)).StatusCode);
            }

            // 6. Last active admin capability guard: stripping SystemAdmin permissions down to empty -> 422
            Guid systemAdminRoleId;
            using (var listRolesRes = await adminClient.GetAsync("/api/v1/roles?search=SystemAdmin"))
            {
                Assert.Equal(HttpStatusCode.OK, listRolesRes.StatusCode);
                using var rolesDoc = JsonDocument.Parse(await listRolesRes.Content.ReadAsStringAsync());
                systemAdminRoleId = rolesDoc.RootElement.GetProperty("items")[0].GetProperty("id").GetGuid();
            }

            using (var stripSysAdminReq = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/roles/{systemAdminRoleId}/permissions")
            {
                Content = JsonContent.Create(new { permissionIds = Array.Empty<Guid>() })
            })
            {
                stripSysAdminReq.Headers.Add("X-CSRF-TOKEN", adminCsrf);
                Assert.Equal(HttpStatusCode.UnprocessableEntity, (await adminClient.SendAsync(stripSysAdminReq)).StatusCode);
            }

            // 7. System permission dictionary count MUST NEVER shrink, and OP-01 audit events must exist
            await using var verifyScope = factory.Services.CreateAsyncScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApiGovernanceDbContext>();

            var finalDictCount = await verifyDb.Permissions.CountAsync();
            Assert.Equal(initialDictCount, finalDictCount);

            var operations = await verifyDb.AuditEvents
                .Where(a => a.ResourceId == targetRoleId)
                .Select(a => a.Operation)
                .ToListAsync();
            Assert.Contains("ROLE_PERMISSIONS_UPDATED", operations);
            Assert.Contains("ROLE_PERMISSIONS_ESCALATION_BLOCKED", operations);
        }
        finally
        {
            await factory.DisposeAsync();
            if (Directory.Exists(dpDir)) Directory.Delete(dpDir, recursive: true);
        }
    }

    [Fact]
    public async Task UserIndividualOverrides_EnforcesAdr01Precedence_AntiEscalation_RevertToInherited_AndAuditsChanges()
    {
        await ResetIsolatedDatabaseAsync();
        var (factory, spy, dpDir) = CreateFactory();

        try
        {
            var (_, adminClient) = await BootstrapActivateAndLoginAdminAsync(factory, spy);
            var adminCsrf = await FetchCsrfAsync(adminClient);

            Dictionary<string, Guid> permMap;
            using (var catRes = await adminClient.GetAsync("/api/v1/permissions"))
            {
                Assert.Equal(HttpStatusCode.OK, catRes.StatusCode);
                using var catDoc = JsonDocument.Parse(await catRes.Content.ReadAsStringAsync());
                permMap = catDoc.RootElement.GetProperty("permissions").EnumerateArray().ToDictionary(
                    e => e.GetProperty("code").GetString()!,
                    e => e.GetProperty("id").GetGuid(),
                    StringComparer.OrdinalIgnoreCase);
            }

            async Task<Guid> CreateRoleWithPermsAsync(string name, params string[] codes)
            {
                using var createReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/roles")
                {
                    Content = JsonContent.Create(new { name, isPinned = false })
                };
                createReq.Headers.Add("X-CSRF-TOKEN", adminCsrf);
                using var createRes = await adminClient.SendAsync(createReq);
                Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
                using var doc = JsonDocument.Parse(await createRes.Content.ReadAsStringAsync());
                var roleId = doc.RootElement.GetProperty("id").GetGuid();

                if (codes.Length > 0)
                {
                    using var permReq = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/roles/{roleId}/permissions")
                    {
                        Content = JsonContent.Create(new { permissionIds = codes.Select(c => permMap[c]).ToArray() })
                    };
                    permReq.Headers.Add("X-CSRF-TOKEN", adminCsrf);
                    Assert.Equal(HttpStatusCode.OK, (await adminClient.SendAsync(permReq)).StatusCode);
                }

                return roleId;
            }

            var roleAId = await CreateRoleWithPermsAsync("RoleInheritanceA", UserPermissionCodes.UsersView, "company_systems.view");
            var roleBId = await CreateRoleWithPermsAsync("RoleInheritanceB", UserPermissionCodes.RolesView);
            var delegatorRoleId = await CreateRoleWithPermsAsync(
                "LimitedOverrideAdmin",
                UserPermissionCodes.PermissionsView,
                UserPermissionCodes.PermissionsManage,
                UserPermissionCodes.UsersView);

            var (targetUserId, targetClient) = await CreateActivateAndSignInUserAsync(
                factory,
                spy,
                adminClient,
                adminCsrf,
                "مستخدم استثناءات فردية",
                $"overrides-target-{Guid.NewGuid():N}@api-governance.local",
                "TargetOverrideUser!2026#",
                roleAId,
                roleBId);

            var (_, delegatorClient) = await CreateActivateAndSignInUserAsync(
                factory,
                spy,
                adminClient,
                adminCsrf,
                "مفوض استثناءات مقيد",
                $"overrides-delegator-{Guid.NewGuid():N}@api-governance.local",
                "DelegatorUser!2026#",
                delegatorRoleId);
            var delegatorCsrf = await FetchCsrfAsync(delegatorClient);

            // 1. Target user without overrides -> inherits RoleA + RoleB union, lacks providers.view & api_documents.delete
            using (var initialMatrixRes = await adminClient.GetAsync($"/api/v1/users/{targetUserId}/permissions"))
            {
                Assert.Equal(HttpStatusCode.OK, initialMatrixRes.StatusCode);
                using var doc = JsonDocument.Parse(await initialMatrixRes.Content.ReadAsStringAsync());
                var entries = doc.RootElement.GetProperty("permissions").EnumerateArray().ToDictionary(
                    e => e.GetProperty("code").GetString()!,
                    e => e);

                Assert.True(entries[UserPermissionCodes.UsersView].GetProperty("effectiveAllowed").GetBoolean());
                Assert.Equal("ROLE_INHERITED", entries[UserPermissionCodes.UsersView].GetProperty("resolutionSource").GetString());
                Assert.True(entries[UserPermissionCodes.RolesView].GetProperty("effectiveAllowed").GetBoolean());
                Assert.False(entries["providers.view"].GetProperty("effectiveAllowed").GetBoolean());
                Assert.Equal("DENIED_DEFAULT", entries["providers.view"].GetProperty("resolutionSource").GetString());
            }

            // 2. Apply User REVOKE (on inherited users.view & roles.view) and User GRANT (on non-inherited providers.view)
            using (var applyOverridesReq = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/users/{targetUserId}/permission-overrides")
            {
                Content = JsonContent.Create(new
                {
                    overrides = new[]
                    {
                        new { permissionId = permMap[UserPermissionCodes.UsersView], decision = "REVOKE" },
                        new { permissionId = permMap[UserPermissionCodes.RolesView], decision = "REVOKE" },
                        new { permissionId = permMap["providers.view"], decision = "GRANT" }
                    }
                })
            })
            {
                applyOverridesReq.Headers.Add("X-CSRF-TOKEN", adminCsrf);
                using var res = await adminClient.SendAsync(applyOverridesReq);
                Assert.Equal(HttpStatusCode.OK, res.StatusCode);
                using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
                var entries = doc.RootElement.GetProperty("permissions").EnumerateArray().ToDictionary(
                    e => e.GetProperty("code").GetString()!,
                    e => e);

                // REVOKE beats Role inheritance
                Assert.True(entries[UserPermissionCodes.UsersView].GetProperty("isInherited").GetBoolean());
                Assert.False(entries[UserPermissionCodes.UsersView].GetProperty("effectiveAllowed").GetBoolean());
                Assert.Equal("USER_REVOKE", entries[UserPermissionCodes.UsersView].GetProperty("resolutionSource").GetString());

                // GRANT beats missing role permission
                Assert.False(entries["providers.view"].GetProperty("isInherited").GetBoolean());
                Assert.True(entries["providers.view"].GetProperty("effectiveAllowed").GetBoolean());
                Assert.Equal("USER_GRANT", entries["providers.view"].GetProperty("resolutionSource").GetString());
            }

            // Live /api/v1/me + endpoint enforcement reflect User REVOKE & User GRANT immediately!
            using (var targetMeRes = await targetClient.GetAsync("/api/v1/me"))
            {
                Assert.Equal(HttpStatusCode.OK, targetMeRes.StatusCode);
                using var meDoc = JsonDocument.Parse(await targetMeRes.Content.ReadAsStringAsync());
                var mePerms = meDoc.RootElement.GetProperty("permissions").EnumerateArray().Select(e => e.GetString()!).ToList();
                Assert.DoesNotContain(UserPermissionCodes.UsersView, mePerms);
                Assert.DoesNotContain(UserPermissionCodes.RolesView, mePerms);
                Assert.Contains("providers.view", mePerms);
                Assert.Contains("company_systems.view", mePerms);
            }
            Assert.Equal(HttpStatusCode.Forbidden, (await targetClient.GetAsync("/api/v1/users")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await targetClient.GetAsync("/api/v1/roles")).StatusCode);

            // 3. Anti-escalation: LimitedOverrideAdmin cannot GRANT api_documents.delete (not held) AND cannot un-revoke roles.view (not held)!
            using (var escGrantReq = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/users/{targetUserId}/permission-overrides")
            {
                Content = JsonContent.Create(new
                {
                    overrides = new[]
                    {
                        new { permissionId = permMap[UserPermissionCodes.UsersView], decision = "REVOKE" },
                        new { permissionId = permMap[UserPermissionCodes.RolesView], decision = "REVOKE" },
                        new { permissionId = permMap["providers.view"], decision = "GRANT" },
                        new { permissionId = permMap["api_documents.delete"], decision = "GRANT" }
                    }
                })
            })
            {
                escGrantReq.Headers.Add("X-CSRF-TOKEN", delegatorCsrf);
                Assert.Equal(HttpStatusCode.Forbidden, (await delegatorClient.SendAsync(escGrantReq)).StatusCode);
            }

            using (var escUnrevokeReq = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/users/{targetUserId}/permission-overrides")
            {
                Content = JsonContent.Create(new
                {
                    overrides = new[]
                    {
                        new { permissionId = permMap[UserPermissionCodes.UsersView], decision = "REVOKE" },
                        new { permissionId = permMap["providers.view"], decision = "GRANT" }
                        // Attempting to drop REVOKE on roles.view without holding roles.view!
                    }
                })
            })
            {
                escUnrevokeReq.Headers.Add("X-CSRF-TOKEN", delegatorCsrf);
                Assert.Equal(HttpStatusCode.Forbidden, (await delegatorClient.SendAsync(escUnrevokeReq)).StatusCode);
            }

            // 4. Full admin reverts all overrides back to Inherited (empty array / INHERIT)
            using (var revertReq = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/users/{targetUserId}/permission-overrides")
            {
                Content = JsonContent.Create(new { overrides = Array.Empty<object>() })
            })
            {
                revertReq.Headers.Add("X-CSRF-TOKEN", adminCsrf);
                using var res = await adminClient.SendAsync(revertReq);
                Assert.Equal(HttpStatusCode.OK, res.StatusCode);
                using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
                var entries = doc.RootElement.GetProperty("permissions").EnumerateArray().ToDictionary(
                    e => e.GetProperty("code").GetString()!,
                    e => e);

                Assert.True(entries[UserPermissionCodes.UsersView].GetProperty("effectiveAllowed").GetBoolean());
                Assert.Equal("ROLE_INHERITED", entries[UserPermissionCodes.UsersView].GetProperty("resolutionSource").GetString());
                Assert.True(entries[UserPermissionCodes.RolesView].GetProperty("effectiveAllowed").GetBoolean());
                Assert.False(entries["providers.view"].GetProperty("effectiveAllowed").GetBoolean());
            }

            // Target user immediately regains inherited /api/v1/users access
            Assert.Equal(HttpStatusCode.OK, (await targetClient.GetAsync("/api/v1/users")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await targetClient.GetAsync("/api/v1/roles")).StatusCode);

            // Verify OP-01 audit trail & DB clean override removal
            await using var verifyScope = factory.Services.CreateAsyncScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApiGovernanceDbContext>();

            Assert.Equal(0, await verifyDb.UserPermissionOverrides.CountAsync(uo => uo.UserId == targetUserId));

            var auditOps = await verifyDb.AuditEvents
                .Where(a => a.ResourceId == targetUserId)
                .Select(a => a.Operation)
                .ToListAsync();
            Assert.Contains("USER_PERMISSION_OVERRIDES_UPDATED", auditOps);
            Assert.Contains("USER_OVERRIDES_ESCALATION_BLOCKED", auditOps);
        }
        finally
        {
            await factory.DisposeAsync();
            if (Directory.Exists(dpDir)) Directory.Delete(dpDir, recursive: true);
        }
    }
}

