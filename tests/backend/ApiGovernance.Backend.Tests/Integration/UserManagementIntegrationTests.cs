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
public sealed class UserManagementIntegrationTests
{
    private sealed class CapturingDeliverySpy : IAuthTokenDeliveryChannel
    {
        public string ChannelCode => "TEST_I02_SPY";
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
                        ["AuthenticationPolicy:LoginRateLimitPermitLimit"] = "50"
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

    private static async Task CleanAllUserManagementRowsOnTestDbAsync()
    {
        var migratorConn = BackendContractIntegrationTests.GetIsolatedTestMigrationsConnectionString();
        var options = new DbContextOptionsBuilder<ApiGovernanceDbContext>()
            .UseNpgsql(migratorConn, n => n.MigrationsAssembly(typeof(ApiGovernanceDbContext).Assembly.FullName))
            .Options;
        await using var db = new ApiGovernanceDbContext(options);

        // Reset isolated test DB via official Down->Up migrations so OP-01 triggers & Restrict FKs remain respected
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("0");
        await migrator.MigrateAsync();
    }

    [Fact]
    public async Task FirstAdminBootstrap_IsOneTimeSecure_AndDrivesFullUserLifecycleWithDeactivationSessionRevocation()
    {
        await CleanAllUserManagementRowsOnTestDbAsync();
        var (factory, spy, dpDir) = CreateFactory();

        try
        {
            const string adminPass = "Bootstr@pAdmin!2026";
            const string memberPass = "MemberActiv@ted!2026";
            var adminEmail = $"sysadmin-{Guid.NewGuid():N}@api-governance.local";

            // 1. Bootstrap first admin cleanly without hardcoded password (activation token flow)
            Guid adminId;
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var bootstrap = scope.ServiceProvider.GetRequiredService<IInitialAdminBootstrapService>();
                var res1 = await bootstrap.BootstrapFirstAdminAsync(
                    new InitialAdminBootstrapRequest(adminEmail, "مدير النظام الأول"));

                Assert.True(res1.Created);
                Assert.Equal("BOOTSTRAP_ADMIN_CREATED", res1.StatusCode);
                Assert.True(res1.RequiresActivation);
                Assert.NotNull(res1.AdminUserId);
                adminId = res1.AdminUserId!.Value;

                // Second bootstrap call MUST refuse even when providing a password, because bootstrap is disabled after first use!
                var res2 = await bootstrap.BootstrapFirstAdminAsync(
                    new InitialAdminBootstrapRequest("rogue-second@api-governance.local", "Rogue Second", "RogueValid15P@ss!"));
                Assert.False(res2.Created);
                Assert.Equal("BOOTSTRAP_ALREADY_INITIALIZED", res2.StatusCode);
            }

            var adminDelivery = Assert.Single(spy.Deliveries);
            Assert.Equal("ACCOUNT_ACTIVATION", adminDelivery.Purpose);

            // Activate first admin via single-use token & sign in
            using var adminClient = CreateCookieClient(factory);
            var anonCsrf = await FetchCsrfAsync(adminClient);
            using (var activateReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/password/reset/complete")
            {
                Content = JsonContent.Create(new
                {
                    email = adminEmail,
                    token = adminDelivery.SingleUseToken,
                    newPassword = adminPass
                })
            })
            {
                activateReq.Headers.Add("X-CSRF-TOKEN", anonCsrf);
                using var activateRes = await adminClient.SendAsync(activateReq);
                Assert.Equal(HttpStatusCode.NoContent, activateRes.StatusCode);
            }

            using (var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
            {
                Content = JsonContent.Create(new { email = adminEmail, password = adminPass })
            })
            {
                loginReq.Headers.Add("X-CSRF-TOKEN", anonCsrf);
                using var loginRes = await adminClient.SendAsync(loginReq);
                Assert.Equal(HttpStatusCode.OK, loginRes.StatusCode);
            }

            // 2. Unauthenticated requests to /api/v1/users MUST fail with 401
            using (var anonClient = CreateCookieClient(factory))
            {
                using var unauthList = await anonClient.GetAsync("/api/v1/users");
                Assert.Equal(HttpStatusCode.Unauthorized, unauthList.StatusCode);
            }

            // 3. Create new member user via POST /api/v1/users (requires CSRF + users.create + users.password.reset for activation)
            var memberEmail = $"member-{Guid.NewGuid():N}@api-governance.local";
            var adminCsrf = await FetchCsrfAsync(adminClient);
            Guid memberId;

            using (var createReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/users")
            {
                Content = JsonContent.Create(new
                {
                    fullName = "مهندس تكامل جديد",
                    email = memberEmail,
                    triggerActivationToken = true
                })
            })
            {
                createReq.Headers.Add("X-CSRF-TOKEN", adminCsrf);
                using var createRes = await adminClient.SendAsync(createReq);
                Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);

                var rawBody = await createRes.Content.ReadAsStringAsync();
                Assert.DoesNotContain("passwordHash", rawBody, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("securityStamp", rawBody, StringComparison.OrdinalIgnoreCase);

                using var createdDoc = JsonDocument.Parse(rawBody);
                memberId = createdDoc.RootElement.GetProperty("id").GetGuid();
                Assert.True(createdDoc.RootElement.GetProperty("isActive").GetBoolean());
                Assert.False(createdDoc.RootElement.GetProperty("isActivated").GetBoolean());
            }

            // Duplicate email -> 409 Conflict
            using (var dupReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/users")
            {
                Content = JsonContent.Create(new
                {
                    fullName = "Duplicate User",
                    email = memberEmail.ToUpperInvariant(),
                    triggerActivationToken = false
                })
            })
            {
                dupReq.Headers.Add("X-CSRF-TOKEN", adminCsrf);
                using var dupRes = await adminClient.SendAsync(dupReq);
                Assert.Equal(HttpStatusCode.Conflict, dupRes.StatusCode);
            }

            // Nonexistent ConsumerEntityId -> 422 Unprocessable Entity
            using (var badConsumerReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/users")
            {
                Content = JsonContent.Create(new
                {
                    fullName = "Bad Scope User",
                    email = $"badscope-{Guid.NewGuid():N}@api-governance.local",
                    consumerEntityId = Guid.NewGuid(),
                    triggerActivationToken = false
                })
            })
            {
                badConsumerReq.Headers.Add("X-CSRF-TOKEN", adminCsrf);
                using var badConsumerRes = await adminClient.SendAsync(badConsumerReq);
                Assert.Equal(HttpStatusCode.UnprocessableEntity, badConsumerRes.StatusCode);
            }

            // 4. Activate member account & verify unprivileged user receives 403 Forbidden on /api/v1/users
            var memberDelivery = spy.Deliveries.Last();
            Assert.Equal(memberId, memberDelivery.TargetUserId);

            using var memberClient = CreateCookieClient(factory);
            var memberAnonCsrf = await FetchCsrfAsync(memberClient);

            using (var actMemberReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/password/reset/complete")
            {
                Content = JsonContent.Create(new
                {
                    email = memberEmail,
                    token = memberDelivery.SingleUseToken,
                    newPassword = memberPass
                })
            })
            {
                actMemberReq.Headers.Add("X-CSRF-TOKEN", memberAnonCsrf);
                using var actMemberRes = await memberClient.SendAsync(actMemberReq);
                Assert.Equal(HttpStatusCode.NoContent, actMemberRes.StatusCode);
            }

            using (var loginMemberReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
            {
                Content = JsonContent.Create(new { email = memberEmail, password = memberPass })
            })
            {
                loginMemberReq.Headers.Add("X-CSRF-TOKEN", memberAnonCsrf);
                using var loginMemberRes = await memberClient.SendAsync(loginMemberReq);
                Assert.Equal(HttpStatusCode.OK, loginMemberRes.StatusCode);
            }

            // Unprivileged member calling GET /api/v1/users or POST /api/v1/users MUST receive 403 Forbidden
            using (var forbiddenList = await memberClient.GetAsync("/api/v1/users"))
            {
                Assert.Equal(HttpStatusCode.Forbidden, forbiddenList.StatusCode);
            }

            // 5. Admin lists & searches users -> returns both admin and activated member
            using (var listRes = await adminClient.GetAsync($"/api/v1/users?search={Uri.EscapeDataString("مهندس")}&isActive=true"))
            {
                Assert.Equal(HttpStatusCode.OK, listRes.StatusCode);
                using var listDoc = JsonDocument.Parse(await listRes.Content.ReadAsStringAsync());
                Assert.Equal(1, listDoc.RootElement.GetProperty("totalCount").GetInt32());
                var first = listDoc.RootElement.GetProperty("items")[0];
                Assert.True(first.GetProperty("isActivated").GetBoolean());
            }

            // 6. Self-deactivation guard -> Admin cannot deactivate themselves (422)
            using (var selfDeactivateReq = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/users/{adminId}")
            {
                Content = JsonContent.Create(new { isActive = false })
            })
            {
                selfDeactivateReq.Headers.Add("X-CSRF-TOKEN", adminCsrf);
                using var selfDeactivateRes = await adminClient.SendAsync(selfDeactivateReq);
                Assert.Equal(HttpStatusCode.UnprocessableEntity, selfDeactivateRes.StatusCode);
            }

            // 7. Admin deactivates member -> member's live session is immediately revoked (401) & future login refused
            using (var deactivateReq = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/users/{memberId}")
            {
                Content = JsonContent.Create(new { fullName = "مهندس تكامل (معطل)", isActive = false })
            })
            {
                deactivateReq.Headers.Add("X-CSRF-TOKEN", adminCsrf);
                using var deactivateRes = await adminClient.SendAsync(deactivateReq);
                Assert.Equal(HttpStatusCode.OK, deactivateRes.StatusCode);
                using var patchDoc = JsonDocument.Parse(await deactivateRes.Content.ReadAsStringAsync());
                Assert.False(patchDoc.RootElement.GetProperty("isActive").GetBoolean());
            }

            using (var memberMeAfterDeactivate = await memberClient.GetAsync("/api/v1/me"))
            {
                Assert.Equal(HttpStatusCode.Unauthorized, memberMeAfterDeactivate.StatusCode);
            }

            using (var blockedLoginReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
            {
                Content = JsonContent.Create(new { email = memberEmail, password = memberPass })
            })
            {
                blockedLoginReq.Headers.Add("X-CSRF-TOKEN", await FetchCsrfAsync(memberClient));
                using var blockedLoginRes = await memberClient.SendAsync(blockedLoginReq);
                Assert.Equal(HttpStatusCode.Unauthorized, blockedLoginRes.StatusCode);
            }

            // 8. Admin reactivates member -> member can sign in again (200)
            using (var reactivateReq = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/users/{memberId}")
            {
                Content = JsonContent.Create(new { isActive = true })
            })
            {
                reactivateReq.Headers.Add("X-CSRF-TOKEN", adminCsrf);
                using var reactivateRes = await adminClient.SendAsync(reactivateReq);
                Assert.Equal(HttpStatusCode.OK, reactivateRes.StatusCode);
            }

            using (var okLoginAgainReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
            {
                Content = JsonContent.Create(new { email = memberEmail, password = memberPass })
            })
            {
                okLoginAgainReq.Headers.Add("X-CSRF-TOKEN", await FetchCsrfAsync(memberClient));
                using var okLoginAgainRes = await memberClient.SendAsync(okLoginAgainReq);
                Assert.Equal(HttpStatusCode.OK, okLoginAgainRes.StatusCode);
            }

            // 9. Verify OP-01 audit events recorded without sensitive values
            await using var verifyScope = factory.Services.CreateAsyncScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApiGovernanceDbContext>();
            var operations = await verifyDb.AuditEvents
                .Where(a => a.ResourceId == adminId || a.ResourceId == memberId)
                .Select(a => a.Operation)
                .ToListAsync();

            Assert.Contains("BOOTSTRAP_ADMIN_CREATED", operations);
            Assert.Contains("USER_CREATED", operations);
            Assert.Contains("USER_DEACTIVATED", operations);
            Assert.Contains("USER_ACTIVATED", operations);
        }
        finally
        {
            await factory.DisposeAsync();
            if (Directory.Exists(dpDir)) Directory.Delete(dpDir, recursive: true);
        }
    }
}

