using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ApiGovernance.Modules.IdentityAccess.Contracts;
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
public sealed class RoleManagementIntegrationTests
{
    private sealed class CapturingDeliverySpy : IAuthTokenDeliveryChannel
    {
        public string ChannelCode => "TEST_I03_SPY";
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
                        ["AuthenticationPolicy:LoginRateLimitPermitLimit"] = "100"
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
        const string adminPass = "Bootstr@pAdminI03!2026";
        var adminEmail = $"sysadmin-i03-{Guid.NewGuid():N}@api-governance.local";

        Guid adminId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var bootstrap = scope.ServiceProvider.GetRequiredService<IInitialAdminBootstrapService>();
            var bootResult = await bootstrap.BootstrapFirstAdminAsync(
                new InitialAdminBootstrapRequest(adminEmail, "مدير النظام I03"));
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

    private static async Task<(Guid UserId, HttpClient Client)> ActivateAndSignInUserAsync(
        WebApplicationFactory<Program> factory,
        CapturingDeliverySpy spy,
        Guid userId,
        string email,
        string password)
    {
        var token = spy.Deliveries.Last(d => d.TargetUserId == userId).SingleUseToken;
        var client = CreateCookieClient(factory);
        var csrf = await FetchCsrfAsync(client);

        using (var activateReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/password/reset/complete")
        {
            Content = JsonContent.Create(new { email, token, newPassword = password })
        })
        {
            activateReq.Headers.Add("X-CSRF-TOKEN", csrf);
            using var activateRes = await client.SendAsync(activateReq);
            Assert.Equal(HttpStatusCode.NoContent, activateRes.StatusCode);
        }

        using (var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new { email, password })
        })
        {
            loginReq.Headers.Add("X-CSRF-TOKEN", csrf);
            using var loginRes = await client.SendAsync(loginReq);
            Assert.Equal(HttpStatusCode.OK, loginRes.StatusCode);
        }

        return (userId, client);
    }

    [Fact]
    public async Task RolesCrud_EnforcesPermissionsCsrfZeroDefaultPrivilegesByNamePinnedAndInUseGuards_AndAuditEvents()
    {
        await ResetIsolatedDatabaseAsync();
        var (factory, spy, dpDir) = CreateFactory();

        try
        {
            var (_, adminClient) = await BootstrapActivateAndLoginAdminAsync(factory, spy);
            var adminCsrf = await FetchCsrfAsync(adminClient);

            // 1. Unauthenticated access must fail with 401
            using (var anonClient = CreateCookieClient(factory))
            {
                using var unauthRoles = await anonClient.GetAsync("/api/v1/roles");
                Assert.Equal(HttpStatusCode.Unauthorized, unauthRoles.StatusCode);
            }

            // 2. CSRF required on POST /api/v1/roles (missing header -> 403)
            using (var missingCsrfReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/roles")
            {
                Content = JsonContent.Create(new { name = "SuperAdmin", isPinned = false })
            })
            {
                using var missingCsrfRes = await adminClient.SendAsync(missingCsrfReq);
                Assert.Equal(HttpStatusCode.Forbidden, missingCsrfRes.StatusCode);
            }

            // 3. Create role literally named "SuperAdmin" -> must start with 0 permissions (no hardcoded privileges by name!)
            Guid namedSuperAdminRoleId;
            using (var createReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/roles")
            {
                Content = JsonContent.Create(new { name = "SuperAdmin", isPinned = false })
            })
            {
                createReq.Headers.Add("X-CSRF-TOKEN", adminCsrf);
                using var createRes = await adminClient.SendAsync(createReq);
                Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);

                using var roleDoc = JsonDocument.Parse(await createRes.Content.ReadAsStringAsync());
                namedSuperAdminRoleId = roleDoc.RootElement.GetProperty("id").GetGuid();
                Assert.Equal("SuperAdmin", roleDoc.RootElement.GetProperty("name").GetString());
                Assert.Equal(0, roleDoc.RootElement.GetProperty("grantedPermissionsCount").GetInt32());
                Assert.Equal(0, roleDoc.RootElement.GetProperty("assignedUsersCount").GetInt32());
                Assert.True(roleDoc.RootElement.GetProperty("isActive").GetBoolean());
            }

            // Duplicate role name (case-insensitive) -> 409 Conflict
            using (var dupReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/roles")
            {
                Content = JsonContent.Create(new { name = "superadmin", isPinned = false })
            })
            {
                dupReq.Headers.Add("X-CSRF-TOKEN", adminCsrf);
                using var dupRes = await adminClient.SendAsync(dupReq);
                Assert.Equal(HttpStatusCode.Conflict, dupRes.StatusCode);
            }

            // 4. Create user assigned to "SuperAdmin" role -> verify zero implicit permissions despite role name!
            var memberEmail = $"supername-member-{Guid.NewGuid():N}@api-governance.local";
            Guid memberId;
            using (var createUserReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/users")
            {
                Content = JsonContent.Create(new
                {
                    fullName = "مستخدم بدور اسمي فقط",
                    email = memberEmail,
                    triggerActivationToken = true,
                    roleIds = new[] { namedSuperAdminRoleId }
                })
            })
            {
                createUserReq.Headers.Add("X-CSRF-TOKEN", adminCsrf);
                using var createUserRes = await adminClient.SendAsync(createUserReq);
                Assert.Equal(HttpStatusCode.Created, createUserRes.StatusCode);
                using var uDoc = JsonDocument.Parse(await createUserRes.Content.ReadAsStringAsync());
                memberId = uDoc.RootElement.GetProperty("id").GetGuid();
            }

            var (_, memberClient) = await ActivateAndSignInUserAsync(
                factory,
                spy,
                memberId,
                memberEmail,
                "MemberZeroPerm!2026");

            using (var meRes = await memberClient.GetAsync("/api/v1/me"))
            {
                Assert.Equal(HttpStatusCode.OK, meRes.StatusCode);
                using var meDoc = JsonDocument.Parse(await meRes.Content.ReadAsStringAsync());
                Assert.Equal(0, meDoc.RootElement.GetProperty("permissions").GetArrayLength());
            }

            using (var forbiddenUsersRes = await memberClient.GetAsync("/api/v1/users"))
            {
                Assert.Equal(HttpStatusCode.Forbidden, forbiddenUsersRes.StatusCode);
            }

            using (var forbiddenRolesRes = await memberClient.GetAsync("/api/v1/roles"))
            {
                Assert.Equal(HttpStatusCode.Forbidden, forbiddenRolesRes.StatusCode);
            }

            // 5. Pinned role (SystemAdmin) cannot be deactivated (422) or deleted (422)
            Guid pinnedSystemAdminId;
            using (var rolesRes = await adminClient.GetAsync("/api/v1/roles?search=SystemAdmin"))
            {
                Assert.Equal(HttpStatusCode.OK, rolesRes.StatusCode);
                using var listDoc = JsonDocument.Parse(await rolesRes.Content.ReadAsStringAsync());
                pinnedSystemAdminId = listDoc.RootElement.GetProperty("items")[0].GetProperty("id").GetGuid();
            }

            using (var deactivatePinnedReq = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/roles/{pinnedSystemAdminId}")
            {
                Content = JsonContent.Create(new { isActive = false })
            })
            {
                deactivatePinnedReq.Headers.Add("X-CSRF-TOKEN", adminCsrf);
                using var res = await adminClient.SendAsync(deactivatePinnedReq);
                Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
            }

            using (var deletePinnedReq = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/roles/{pinnedSystemAdminId}"))
            {
                deletePinnedReq.Headers.Add("X-CSRF-TOKEN", adminCsrf);
                using var res = await adminClient.SendAsync(deletePinnedReq);
                Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
            }

            // 6. In-use unpinned role cannot be deleted while assigned (422)
            using (var deleteInUseReq = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/roles/{namedSuperAdminRoleId}"))
            {
                deleteInUseReq.Headers.Add("X-CSRF-TOKEN", adminCsrf);
                using var res = await adminClient.SendAsync(deleteInUseReq);
                Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
            }

            // 7. Toggle unused unpinned role active state after detaching user, then delete cleanly (204)
            using (var clearRolesReq = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/users/{memberId}/roles")
            {
                Content = JsonContent.Create(new { roleIds = Array.Empty<Guid>() })
            })
            {
                clearRolesReq.Headers.Add("X-CSRF-TOKEN", adminCsrf);
                using var clearRes = await adminClient.SendAsync(clearRolesReq);
                Assert.Equal(HttpStatusCode.OK, clearRes.StatusCode);
            }

            using (var deactivateReq = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/roles/{namedSuperAdminRoleId}")
            {
                Content = JsonContent.Create(new { isActive = false })
            })
            {
                deactivateReq.Headers.Add("X-CSRF-TOKEN", adminCsrf);
                using var res = await adminClient.SendAsync(deactivateReq);
                Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            }

            using (var reactivateReq = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/roles/{namedSuperAdminRoleId}")
            {
                Content = JsonContent.Create(new { name = "SuperAdminRenamed", isActive = true })
            })
            {
                reactivateReq.Headers.Add("X-CSRF-TOKEN", adminCsrf);
                using var res = await adminClient.SendAsync(reactivateReq);
                Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            }

            using (var deleteUnusedReq = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/roles/{namedSuperAdminRoleId}"))
            {
                deleteUnusedReq.Headers.Add("X-CSRF-TOKEN", adminCsrf);
                using var res = await adminClient.SendAsync(deleteUnusedReq);
                Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
            }

            // 8. Verify OP-01 audit events
            await using var verifyScope = factory.Services.CreateAsyncScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApiGovernanceDbContext>();
            var operations = await verifyDb.AuditEvents
                .Where(a => a.ResourceId == namedSuperAdminRoleId || a.ResourceId == memberId)
                .Select(a => a.Operation)
                .ToListAsync();

            Assert.Contains("ROLE_CREATED", operations);
            Assert.Contains("ROLE_DEACTIVATED", operations);
            Assert.Contains("ROLE_ACTIVATED", operations);
            Assert.Contains("ROLE_DELETED", operations);
            Assert.Contains("USER_ROLES_REPLACED", operations);
        }
        finally
        {
            await factory.DisposeAsync();
            if (Directory.Exists(dpDir)) Directory.Delete(dpDir, recursive: true);
        }
    }

    [Fact]
    public async Task MultiRoleAssignment_CombinesEffectivePermissionsAcrossActiveRoles_UpdatesImmediately_AndProtectsLastAdmin()
    {
        await ResetIsolatedDatabaseAsync();
        var (factory, spy, dpDir) = CreateFactory();

        try
        {
            var (adminId, adminClient) = await BootstrapActivateAndLoginAdminAsync(factory, spy);
            var adminCsrf = await FetchCsrfAsync(adminClient);

            // 1. Create two custom roles via API (initially 0 permissions each)
            async Task<Guid> CreateRoleAsync(string roleName)
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/roles")
                {
                    Content = JsonContent.Create(new { name = roleName, isPinned = false })
                };
                req.Headers.Add("X-CSRF-TOKEN", adminCsrf);
                using var res = await adminClient.SendAsync(req);
                Assert.Equal(HttpStatusCode.Created, res.StatusCode);
                using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
                return doc.RootElement.GetProperty("id").GetGuid();
            }

            var usersReaderRoleId = await CreateRoleAsync("CustomUsersReader");
            var rolesViewerRoleId = await CreateRoleAsync("CustomRolesViewer");

            // Seed specific permissions per role to test dynamic union across multiple roles (I04 manages these via UI later)
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiGovernanceDbContext>();
                var usersViewPerm = await db.Permissions.FirstAsync(p => p.Code == UserPermissionCodes.UsersView);
                var rolesViewPerm = await db.Permissions.FirstAsync(p => p.Code == UserPermissionCodes.RolesView);

                db.RolePermissions.Add(new RolePermissionEntity
                {
                    Id = Guid.NewGuid(),
                    RoleId = usersReaderRoleId,
                    PermissionId = usersViewPerm.Id
                });
                db.RolePermissions.Add(new RolePermissionEntity
                {
                    Id = Guid.NewGuid(),
                    RoleId = rolesViewerRoleId,
                    PermissionId = rolesViewPerm.Id
                });
                await db.SaveChangesAsync();
            }

            // 2. Create operator account (no roles yet), activate and sign in
            var operatorEmail = $"multi-role-op-{Guid.NewGuid():N}@api-governance.local";
            Guid operatorId;
            using (var createOpReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/users")
            {
                Content = JsonContent.Create(new
                {
                    fullName = "مشغّل متعدد الأدوار",
                    email = operatorEmail,
                    triggerActivationToken = true
                })
            })
            {
                createOpReq.Headers.Add("X-CSRF-TOKEN", adminCsrf);
                using var createOpRes = await adminClient.SendAsync(createOpReq);
                Assert.Equal(HttpStatusCode.Created, createOpRes.StatusCode);
                using var uDoc = JsonDocument.Parse(await createOpRes.Content.ReadAsStringAsync());
                operatorId = uDoc.RootElement.GetProperty("id").GetGuid();
            }

            var (_, opClient) = await ActivateAndSignInUserAsync(
                factory,
                spy,
                operatorId,
                operatorEmail,
                "MultiRoleOp!2026#");

            // Before assignment -> neither /users nor /roles is allowed
            Assert.Equal(HttpStatusCode.Forbidden, (await opClient.GetAsync("/api/v1/users")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await opClient.GetAsync("/api/v1/roles")).StatusCode);

            // 3. Assign BOTH roles atomically via PUT /api/v1/users/{id}/roles (including duplicate GUID in input to test deduplication)
            using (var assignReq = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/users/{operatorId}/roles")
            {
                Content = JsonContent.Create(new { roleIds = new[] { usersReaderRoleId, rolesViewerRoleId, usersReaderRoleId } })
            })
            {
                assignReq.Headers.Add("X-CSRF-TOKEN", adminCsrf);
                using var assignRes = await adminClient.SendAsync(assignReq);
                Assert.Equal(HttpStatusCode.OK, assignRes.StatusCode);

                using var assignDoc = JsonDocument.Parse(await assignRes.Content.ReadAsStringAsync());
                Assert.Equal(2, assignDoc.RootElement.GetProperty("roles").GetArrayLength());
                Assert.Equal(2, assignDoc.RootElement.GetProperty("effectivePermissionsCount").GetInt32());
            }

            // Verify immediate combined effect on operator's live endpoints & /me
            using (var opMeRes = await opClient.GetAsync("/api/v1/me"))
            {
                Assert.Equal(HttpStatusCode.OK, opMeRes.StatusCode);
                using var meDoc = JsonDocument.Parse(await opMeRes.Content.ReadAsStringAsync());
                var perms = meDoc.RootElement.GetProperty("permissions").EnumerateArray().Select(e => e.GetString()!).ToList();
                Assert.Contains(UserPermissionCodes.UsersView, perms);
                Assert.Contains(UserPermissionCodes.RolesView, perms);
            }

            Assert.Equal(HttpStatusCode.OK, (await opClient.GetAsync("/api/v1/users")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await opClient.GetAsync("/api/v1/roles")).StatusCode);
            using (var opCreateRoleReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/roles")
            {
                Content = JsonContent.Create(new { name = "UnauthorizedRole", isPinned = false })
            })
            {
                opCreateRoleReq.Headers.Add("X-CSRF-TOKEN", await FetchCsrfAsync(opClient));
                Assert.Equal(HttpStatusCode.Forbidden, (await opClient.SendAsync(opCreateRoleReq)).StatusCode);
            }

            // 4. Reject assigning nonexistent or inactive role (422)
            using (var badRoleReq = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/users/{operatorId}/roles")
            {
                Content = JsonContent.Create(new { roleIds = new[] { Guid.NewGuid() } })
            })
            {
                badRoleReq.Headers.Add("X-CSRF-TOKEN", adminCsrf);
                Assert.Equal(HttpStatusCode.UnprocessableEntity, (await adminClient.SendAsync(badRoleReq)).StatusCode);
            }

            // 5. Remove CustomRolesViewer atomically -> operator immediately keeps users.view (200) and loses roles.view (403)
            using (var trimReq = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/users/{operatorId}/roles")
            {
                Content = JsonContent.Create(new { roleIds = new[] { usersReaderRoleId } })
            })
            {
                trimReq.Headers.Add("X-CSRF-TOKEN", adminCsrf);
                Assert.Equal(HttpStatusCode.OK, (await adminClient.SendAsync(trimReq)).StatusCode);
            }

            Assert.Equal(HttpStatusCode.OK, (await opClient.GetAsync("/api/v1/users")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await opClient.GetAsync("/api/v1/roles")).StatusCode);

            // 6. Last active admin guard -> cannot strip roles from the sole active admin (422)
            using (var stripAdminReq = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/users/{adminId}/roles")
            {
                Content = JsonContent.Create(new { roleIds = Array.Empty<Guid>() })
            })
            {
                stripAdminReq.Headers.Add("X-CSRF-TOKEN", adminCsrf);
                Assert.Equal(HttpStatusCode.UnprocessableEntity, (await adminClient.SendAsync(stripAdminReq)).StatusCode);
            }
        }
        finally
        {
            await factory.DisposeAsync();
            if (Directory.Exists(dpDir)) Directory.Delete(dpDir, recursive: true);
        }
    }
}

