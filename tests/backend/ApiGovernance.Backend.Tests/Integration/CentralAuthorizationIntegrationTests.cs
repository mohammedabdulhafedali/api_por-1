using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using ApiGovernance.Modules.IdentityAccess.Contracts;
using ApiGovernance.Modules.IdentityAccess.Services;
using ApiGovernance.Shared.Persistence;
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

[Collection("IsolatedTestDatabase")]
public sealed class CentralAuthorizationIntegrationTests
{
    private sealed class CapturingDeliverySpy : IAuthTokenDeliveryChannel
    {
        public string ChannelCode => "TEST_I05_SPY";
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

    [Fact]
    public async Task AllVersionedApiEndpoints_ExplicitlyDeclareCentralAuthorizationOrAllowAnonymousMetadata()
    {
        var (factory, _, dpDir) = CreateFactory();
        try
        {
            using var _ = factory.CreateClient();
            var endpointDataSource = factory.Services.GetRequiredService<EndpointDataSource>();

            var issues = EndpointAuthorizationCatalogVerifier.AuditVersionedApiEndpoints(endpointDataSource);
            Assert.Empty(issues);

            var permissionMetadataCodes = endpointDataSource.Endpoints
                .Select(e => e.Metadata.GetMetadata<RequiredPermissionEndpointMetadata>()?.PermissionCode)
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            Assert.Contains(UserPermissionCodes.UsersView, permissionMetadataCodes);
            Assert.Contains(UserPermissionCodes.UsersCreate, permissionMetadataCodes);
            Assert.Contains(UserPermissionCodes.UsersEdit, permissionMetadataCodes);
            Assert.Contains(UserPermissionCodes.UsersAssignRoles, permissionMetadataCodes);
            Assert.Contains(UserPermissionCodes.UsersPasswordReset, permissionMetadataCodes);
            Assert.Contains(UserPermissionCodes.RolesView, permissionMetadataCodes);
            Assert.Contains(UserPermissionCodes.RolesCreate, permissionMetadataCodes);
            Assert.Contains(UserPermissionCodes.RolesEdit, permissionMetadataCodes);
            Assert.Contains(UserPermissionCodes.RolesDelete, permissionMetadataCodes);
            Assert.Contains(UserPermissionCodes.PermissionsView, permissionMetadataCodes);
            Assert.Contains(UserPermissionCodes.PermissionsManage, permissionMetadataCodes);
        }
        finally
        {
            await factory.DisposeAsync();
            if (Directory.Exists(dpDir)) Directory.Delete(dpDir, recursive: true);
        }
    }

    [Fact]
    public async Task CentralAuthorization_EnforcesServer403BeforeValidation_MultiRoleUnion_RevokePrecedence_AndCrossModuleService()
    {
        await ResetIsolatedDatabaseAsync();
        var (factory, spy, dpDir) = CreateFactory();

        try
        {
            const string adminPass = "Bootstr@pAdminI05!2026";
            var adminEmail = $"sysadmin-i05-{Guid.NewGuid():N}@api-governance.local";

            Guid adminId;
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var bootstrap = scope.ServiceProvider.GetRequiredService<IInitialAdminBootstrapService>();
                var bootRes = await bootstrap.BootstrapFirstAdminAsync(
                    new InitialAdminBootstrapRequest(adminEmail, "مدير النظام I05"));
                adminId = bootRes.AdminUserId!.Value;
            }

            var adminClient = CreateCookieClient(factory);
            var adminCsrf = await FetchCsrfAsync(adminClient);
            var adminToken = spy.Deliveries.Last(d => d.TargetUserId == adminId).SingleUseToken;

            using (var actReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/password/reset/complete")
            {
                Content = JsonContent.Create(new { email = adminEmail, token = adminToken, newPassword = adminPass })
            })
            {
                actReq.Headers.Add("X-CSRF-TOKEN", adminCsrf);
                Assert.Equal(HttpStatusCode.NoContent, (await adminClient.SendAsync(actReq)).StatusCode);
            }

            using (var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
            {
                Content = JsonContent.Create(new { email = adminEmail, password = adminPass })
            })
            {
                loginReq.Headers.Add("X-CSRF-TOKEN", adminCsrf);
                Assert.Equal(HttpStatusCode.OK, (await adminClient.SendAsync(loginReq)).StatusCode);
            }

            // Re-fetch CSRF token bound to the newly authenticated admin identity
            adminCsrf = await FetchCsrfAsync(adminClient);

            Dictionary<string, Guid> permMap;
            using (var catRes = await adminClient.GetAsync("/api/v1/permissions"))
            {
                catRes.EnsureSuccessStatusCode();
                using var doc = JsonDocument.Parse(await catRes.Content.ReadAsStringAsync());
                permMap = doc.RootElement.GetProperty("permissions").EnumerateArray().ToDictionary(
                    e => e.GetProperty("code").GetString()!,
                    e => e.GetProperty("id").GetGuid(),
                    StringComparer.OrdinalIgnoreCase);
            }

            async Task<Guid> CreateRoleWithPermsAsync(string name, params string[] codes)
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/roles")
                {
                    Content = JsonContent.Create(new { name, isPinned = false })
                };
                req.Headers.Add("X-CSRF-TOKEN", adminCsrf);
                using var res = await adminClient.SendAsync(req);
                res.EnsureSuccessStatusCode();
                using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
                var roleId = doc.RootElement.GetProperty("id").GetGuid();

                using var permReq = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/roles/{roleId}/permissions")
                {
                    Content = JsonContent.Create(new { permissionIds = codes.Select(c => permMap[c]).ToArray() })
                };
                permReq.Headers.Add("X-CSRF-TOKEN", adminCsrf);
                (await adminClient.SendAsync(permReq)).EnsureSuccessStatusCode();
                return roleId;
            }

            var roleAlphaId = await CreateRoleWithPermsAsync("CentralAlpha", UserPermissionCodes.UsersView);
            var roleBetaId = await CreateRoleWithPermsAsync("CentralBeta", UserPermissionCodes.RolesView, "company_systems.view");

            var memberEmail = $"central-member-{Guid.NewGuid():N}@api-governance.local";
            Guid memberId;
            using (var createUserReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/users")
            {
                Content = JsonContent.Create(new
                {
                    fullName = "عضو تفويض مركزي",
                    email = memberEmail,
                    triggerActivationToken = true,
                    roleIds = new[] { roleAlphaId, roleBetaId }
                })
            })
            {
                createUserReq.Headers.Add("X-CSRF-TOKEN", adminCsrf);
                using var res = await adminClient.SendAsync(createUserReq);
                res.EnsureSuccessStatusCode();
                using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
                memberId = doc.RootElement.GetProperty("id").GetGuid();
            }

            var memberClient = CreateCookieClient(factory);
            var memberCsrf = await FetchCsrfAsync(memberClient);
            var memberToken = spy.Deliveries.Last(d => d.TargetUserId == memberId).SingleUseToken;

            using (var actMember = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/password/reset/complete")
            {
                Content = JsonContent.Create(new { email = memberEmail, token = memberToken, newPassword = "CentralMember!2026#" })
            })
            {
                actMember.Headers.Add("X-CSRF-TOKEN", memberCsrf);
                Assert.Equal(HttpStatusCode.NoContent, (await memberClient.SendAsync(actMember)).StatusCode);
            }

            using (var loginMember = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
            {
                Content = JsonContent.Create(new { email = memberEmail, password = "CentralMember!2026#" })
            })
            {
                loginMember.Headers.Add("X-CSRF-TOKEN", memberCsrf);
                Assert.Equal(HttpStatusCode.OK, (await memberClient.SendAsync(loginMember)).StatusCode);
            }

            // 1. UI Bypass attempt with INVALID payload against POST /api/v1/users -> central filter runs BEFORE validation and returns 403 ProblemDetails (not 400!)
            using (var bypassReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/users")
            {
                Content = JsonContent.Create(new { fullName = "", email = "invalid-not-email" })
            })
            {
                bypassReq.Headers.Add("X-CSRF-TOKEN", await FetchCsrfAsync(memberClient));
                using var bypassRes = await memberClient.SendAsync(bypassReq);
                Assert.Equal(HttpStatusCode.Forbidden, bypassRes.StatusCode);
                using var errDoc = JsonDocument.Parse(await bypassRes.Content.ReadAsStringAsync());
                Assert.Equal("ERR_FORBIDDEN", errDoc.RootElement.GetProperty("code").GetString());
                Assert.False(string.IsNullOrWhiteSpace(errDoc.RootElement.GetProperty("traceId").GetString()));
            }

            // 2. Shared IServerAuthorizationService works identically for future modules (e.g. company_systems.view from RoleBeta)
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiGovernanceDbContext>();
                var memberUser = await db.Users.AsNoTracking().FirstAsync(u => u.Id == memberId);
                var centralAuth = scope.ServiceProvider.GetRequiredService<IServerAuthorizationService>();
                var principal = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, memberUser.Id.ToString()),
                    new Claim(PlatformSecurityClaimTypes.SecurityStamp, memberUser.SecurityStamp!)
                ], "Testing"));

                Assert.True(await centralAuth.HasGeneralPermissionAsync(principal, "company_systems.view"));
                Assert.False(await centralAuth.HasGeneralPermissionAsync(principal, "api_documents.delete"));
            }

            // 3. Multi-role union grants both /users and /roles; individual REVOKE immediately blocks /users with 403
            Assert.Equal(HttpStatusCode.OK, (await memberClient.GetAsync("/api/v1/users")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await memberClient.GetAsync("/api/v1/roles")).StatusCode);

            using (var revokeReq = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/users/{memberId}/permission-overrides")
            {
                Content = JsonContent.Create(new
                {
                    overrides = new[]
                    {
                        new { permissionId = permMap[UserPermissionCodes.UsersView], decision = "REVOKE" }
                    }
                })
            })
            {
                revokeReq.Headers.Add("X-CSRF-TOKEN", adminCsrf);
                Assert.Equal(HttpStatusCode.OK, (await adminClient.SendAsync(revokeReq)).StatusCode);
            }

            Assert.Equal(HttpStatusCode.Forbidden, (await memberClient.GetAsync("/api/v1/users")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await memberClient.GetAsync("/api/v1/roles")).StatusCode);

            // Verify OP-01 audit captured endpoint denials
            await using var verifyScope = factory.Services.CreateAsyncScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApiGovernanceDbContext>();
            var deniedOps = await verifyDb.AuditEvents
                .Where(a => a.ActorUserId == memberId && a.Result == "FORBIDDEN")
                .Select(a => a.Operation)
                .ToListAsync();
            Assert.Contains("USER_CREATE_DENIED", deniedOps);
            Assert.Contains("USERS_LIST_DENIED", deniedOps);
        }
        finally
        {
            await factory.DisposeAsync();
            if (Directory.Exists(dpDir)) Directory.Delete(dpDir, recursive: true);
        }
    }
}

