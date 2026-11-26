using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ApiGovernance.Modules.IdentityAccess.Contracts;
using ApiGovernance.Modules.IdentityAccess.Services;
using ApiGovernance.Shared.Contracts;
using ApiGovernance.Shared.Persistence;
using ApiGovernance.Shared.Persistence.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace ApiGovernance.Backend.Tests.Integration;

[Collection("IsolatedTestDatabase")]
public sealed class IdentityAuthenticationIntegrationTests
{
    private sealed class CapturingTokenDeliveryChannel : IAuthTokenDeliveryChannel
    {
        public string ChannelCode => "TEST_IN_MEMORY_SPY";
        public AuthTokenDeliveryContext? LastDelivery { get; private set; }

        public Task DeliverPasswordResetTokenAsync(
            AuthTokenDeliveryContext context,
            CancellationToken cancellationToken = default)
        {
            LastDelivery = context;
            return Task.CompletedTask;
        }
    }

    private static (WebApplicationFactory<Program> Factory, CapturingTokenDeliveryChannel DeliverySpy, string DataProtectionDir)
        CreateAuthFactory(string environmentName = "Testing")
    {
        var dpDir = Path.Combine(Path.GetTempPath(), "ApiGovTestDP", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dpDir);

        var spy = new CapturingTokenDeliveryChannel();
        var appConn = BackendContractIntegrationTests.GetIsolatedTestDatabaseConnectionString();

        var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(environmentName);
                builder.ConfigureAppConfiguration((_, configBuilder) =>
                {
                    configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:DefaultConnection"] = appConn,
                        ["AuthenticationPolicy:DataProtectionKeyDirectory"] = dpDir,
                        ["AuthenticationPolicy:MaxFailedAccessAttempts"] = "5",
                        ["AuthenticationPolicy:LoginRateLimitPermitLimit"] = "30"
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

    private static HttpClient CreateCookieClient(WebApplicationFactory<Program> factory, bool https = false)
    {
        return factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
            BaseAddress = new Uri(https ? "https://localhost" : "http://localhost")
        });
    }

    private static async Task<string> FetchCsrfHeaderTokenAsync(HttpClient client)
    {
        using var res = await client.GetAsync("/api/v1/auth/csrf");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.True(res.Headers.CacheControl?.NoStore ?? false);

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var token = doc.RootElement.GetProperty("token").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));
        return token!;
    }

    private static async Task EnsureSchemaMigratedAsync()
    {
        var migratorConn = BackendContractIntegrationTests.GetIsolatedTestMigrationsConnectionString();
        var options = new DbContextOptionsBuilder<ApiGovernanceDbContext>()
            .UseNpgsql(migratorConn, n => n.MigrationsAssembly(typeof(ApiGovernanceDbContext).Assembly.FullName))
            .Options;
        await using var db = new ApiGovernanceDbContext(options);
        await db.Database.MigrateAsync();
    }

    private static async Task<(Guid UserId, string Email)> SeedTestUserAsync(
        IServiceProvider services,
        string? initialPassword,
        bool isActive = true,
        bool grantResetPermission = false)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiGovernanceDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<UserEntity>>();

        var userId = Guid.NewGuid();
        var email = $"i01-{userId:N}@api-governance.local";

        var user = new UserEntity
        {
            Id = userId,
            FullName = $"I01 User {userId:N}"[..24],
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
              Username = email.Split('@')[0],
              NormalizedUsername = email.Split('@')[0].ToUpperInvariant(),
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString("N"),
            LockoutEnabled = true,
            IsActive = isActive,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var createResult = initialPassword is null
            ? await userManager.CreateAsync(user)
            : await userManager.CreateAsync(user, initialPassword);
        Assert.True(createResult.Succeeded, string.Join("; ", createResult.Errors.Select(e => e.Description)));

        if (grantResetPermission)
        {
            var perm = await db.Permissions.FirstOrDefaultAsync(p => p.Code == IdentityPermissionCodes.UsersPasswordReset);
            if (perm is null)
            {
                perm = new PermissionEntity
                {
                    Id = Guid.NewGuid(),
                    Code = IdentityPermissionCodes.UsersPasswordReset,
                    Resource = "users",
                    Action = "password.reset"
                };
                db.Permissions.Add(perm);
            }

            var roleId = Guid.NewGuid();
            db.Roles.Add(new RoleEntity
            {
                Id = roleId,
                Name = $"IdentityAdmin-{roleId:N}",
                IsActive = true,
                IsPinned = false
            });
            db.UserRoles.Add(new UserRoleEntity
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                RoleId = roleId
            });
            db.RolePermissions.Add(new RolePermissionEntity
            {
                Id = Guid.NewGuid(),
                RoleId = roleId,
                PermissionId = perm.Id
            });

            await db.SaveChangesAsync();
        }

        return (userId, email);
    }

    [Fact]
    public async Task ProtectedRoutes_WhenUnauthenticated_Return401ProblemDetailsWithoutHtmlRedirect()
    {
        await EnsureSchemaMigratedAsync();
        var (factory, _, dpDir) = CreateAuthFactory();
        try
        {
            using var client = CreateCookieClient(factory);

            using var meRes = await client.GetAsync("/api/v1/me");
            Assert.Equal(HttpStatusCode.Unauthorized, meRes.StatusCode);
            Assert.Equal("application/problem+json", meRes.Content.Headers.ContentType?.MediaType);
            using var meDoc = JsonDocument.Parse(await meRes.Content.ReadAsStringAsync());
            Assert.Equal(ApiErrorCodes.Unauthorized, meDoc.RootElement.GetProperty("code").GetString());

            var csrf = await FetchCsrfHeaderTokenAsync(client);

            using var logoutReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
            logoutReq.Headers.Add("X-CSRF-TOKEN", csrf);
            using var logoutRes = await client.SendAsync(logoutReq);
            Assert.Equal(HttpStatusCode.Unauthorized, logoutRes.StatusCode);

            using var cpReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/password/change")
            {
                Content = JsonContent.Create(new { currentPassword = "x", newPassword = "ValidP@ssword!2026" })
            };
            cpReq.Headers.Add("X-CSRF-TOKEN", csrf);
            using var cpRes = await client.SendAsync(cpReq);
            Assert.Equal(HttpStatusCode.Unauthorized, cpRes.StatusCode);
        }
        finally
        {
            await factory.DisposeAsync();
            if (Directory.Exists(dpDir)) Directory.Delete(dpDir, recursive: true);
        }
    }

    [Fact]
    public async Task AuthStateChangingEndpoints_WhenCsrfTokenMissing_Return403ProblemDetails()
    {
        await EnsureSchemaMigratedAsync();
        var (factory, _, dpDir) = CreateAuthFactory();
        try
        {
            using var client = CreateCookieClient(factory);

            using var loginRes = await client.PostAsJsonAsync("/api/v1/auth/login", new
            {
                email = "any@api-governance.local",
                password = "SomePassword!2026"
            });
            Assert.Equal(HttpStatusCode.Forbidden, loginRes.StatusCode);
            Assert.Equal("application/problem+json", loginRes.Content.Headers.ContentType?.MediaType);

            using var resetCompleteRes = await client.PostAsJsonAsync("/api/v1/auth/password/reset/complete", new
            {
                email = "any@api-governance.local",
                token = "0123456789ABCDEF0123",
                newPassword = "StrongPassword!2026"
            });
            Assert.Equal(HttpStatusCode.Forbidden, resetCompleteRes.StatusCode);
        }
        finally
        {
            await factory.DisposeAsync();
            if (Directory.Exists(dpDir)) Directory.Delete(dpDir, recursive: true);
        }
    }

    [Fact]
    public async Task FullLifecycle_ActivationLoginMeChangePasswordAndRevocableLogout_WorksEndToEndWithAuditTrail()
    {
        await EnsureSchemaMigratedAsync();
        var (factory, spy, dpDir) = CreateAuthFactory();
        try
        {
            const string adminPass = "AdminP@ssword!2026";
            const string activatedPass = "UserActiv@ted!2026";
            const string changedPass = "UserCh@nged!2027";

            var (adminId, adminEmail) = await SeedTestUserAsync(factory.Services, adminPass, isActive: true, grantResetPermission: true);
            var (targetId, targetEmail) = await SeedTestUserAsync(factory.Services, initialPassword: null, isActive: true, grantResetPermission: false);

            // 1. Unactivated user (PasswordHash == null) must NOT be able to sign in
            using (var unactivatedClient = CreateCookieClient(factory))
            {
                var csrf = await FetchCsrfHeaderTokenAsync(unactivatedClient);
                using var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
                {
                    Content = JsonContent.Create(new { email = targetEmail, password = activatedPass })
                };
                loginReq.Headers.Add("X-CSRF-TOKEN", csrf);
                using var loginRes = await unactivatedClient.SendAsync(loginReq);
                Assert.Equal(HttpStatusCode.Unauthorized, loginRes.StatusCode);
            }

            // 2. Admin signs in via Username (not email), initiates activation/reset -> 204 (zero secrets in HTTP response), token goes only to IAuthTokenDeliveryChannel
            using (var adminClient = CreateCookieClient(factory))
            {
                var adminUsername = adminEmail.Split('@')[0];
                var csrf = await FetchCsrfHeaderTokenAsync(adminClient);
                using var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
                {
                    Content = JsonContent.Create(new { username = adminUsername, password = adminPass })
                };
                loginReq.Headers.Add("X-CSRF-TOKEN", csrf);
                using var loginRes = await adminClient.SendAsync(loginReq);
                Assert.Equal(HttpStatusCode.OK, loginRes.StatusCode);
                using var loginDoc = JsonDocument.Parse(await loginRes.Content.ReadAsStringAsync());
                Assert.Equal(adminUsername, loginDoc.RootElement.GetProperty("username").GetString());
                Assert.Equal(adminEmail, loginDoc.RootElement.GetProperty("email").GetString());

                // Fetch fresh CSRF after authentication (antiforgery binds to authenticated user identity!)
                var authedCsrf = await FetchCsrfHeaderTokenAsync(adminClient);
                using var initReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/users/{targetId}/password/reset");
                initReq.Headers.Add("X-CSRF-TOKEN", authedCsrf);
                using var initRes = await adminClient.SendAsync(initReq);
                Assert.Equal(HttpStatusCode.NoContent, initRes.StatusCode);
                Assert.Empty(await initRes.Content.ReadAsStringAsync());
            }

            Assert.NotNull(spy.LastDelivery);
            Assert.Equal(targetId, spy.LastDelivery!.TargetUserId);
            Assert.Equal("ACCOUNT_ACTIVATION", spy.LastDelivery.Purpose);
            var singleUseToken = spy.LastDelivery.SingleUseToken;

            // 3. Target user activates account using single-use token, and token replay is rejected
            using (var userClient = CreateCookieClient(factory))
            {
                var csrf = await FetchCsrfHeaderTokenAsync(userClient);
                using var completeReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/password/reset/complete")
                {
                    Content = JsonContent.Create(new
                    {
                        email = targetEmail,
                        token = singleUseToken,
                        newPassword = activatedPass
                    })
                };
                completeReq.Headers.Add("X-CSRF-TOKEN", csrf);
                using var completeRes = await userClient.SendAsync(completeReq);
                Assert.Equal(HttpStatusCode.NoContent, completeRes.StatusCode);

                // Token replay MUST fail
                using var replayReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/password/reset/complete")
                {
                    Content = JsonContent.Create(new
                    {
                        email = targetEmail,
                        token = singleUseToken,
                        newPassword = "ReplayAttempt!2026"
                    })
                };
                replayReq.Headers.Add("X-CSRF-TOKEN", csrf);
                using var replayRes = await userClient.SendAsync(replayReq);
                Assert.Equal(HttpStatusCode.BadRequest, replayRes.StatusCode);
            }

            // 4. Target user signs in, verifies /me, tests cookie replay rejection after Logout
            string capturedCookieHeader;
            using (var targetClient = CreateCookieClient(factory))
            {
                var csrf = await FetchCsrfHeaderTokenAsync(targetClient);
                using var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
                {
                    Content = JsonContent.Create(new { email = targetEmail, password = activatedPass })
                };
                loginReq.Headers.Add("X-CSRF-TOKEN", csrf);
                using var loginRes = await targetClient.SendAsync(loginReq);
                Assert.Equal(HttpStatusCode.OK, loginRes.StatusCode);

                var setCookies = loginRes.Headers.GetValues("Set-Cookie").ToList();
                var sessionSetCookie = setCookies.Single(c => c.StartsWith("ApiGov.Session=", StringComparison.Ordinal));
                Assert.Contains("httponly", sessionSetCookie, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("samesite=lax", sessionSetCookie, StringComparison.OrdinalIgnoreCase);
                capturedCookieHeader = sessionSetCookie.Split(';', 2)[0];

                using var meRes = await targetClient.GetAsync("/api/v1/me");
                Assert.Equal(HttpStatusCode.OK, meRes.StatusCode);
                using var meDoc = JsonDocument.Parse(await meRes.Content.ReadAsStringAsync());
                Assert.Equal(targetEmail, meDoc.RootElement.GetProperty("email").GetString());

                // User without users.password.reset permission MUST get 403 Forbidden
                var authedCsrf = await FetchCsrfHeaderTokenAsync(targetClient);
                using var forbiddenResetReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/users/{adminId}/password/reset");
                forbiddenResetReq.Headers.Add("X-CSRF-TOKEN", authedCsrf);
                using var forbiddenResetRes = await targetClient.SendAsync(forbiddenResetReq);
                Assert.Equal(HttpStatusCode.Forbidden, forbiddenResetRes.StatusCode);

                // Logout revokes server-side session ticket
                using var logoutReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
                logoutReq.Headers.Add("X-CSRF-TOKEN", authedCsrf);
                using var logoutRes = await targetClient.SendAsync(logoutReq);
                Assert.Equal(HttpStatusCode.NoContent, logoutRes.StatusCode);

                // Normal client is now logged out -> 401
                using var afterLogoutMe = await targetClient.GetAsync("/api/v1/me");
                Assert.Equal(HttpStatusCode.Unauthorized, afterLogoutMe.StatusCode);
            }

            // Replaying the captured old session cookie from a raw client MUST fail (ticket revoked in DB!)
            using (var replayCookieClient = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false }))
            {
                using var replayMeReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
                replayMeReq.Headers.Add("Cookie", capturedCookieHeader);
                using var replayMeRes = await replayCookieClient.SendAsync(replayMeReq);
                Assert.Equal(HttpStatusCode.Unauthorized, replayMeRes.StatusCode);
            }

            // 5. Login, Change Password -> invalidates all sessions, rejects old password, accepts new password
            using (var changeClient = CreateCookieClient(factory))
            {
                var csrf = await FetchCsrfHeaderTokenAsync(changeClient);
                using var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
                {
                    Content = JsonContent.Create(new { email = targetEmail, password = activatedPass })
                };
                loginReq.Headers.Add("X-CSRF-TOKEN", csrf);
                using var loginRes = await changeClient.SendAsync(loginReq);
                Assert.Equal(HttpStatusCode.OK, loginRes.StatusCode);

                var authedCsrf = await FetchCsrfHeaderTokenAsync(changeClient);

                // Password policy enforcement: 14-char strong password MUST be rejected (minimum is 15 chars!)
                using (var shortPassReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/password/change")
                {
                    Content = JsonContent.Create(new
                    {
                        currentPassword = activatedPass,
                        newPassword = "Short14P@ss!99" // exactly 14 chars
                    })
                })
                {
                    shortPassReq.Headers.Add("X-CSRF-TOKEN", authedCsrf);
                    using var shortPassRes = await changeClient.SendAsync(shortPassReq);
                    Assert.Equal(HttpStatusCode.BadRequest, shortPassRes.StatusCode);
                }

                // Exact 15+ char compliant password succeeds
                using var cpReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/password/change")
                {
                    Content = JsonContent.Create(new
                    {
                        currentPassword = activatedPass,
                        newPassword = changedPass
                    })
                };
                cpReq.Headers.Add("X-CSRF-TOKEN", authedCsrf);
                using var cpRes = await changeClient.SendAsync(cpReq);
                Assert.Equal(HttpStatusCode.NoContent, cpRes.StatusCode);

                // Session must be invalidated immediately
                using var postChangeMe = await changeClient.GetAsync("/api/v1/me");
                Assert.Equal(HttpStatusCode.Unauthorized, postChangeMe.StatusCode);

                // Old password must fail; new password must succeed
                var anonCsrf = await FetchCsrfHeaderTokenAsync(changeClient);
                using var oldPassReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
                {
                    Content = JsonContent.Create(new { email = targetEmail, password = activatedPass })
                };
                oldPassReq.Headers.Add("X-CSRF-TOKEN", anonCsrf);
                using var oldPassRes = await changeClient.SendAsync(oldPassReq);
                Assert.Equal(HttpStatusCode.Unauthorized, oldPassRes.StatusCode);

                using var newPassReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
                {
                    Content = JsonContent.Create(new { email = targetEmail, password = changedPass })
                };
                newPassReq.Headers.Add("X-CSRF-TOKEN", anonCsrf);
                using var newPassRes = await changeClient.SendAsync(newPassReq);
                Assert.Equal(HttpStatusCode.OK, newPassRes.StatusCode);
            }

            // 6. Verify audit_events logged auth lifecycle without storing plaintext passwords or tokens
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiGovernanceDbContext>();
            var targetAudits = await db.AuditEvents
                .Where(a => a.ResourceId == targetId)
                .OrderBy(a => a.RecordedAt)
                .ToListAsync();

            Assert.Contains(targetAudits, a => a.Operation == "AUTH_ACCOUNT_ACTIVATED");
            Assert.Contains(targetAudits, a => a.Operation == "AUTH_LOGIN_SUCCEEDED");
            Assert.Contains(targetAudits, a => a.Operation == "AUTH_PASSWORD_CHANGED");
            foreach (var audit in targetAudits)
            {
                var summary = audit.ChangeSummaryJson ?? "";
                Assert.DoesNotContain(activatedPass, summary, StringComparison.Ordinal);
                Assert.DoesNotContain(changedPass, summary, StringComparison.Ordinal);
                Assert.DoesNotContain(singleUseToken, summary, StringComparison.Ordinal);
            }
        }
        finally
        {
            await factory.DisposeAsync();
            if (Directory.Exists(dpDir)) Directory.Delete(dpDir, recursive: true);
        }
    }

    [Fact]
    public async Task SecurityPolicies_EnforceLockout429_ImmediateRejectionOnDeactivation_AndSecureCookieInProduction()
    {
        await EnsureSchemaMigratedAsync();
        var (factory, _, dpDir) = CreateAuthFactory("Testing");
        try
        {
            const string validPass = "LockoutPolicy!2026";
            var (userId, email) = await SeedTestUserAsync(factory.Services, validPass, isActive: true);

            // 1. Deactivating an already-authenticated user immediately terminates access on next /me call
            using (var activeClient = CreateCookieClient(factory))
            {
                var csrf = await FetchCsrfHeaderTokenAsync(activeClient);
                using var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
                {
                    Content = JsonContent.Create(new { email, password = validPass })
                };
                loginReq.Headers.Add("X-CSRF-TOKEN", csrf);
                using var loginRes = await activeClient.SendAsync(loginReq);
                Assert.Equal(HttpStatusCode.OK, loginRes.StatusCode);

                // Deactivate user directly in DB (as I02 will later do)
                await using (var scope = factory.Services.CreateAsyncScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<ApiGovernanceDbContext>();
                    await db.Users.Where(u => u.Id == userId)
                        .ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, false));
                }

                // Next /me request MUST immediately return 401!
                using var deactivatedMe = await activeClient.GetAsync("/api/v1/me");
                Assert.Equal(HttpStatusCode.Unauthorized, deactivatedMe.StatusCode);
            }

            // Re-activate user for lockout test
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiGovernanceDbContext>();
                await db.Users.Where(u => u.Id == userId)
                    .ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, true));
            }

            // 2. Lockout after 5 consecutive failed password attempts -> 429 Too Many Requests
            using (var attackerClient = CreateCookieClient(factory))
            {
                var csrf = await FetchCsrfHeaderTokenAsync(attackerClient);

                for (var attempt = 1; attempt <= 4; attempt++)
                {
                    using var badReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
                    {
                        Content = JsonContent.Create(new { email, password = $"WrongAttempt#{attempt}" })
                    };
                    badReq.Headers.Add("X-CSRF-TOKEN", csrf);
                    using var badRes = await attackerClient.SendAsync(badReq);
                    Assert.Equal(HttpStatusCode.Unauthorized, badRes.StatusCode);
                }

                // 5th attempt hits MaxFailedAccessAttempts=5 -> locked -> 429
                using var fifthReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
                {
                    Content = JsonContent.Create(new { email, password = "WrongAttempt#5" })
                };
                fifthReq.Headers.Add("X-CSRF-TOKEN", csrf);
                using var fifthRes = await attackerClient.SendAsync(fifthReq);
                Assert.Equal(HttpStatusCode.TooManyRequests, fifthRes.StatusCode);

                // Subsequent login even with CORRECT password is blocked with 429 while locked
                using var blockedGoodReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
                {
                    Content = JsonContent.Create(new { email, password = validPass })
                };
                blockedGoodReq.Headers.Add("X-CSRF-TOKEN", csrf);
                using var blockedGoodRes = await attackerClient.SendAsync(blockedGoodReq);
                Assert.Equal(HttpStatusCode.TooManyRequests, blockedGoodRes.StatusCode);
            }

            // 3. Production environment emits Secure, HttpOnly, SameSite=Lax cookies
            var (prodFactory, _, prodDpDir) = CreateAuthFactory("Production");
            try
            {
                var (_, prodEmail) = await SeedTestUserAsync(prodFactory.Services, "ProdCookie!2026", isActive: true);
                using var prodClient = CreateCookieClient(prodFactory, https: true);
                var csrf = await FetchCsrfHeaderTokenAsync(prodClient);

                using var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
                {
                    Content = JsonContent.Create(new { email = prodEmail, password = "ProdCookie!2026" })
                };
                loginReq.Headers.Add("X-CSRF-TOKEN", csrf);
                using var loginRes = await prodClient.SendAsync(loginReq);
                Assert.Equal(HttpStatusCode.OK, loginRes.StatusCode);

                var sessionCookie = loginRes.Headers.GetValues("Set-Cookie")
                    .Single(c => c.StartsWith("ApiGov.Session=", StringComparison.Ordinal));
                Assert.Contains("secure", sessionCookie, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("httponly", sessionCookie, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("samesite=lax", sessionCookie, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                await prodFactory.DisposeAsync();
                if (Directory.Exists(prodDpDir)) Directory.Delete(prodDpDir, recursive: true);
            }

            // 4. Session rotation on re-login (SEC-T-006) & Expired session rejection (SEC-T-004)
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiGovernanceDbContext>();
                await db.Users.Where(u => u.Id == userId)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(u => u.AccessFailedCount, 0)
                        .SetProperty(u => u.LockoutEnd, (DateTimeOffset?)null));
            }

            using (var rotClient = CreateCookieClient(factory))
            {
                var csrf1 = await FetchCsrfHeaderTokenAsync(rotClient);
                using var login1Req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
                {
                    Content = JsonContent.Create(new { email, password = validPass })
                };
                login1Req.Headers.Add("X-CSRF-TOKEN", csrf1);
                using var login1Res = await rotClient.SendAsync(login1Req);
                Assert.Equal(HttpStatusCode.OK, login1Res.StatusCode);
                var oldCookie = login1Res.Headers.GetValues("Set-Cookie")
                    .Single(c => c.StartsWith("ApiGov.Session=", StringComparison.Ordinal))
                    .Split(';', 2)[0];

                // Re-authenticate while already holding a session -> must issue a NEW ticket and revoke oldCookie
                var csrf2 = await FetchCsrfHeaderTokenAsync(rotClient);
                using var login2Req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
                {
                    Content = JsonContent.Create(new { email, password = validPass })
                };
                login2Req.Headers.Add("X-CSRF-TOKEN", csrf2);
                using var login2Res = await rotClient.SendAsync(login2Req);
                Assert.Equal(HttpStatusCode.OK, login2Res.StatusCode);

                // Old cookie must now fail (401); new session must succeed (200)
                using var oldReplayClient = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
                using var oldReplayReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
                oldReplayReq.Headers.Add("Cookie", oldCookie);
                using var oldReplayRes = await oldReplayClient.SendAsync(oldReplayReq);
                Assert.Equal(HttpStatusCode.Unauthorized, oldReplayRes.StatusCode);

                using var activeMe = await rotClient.GetAsync("/api/v1/me");
                Assert.Equal(HttpStatusCode.OK, activeMe.StatusCode);

                // Force session expiration in DB -> next request must return 401 (SEC-T-004)
                await using var expScope = factory.Services.CreateAsyncScope();
                var expDb = expScope.ServiceProvider.GetRequiredService<ApiGovernanceDbContext>();
                var past = DateTimeOffset.UtcNow.AddMinutes(-5);
                await expDb.AuthSessions.Where(s => s.UserId == userId && s.RevokedAt == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresAt, past));

                using var expiredMe = await rotClient.GetAsync("/api/v1/me");
                Assert.Equal(HttpStatusCode.Unauthorized, expiredMe.StatusCode);
            }

            // 5. Endpoint Rate Limiter returns 429 Problem Details even on nonexistent accounts
            var rateLimitDpDir = Path.Combine(Path.GetTempPath(), "ApiGovTestDP", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(rateLimitDpDir);
            await using var rlFactory = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder =>
                {
                    builder.UseEnvironment("Testing");
                    builder.ConfigureAppConfiguration((_, configBuilder) =>
                    {
                        configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                        {
                            ["ConnectionStrings:DefaultConnection"] = BackendContractIntegrationTests.GetIsolatedTestDatabaseConnectionString(),
                            ["AuthenticationPolicy:DataProtectionKeyDirectory"] = rateLimitDpDir,
                            ["AuthenticationPolicy:LoginRateLimitPermitLimit"] = "3",
                            ["AuthenticationPolicy:LoginRateLimitWindowSeconds"] = "60"
                        });
                    });
                });
            try
            {
                using var rlClient = CreateCookieClient(rlFactory);
                var csrf = await FetchCsrfHeaderTokenAsync(rlClient);
                for (var i = 1; i <= 3; i++)
                {
                    using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
                    {
                        Content = JsonContent.Create(new { email = $"unknown-{i}@api-governance.local", password = "SomePassword!2026" })
                    };
                    req.Headers.Add("X-CSRF-TOKEN", csrf);
                    using var res = await rlClient.SendAsync(req);
                    Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
                }

                // 4th attempt exceeds permit limit -> 429 Too Many Requests Problem Details
                using var blockedReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
                {
                    Content = JsonContent.Create(new { email = "unknown-4@api-governance.local", password = "SomePassword!2026" })
                };
                blockedReq.Headers.Add("X-CSRF-TOKEN", csrf);
                using var blockedRes = await rlClient.SendAsync(blockedReq);
                Assert.Equal(HttpStatusCode.TooManyRequests, blockedRes.StatusCode);
                Assert.Equal("application/problem+json", blockedRes.Content.Headers.ContentType?.MediaType);
            }
            finally
            {
                await rlFactory.DisposeAsync();
                if (Directory.Exists(rateLimitDpDir)) Directory.Delete(rateLimitDpDir, recursive: true);
            }
        }
        finally
        {
            await factory.DisposeAsync();
            if (Directory.Exists(dpDir)) Directory.Delete(dpDir, recursive: true);
        }
    }
}
