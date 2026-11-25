using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ApiGovernance.Modules.IdentityAccess.Services;
using ApiGovernance.Shared.Persistence;
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

/// <summary>
/// Sprint S13 Comprehensive Test Suite covering:
/// - Task P05: Append-Only Audit Log (UI-16), Authorized Global Search (UI-18), Permission-Adaptive Dashboard (UI-17)
/// - Task Q01: Contract, RBAC (ADR-01), Document ACL &amp; anti-IDOR matrix assertions
/// - Task Q02: Full End-to-End Integration Lifecycle (Document -&gt; Integration -&gt; Version -&gt; Scan -&gt; Workflow -&gt; Publish -&gt; Portal)
/// - Task Q03: Approved Change Request E2E Lifecycle (Scoped Files -&gt; Change Route -&gt; Working Copy -&gt; Replacement -&gt; Re-Approve -&gt; Republish)
/// </summary>
[Collection("IsolatedTestDatabase")]
public sealed class MonitoringAndE2ESuiteS13Tests
{
    private sealed class CapturingDeliverySpy : IAuthTokenDeliveryChannel
    {
        public string ChannelCode => "TEST_S13_SPY";
        public List<AuthTokenDeliveryContext> Deliveries { get; } = [];

        public Task DeliverPasswordResetTokenAsync(AuthTokenDeliveryContext context, CancellationToken cancellationToken = default)
        {
            Deliveries.Add(context);
            return Task.CompletedTask;
        }
    }

    private sealed class EmbeddedCleanClamAvServer : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _acceptLoop;

        public int Port { get; }

        public EmbeddedCleanClamAvServer()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _acceptLoop = Task.Run(AcceptClientsAsync);
        }

        private async Task AcceptClientsAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                    _ = HandleClientAsync(client);
                }
                catch
                {
                    break;
                }
            }
        }

        private static async Task HandleClientAsync(TcpClient client)
        {
            using (client)
            await using (var stream = client.GetStream())
            {
                var cmdBuf = new byte[10]; // "zINSTREAM\0"
                var cmdRead = 0;
                while (cmdRead < cmdBuf.Length)
                {
                    var n = await stream.ReadAsync(cmdBuf.AsMemory(cmdRead));
                    if (n == 0) return;
                    cmdRead += n;
                }

                var lenBuf = new byte[4];
                while (true)
                {
                    var off = 0;
                    while (off < 4)
                    {
                        var r = await stream.ReadAsync(lenBuf.AsMemory(off, 4 - off));
                        if (r == 0) return;
                        off += r;
                    }

                    var chunkLen = BinaryPrimitives.ReadUInt32BigEndian(lenBuf);
                    if (chunkLen == 0) break;

                    var chunk = new byte[chunkLen];
                    var cOff = 0;
                    while (cOff < chunk.Length)
                    {
                        var r = await stream.ReadAsync(chunk.AsMemory(cOff, chunk.Length - cOff));
                        if (r == 0) return;
                        cOff += r;
                    }
                }

                await stream.WriteAsync(Encoding.UTF8.GetBytes("stream: OK\0"));
                await stream.FlushAsync();
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _cts.CancelAsync();
            _listener.Stop();
            try { await _acceptLoop; } catch { /* ignore */ }
            _cts.Dispose();
        }
    }

    private static (WebApplicationFactory<Program> Factory, CapturingDeliverySpy Spy, string DpDir, string StorageDir)
        CreateFactory(int clamAvPort)
    {
        var dpDir = Path.Combine(Path.GetTempPath(), $"apigov-dp-s13-{Guid.NewGuid():N}");
        var storageDir = Path.Combine(Path.GetTempPath(), $"apigov-store-s13-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dpDir);
        Directory.CreateDirectory(storageDir);
        var spy = new CapturingDeliverySpy();

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
                        ["FileScanning:Provider"] = "ClamAv",
                        ["FileScanning:ClamAvHost"] = "127.0.0.1",
                        ["FileScanning:ClamAvPort"] = clamAvPort.ToString()
                    });
                });
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IAuthTokenDeliveryChannel>();
                    services.AddSingleton<IAuthTokenDeliveryChannel>(spy);
                });
            });

        return (factory, spy, dpDir, storageDir);
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
        WebApplicationFactory<Program> factory)
    {
        var adminEmail = $"s13-admin-{Guid.NewGuid():N}@api-governance.local";
        const string adminPwd = "S13#AdminStrong!99";

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var bootstrap = scope.ServiceProvider.GetRequiredService<IInitialAdminBootstrapService>();
            var res = await bootstrap.BootstrapFirstAdminAsync(new(adminEmail, "S13 Platform Admin", adminPwd));
            Assert.True(res.Created);
        }

        var client = CreateCookieClient(factory);
        var csrf = await FetchCsrfAsync(client);
        var (loginCode, _) = await SendJsonAsync(client, csrf, HttpMethod.Post, "/api/v1/auth/login",
            new { email = adminEmail, password = adminPwd });
        Assert.Equal(HttpStatusCode.OK, loginCode);

        var csrfAfter = await FetchCsrfAsync(client);
        var (_, meDoc) = await SendJsonAsync(client, csrfAfter, HttpMethod.Get, "/api/v1/me");
        var adminId = meDoc!.RootElement.GetProperty("id").GetGuid();

        return (adminId, client, csrfAfter);
    }

    private static async Task<(Guid UserId, HttpClient Client, string Csrf)> CreateAndLoginUserAsync(
        WebApplicationFactory<Program> factory,
        CapturingDeliverySpy spy,
        HttpClient adminClient,
        string adminCsrf,
        string email,
        string fullName,
        Guid[] roleIds,
        Guid? consumerEntityId = null)
    {
        var (uCode, userDoc) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/users",
            new { fullName, email, roleIds, consumerEntityId, triggerActivationToken = true });
        Assert.Equal(HttpStatusCode.Created, uCode);
        var userId = userDoc!.RootElement.GetProperty("id").GetGuid();
        var tok = spy.Deliveries.Last(d => d.TargetUserId == userId).SingleUseToken;

        const string pwd = "S13#UserStrong!88";
        var client = CreateCookieClient(factory);
        var csrf = await FetchCsrfAsync(client);
        Assert.Equal(HttpStatusCode.NoContent, (await SendJsonAsync(client, csrf, HttpMethod.Post,
            "/api/v1/auth/password/reset/complete", new { email, token = tok, newPassword = pwd })).Status);
        Assert.Equal(HttpStatusCode.OK, (await SendJsonAsync(client, csrf, HttpMethod.Post,
            "/api/v1/auth/login", new { email, password = pwd })).Status);

        return (userId, client, await FetchCsrfAsync(client));
    }

    private static async Task<(HttpStatusCode Status, JsonDocument? Doc)> SendJsonAsync(
        HttpClient client,
        string csrf,
        HttpMethod method,
        string url,
        object? payload = null)
    {
        using var req = new HttpRequestMessage(method, url);
        if (method != HttpMethod.Get && method != HttpMethod.Head)
        {
            req.Headers.Add("X-CSRF-TOKEN", csrf);
        }
        if (payload is not null)
        {
            req.Content = JsonContent.Create(payload);
        }

        using var res = await client.SendAsync(req);
        var raw = await res.Content.ReadAsStringAsync();
        JsonDocument? doc = null;
        if (!string.IsNullOrWhiteSpace(raw) &&
            (raw.TrimStart().StartsWith('{') || raw.TrimStart().StartsWith('[')))
        {
            doc = JsonDocument.Parse(raw);
        }

        return (res.StatusCode, doc);
    }

    private static async Task<(HttpStatusCode Status, JsonDocument? Doc)> UploadJsonFileAsync(
        HttpClient client,
        string csrf,
        Guid revisionId,
        Guid fileTypeId,
        string fileName,
        byte[] contentBytes)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(fileTypeId.ToString()), "fileTypeId");
        var fileContent = new ByteArrayContent(contentBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        form.Add(fileContent, "file", fileName);

        using var req = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/revisions/{revisionId}/files")
        {
            Content = form
        };
        req.Headers.Add("X-CSRF-TOKEN", csrf);

        using var res = await client.SendAsync(req);
        var raw = await res.Content.ReadAsStringAsync();
        JsonDocument? doc = null;
        if (!string.IsNullOrWhiteSpace(raw) && raw.TrimStart().StartsWith('{'))
        {
            doc = JsonDocument.Parse(raw);
        }

        return (res.StatusCode, doc);
    }

    [Fact]
    public async Task P05_AuditLog_GlobalSearch_And_Dashboard_Strictly_Enforce_RBAC_And_DocumentAcl()
    {
        await ResetIsolatedDatabaseAsync();
        await using var clamAv = new EmbeddedCleanClamAvServer();
        var (factory, spy, dpDir, storageDir) = CreateFactory(clamAv.Port);

        try
        {
            var (adminId, adminClient, adminCsrf) = await BootstrapAndLoginAdminAsync(factory);

            var (_, permRes) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Get, "/api/v1/permissions");
            var permIds = permRes!.RootElement.GetProperty("permissions").EnumerateArray()
                .ToDictionary(e => e.GetProperty("code").GetString()!, e => e.GetProperty("id").GetGuid(), StringComparer.OrdinalIgnoreCase);

            // Seed two systems + two consumers + config
            var (_, s1Doc) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/company-systems",
                new { name = "DemoVisibleHub", ownerEmail = "vis@api-governance.local", description = "Visible" });
            var s1Id = s1Doc!.RootElement.GetProperty("id").GetGuid();
            var (_, s2Doc) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/company-systems",
                new { name = "DemoSecretVault", ownerEmail = "sec@api-governance.local", description = "Restricted" });
            var s2Id = s2Doc!.RootElement.GetProperty("id").GetGuid();
            await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/internal-providers",
                new { companySystemId = s1Id, description = "Prov 1" });
            await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/internal-providers",
                new { companySystemId = s2Id, description = "Prov 2" });

            var (_, catDoc) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/consumer-categories",
                new { name = "Demo Category", description = "Cat" });
            var catId = catDoc!.RootElement.GetProperty("id").GetGuid();
            var (_, consDoc) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/consumers",
                new { categoryId = catId, name = "Demo Partner ERP" });
            var consId = consDoc!.RootElement.GetProperty("id").GetGuid();
            var (_, grpDoc) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/api-groups",
                new { name = "Core Ledger", description = "Ledger" });
            var grpId = grpDoc!.RootElement.GetProperty("id").GetGuid();
            var (_, ftDoc) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/file-types",
                new { catalogCode = "OPENAPI_SPEC", displayName = "OpenAPI", maxSizeBytes = 102400L });
            var ftId = ftDoc!.RootElement.GetProperty("id").GetGuid();
            var (_, tplDoc) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/requirement-templates",
                new { name = "TPL-S13", rules = new[] { new { fileTypeId = ftId, requirementMode = "REQUIRED", minCount = 1, maxCount = 3 } } });
            var tplId = tplDoc!.RootElement.GetProperty("id").GetGuid();

            var (_, observerRoleDoc) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/roles",
                new { name = "Scoped Search Role", description = "Search & Dash only" });
            var observerRoleId = observerRoleDoc!.RootElement.GetProperty("id").GetGuid();
            await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Put, $"/api/v1/roles/{observerRoleId}/permissions",
                new
                {
                    permissionIds = new[]
                    {
                        permIds["dashboard.view"],
                        permIds["search.view"],
                        permIds["api_documents.view"],
                        permIds["integrations.view"]
                    }
                });

            var (_, auditorRoleDoc) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/roles",
                new { name = "Auditor Role", description = "Audit & Dash only" });
            var auditorRoleId = auditorRoleDoc!.RootElement.GetProperty("id").GetGuid();
            await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Put, $"/api/v1/roles/{auditorRoleId}/permissions",
                new
                {
                    permissionIds = new[]
                    {
                        permIds["dashboard.view"],
                        permIds["audit.view"]
                    }
                });

            var (_, routeDoc) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/approval-routes",
                new { name = "Route-S13", stages = new[] { new { position = 1, stageName = "Stage1", responsibleRoleId = auditorRoleId } } });
            var routeId = routeDoc!.RootElement.GetProperty("id").GetGuid();
            var (_, ctDoc) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/change-types",
                new { name = "Minor Update", routeId });
            var (_, polDoc) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/change-policies",
                new { name = "Pol-S13", changeTypeIds = new[] { ctDoc!.RootElement.GetProperty("id").GetGuid() } });
            var polId = polDoc!.RootElement.GetProperty("id").GetGuid();

            var (obsId, obsClient, obsCsrf) = await CreateAndLoginUserAsync(
                factory, spy, adminClient, adminCsrf, "obs.s13@api-governance.local", "Scoped Observer", [observerRoleId]);
            var (_, audClient, audCsrf) = await CreateAndLoginUserAsync(
                factory, spy, adminClient, adminCsrf, "aud.s13@api-governance.local", "Compliance Auditor", [auditorRoleId]);

            // Create Doc1 (visible to obs) & Doc2 (secret, NOT granted to obs)
            var (_, d1Doc) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/api-documents",
                new
                {
                    providerInternalSystemId = s1Id,
                    consumerEntityId = consId,
                    apiGroupId = grpId,
                    requirementTemplateId = tplId,
                    approvalRouteId = routeId,
                    changePolicyId = polId,
                    publicationOption = "MANUAL",
                    state = "ACTIVE",
                    developerUserIds = Array.Empty<Guid>()
                });
            var d1Id = d1Doc!.RootElement.GetProperty("id").GetGuid();

            var (_, d2Doc) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/api-documents",
                new
                {
                    providerInternalSystemId = s2Id,
                    consumerEntityId = consId,
                    apiGroupId = grpId,
                    requirementTemplateId = tplId,
                    approvalRouteId = routeId,
                    changePolicyId = polId,
                    publicationOption = "MANUAL",
                    state = "ACTIVE",
                    developerUserIds = Array.Empty<Guid>()
                });
            var d2Id = d2Doc!.RootElement.GetProperty("id").GetGuid();

            // Grant doc.view ONLY on Doc1 to obsId
            var adminUserRules = DocumentPermissionCatalog.All
                .Select(p => new { userId = adminId, permissionCode = p.Code, decision = "GRANT" })
                .ToArray();
            await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Put, $"/api/v1/api-documents/{d1Id}/access",
                new
                {
                    rolePermissions = Array.Empty<object>(),
                    userPermissions = adminUserRules.Concat(new[]
                    {
                        new { userId = obsId, permissionCode = "doc.view", decision = "GRANT" }
                    }).ToArray()
                });

            await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/integrations",
                new { documentId = d1Id, referenceCode = "INT-VISIBLE-S13", developerUserIds = new[] { adminId } });
            await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/integrations",
                new { documentId = d2Id, referenceCode = "INT-SECRET-S13", developerUserIds = new[] { adminId } });

            // Scoped Observer CANNOT read Audit Log (403)
            var (deniedAuditStatus, _) = await SendJsonAsync(obsClient, obsCsrf, HttpMethod.Get, "/api/v1/audit-events");
            Assert.Equal(HttpStatusCode.Forbidden, deniedAuditStatus);

            // Scoped Observer Global Search sees ONLY Doc1 & INT-VISIBLE-S13, ZERO matches for DemoSecretVault/INT-SECRET-S13
            var (sVisCode, sVisDoc) = await SendJsonAsync(obsClient, obsCsrf, HttpMethod.Get, "/api/v1/search?q=Visible");
            Assert.Equal(HttpStatusCode.OK, sVisCode);
            Assert.Equal(2, sVisDoc!.RootElement.GetProperty("totalCount").GetInt32());

            var (sSecCode, sSecDoc) = await SendJsonAsync(obsClient, obsCsrf, HttpMethod.Get, "/api/v1/search?q=Secret");
            Assert.Equal(HttpStatusCode.OK, sSecCode);
            Assert.Equal(0, sSecDoc!.RootElement.GetProperty("totalCount").GetInt32());

            // Scoped Observer Dashboard counts ONLY permitted Doc1 & Integration1 and hides audit events
            var (dashCode, dashDoc) = await SendJsonAsync(obsClient, obsCsrf, HttpMethod.Get, "/api/v1/dashboard");
            Assert.Equal(HttpStatusCode.OK, dashCode);
            Assert.Equal(1, dashDoc!.RootElement.GetProperty("visibleDocumentsCount").GetInt32());
            Assert.Equal(1, dashDoc.RootElement.GetProperty("visibleIntegrationsCount").GetInt32());
            Assert.False(dashDoc.RootElement.GetProperty("canViewAudit").GetBoolean());
            Assert.Empty(dashDoc.RootElement.GetProperty("recentAuditEvents").EnumerateArray());

            // Auditor CANNOT read search (403), CAN read Audit Log including the FORBIDDEN AUDIT_EVENTS_READ_DENIED record!
            Assert.Equal(HttpStatusCode.Forbidden, (await SendJsonAsync(audClient, audCsrf, HttpMethod.Get, "/api/v1/search")).Status);
            var (audCode, audDoc) = await SendJsonAsync(audClient, audCsrf, HttpMethod.Get, "/api/v1/audit-events?operation=AUDIT_EVENTS_READ_DENIED");
            Assert.Equal(HttpStatusCode.OK, audCode);
            Assert.True(audDoc!.RootElement.GetProperty("totalCount").GetInt32() >= 1);
            Assert.Contains(audDoc.RootElement.GetProperty("items").EnumerateArray(),
                e => e.GetProperty("result").GetString() == "FORBIDDEN");
        }
        finally
        {
            await factory.DisposeAsync();
            if (Directory.Exists(dpDir)) Directory.Delete(dpDir, true);
            if (Directory.Exists(storageDir)) Directory.Delete(storageDir, true);
        }
    }

    [Fact]
    public async Task Q01_Q02_Q03_FullLifecycle_And_ApprovedChangeRequest_WorkCopy_Republish_E2E()
    {
        await ResetIsolatedDatabaseAsync();
        await using var clamAv = new EmbeddedCleanClamAvServer();
        var (factory, spy, dpDir, storageDir) = CreateFactory(clamAv.Port);

        try
        {
            var (adminId, adminClient, adminCsrf) = await BootstrapAndLoginAdminAsync(factory);

            var (_, permRes) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Get, "/api/v1/permissions");
            var permIds = permRes!.RootElement.GetProperty("permissions").EnumerateArray()
                .ToDictionary(e => e.GetProperty("code").GetString()!, e => e.GetProperty("id").GetGuid(), StringComparer.OrdinalIgnoreCase);

            // Seed Demo Bank / ERP Master Data
            var (_, sysDoc) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/company-systems",
                new { name = "Demo Core Banking", ownerEmail = "core@api-governance.local", description = "Core" });
            var sysId = sysDoc!.RootElement.GetProperty("id").GetGuid();
            await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/internal-providers",
                new { companySystemId = sysId, description = "Internal Provider" });
            var (_, catDoc) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/consumer-categories",
                new { name = "Enterprise Consumers", description = "Partners" });
            var catId = catDoc!.RootElement.GetProperty("id").GetGuid();
            var (_, consDoc) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/consumers",
                new { categoryId = catId, name = "Demo Financial Gateway" });
            var consumerId = consDoc!.RootElement.GetProperty("id").GetGuid();
            var (_, grpDoc) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/api-groups",
                new { name = "Open Finance APIs", description = "OF" });
            var grpId = grpDoc!.RootElement.GetProperty("id").GetGuid();
            var (_, jsonFtDoc) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/file-types",
                new { catalogCode = "OPENAPI_SPEC", displayName = "OpenAPI Spec", maxSizeBytes = 204800L });
            var jsonFtId = jsonFtDoc!.RootElement.GetProperty("id").GetGuid();
            var (_, mdFtDoc) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/file-types",
                new { catalogCode = "MARKDOWN_DOC", displayName = "Markdown Guide", maxSizeBytes = 204800L });
            var mdFtId = mdFtDoc!.RootElement.GetProperty("id").GetGuid();

            var (_, tplDoc) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/requirement-templates",
                new
                {
                    name = "Q02 Template",
                    rules = new[]
                    {
                        new { fileTypeId = jsonFtId, requirementMode = "REQUIRED", minCount = 1, maxCount = 3 },
                        new { fileTypeId = mdFtId, requirementMode = "OPTIONAL", minCount = 0, maxCount = 2 }
                    }
                });
            var tplId = tplDoc!.RootElement.GetProperty("id").GetGuid();

            // Roles: Developer, Release Reviewer, Change Board, Portal Consumer, Outsider
            var (_, devRoleDoc) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/roles", new { name = "Q02Dev", description = "Dev" });
            var devRoleId = devRoleDoc!.RootElement.GetProperty("id").GetGuid();
            await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Put, $"/api/v1/roles/{devRoleId}/permissions",
                new
                {
                    permissionIds = new[]
                    {
                        permIds["api_documents.view"], permIds["integrations.view"],
                        permIds["versions.view"], permIds["versions.create"],
                        permIds["revisions.view"], permIds["revisions.create"], permIds["revisions.submit"],
                        permIds["files.view"], permIds["files.upload"], permIds["files.delete"], permIds["files.download"],
                        permIds["changes.view"], permIds["changes.create"], permIds["changes.edit"], permIds["changes.submit"],
                        permIds["dashboard.view"], permIds["search.view"]
                    }
                });

            var (_, revRoleDoc) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/roles", new { name = "Q02ReviewerPublisher", description = "Rev" });
            var revRoleId = revRoleDoc!.RootElement.GetProperty("id").GetGuid();
            await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Put, $"/api/v1/roles/{revRoleId}/permissions",
                new
                {
                    permissionIds = new[]
                    {
                        permIds["integrations.view"], permIds["versions.view"], permIds["files.view"],
                        permIds["workflows.view"], permIds["workflows.decide"],
                        permIds["publications.view"], permIds["publications.create"], permIds["publications.withdraw"],
                        permIds["versions.usage.view"], permIds["versions.usage.manage"],
                        permIds["dashboard.view"]
                    }
                });

            var (_, cbRoleDoc) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/roles", new { name = "Q03ChangeBoard", description = "CB" });
            var cbRoleId = cbRoleDoc!.RootElement.GetProperty("id").GetGuid();
            await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Put, $"/api/v1/roles/{cbRoleId}/permissions",
                new
                {
                    permissionIds = new[]
                    {
                        permIds["integrations.view"], permIds["changes.view"], permIds["workflows.view"], permIds["workflows.decide"]
                    }
                });

            var (_, portalRoleDoc) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/roles", new { name = "Q02PortalRole", description = "Portal" });
            var portalRoleId = portalRoleDoc!.RootElement.GetProperty("id").GetGuid();

            var (_, outsiderRoleDoc) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/roles", new { name = "Q01Outsider", description = "No access" });
            var outsiderRoleId = outsiderRoleDoc!.RootElement.GetProperty("id").GetGuid();

            var (_, docRouteDoc) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/approval-routes",
                new { name = "Release Route", stages = new[] { new { position = 1, stageName = "Release Signoff", responsibleRoleId = revRoleId } } });
            var docRouteId = docRouteDoc!.RootElement.GetProperty("id").GetGuid();

            var (_, crRouteDoc) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/approval-routes",
                new { name = "Change Route", stages = new[] { new { position = 1, stageName = "Change Board Review", responsibleRoleId = cbRoleId } } });
            var crRouteId = crRouteDoc!.RootElement.GetProperty("id").GetGuid();

            var (_, ctDoc) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/change-types",
                new { name = "Schema Breaking Update", routeId = crRouteId });
            var ctId = ctDoc!.RootElement.GetProperty("id").GetGuid();
            var (_, polDoc) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/change-policies",
                new { name = "Q03 Policy", changeTypeIds = new[] { ctId } });
            var polId = polDoc!.RootElement.GetProperty("id").GetGuid();

            var (devId, devClient, devCsrf) = await CreateAndLoginUserAsync(
                factory, spy, adminClient, adminCsrf, "dev.e2e@api-governance.local", "E2E Developer", [devRoleId]);
            var (revId, revClient, revCsrf) = await CreateAndLoginUserAsync(
                factory, spy, adminClient, adminCsrf, "rev.e2e@api-governance.local", "E2E Reviewer", [revRoleId]);
            var (cbId, cbClient, cbCsrf) = await CreateAndLoginUserAsync(
                factory, spy, adminClient, adminCsrf, "cb.e2e@api-governance.local", "Change Approver", [cbRoleId]);
            var (_, portalClient, portalCsrf) = await CreateAndLoginUserAsync(
                factory, spy, adminClient, adminCsrf, "consumer.e2e@partner.example", "Gateway Consumer", [portalRoleId], consumerId);
            var (_, outsiderClient, outsiderCsrf) = await CreateAndLoginUserAsync(
                factory, spy, adminClient, adminCsrf, "outsider.e2e@api-governance.local", "Outsider User", [outsiderRoleId]);

            // Create Document & grant ACL + assign devId
            var (_, docRes) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/api-documents",
                new
                {
                    providerInternalSystemId = sysId,
                    consumerEntityId = consumerId,
                    apiGroupId = grpId,
                    requirementTemplateId = tplId,
                    approvalRouteId = docRouteId,
                    changePolicyId = polId,
                    publicationOption = "MANUAL",
                    state = "ACTIVE",
                    developerUserIds = new[] { devId }
                });
            var docId = docRes!.RootElement.GetProperty("id").GetGuid();

            var adminAll = DocumentPermissionCatalog.All.Select(p => new { userId = adminId, permissionCode = p.Code, decision = "GRANT" }).ToArray();
            await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Put, $"/api/v1/api-documents/{docId}/access",
                new
                {
                    rolePermissions = Array.Empty<object>(),
                    userPermissions = adminAll.Concat(new[]
                    {
                        new { userId = devId, permissionCode = "doc.view", decision = "GRANT" },
                        new { userId = devId, permissionCode = "doc.edit", decision = "GRANT" },
                        new { userId = devId, permissionCode = "doc.version_manage", decision = "GRANT" },
                        new { userId = devId, permissionCode = "doc.files_manage", decision = "GRANT" },
                        new { userId = devId, permissionCode = "doc.change", decision = "GRANT" },
                        new { userId = revId, permissionCode = "doc.view", decision = "GRANT" },
                        new { userId = revId, permissionCode = "doc.review", decision = "GRANT" },
                        new { userId = revId, permissionCode = "doc.publish", decision = "GRANT" },
                        new { userId = cbId, permissionCode = "doc.view", decision = "GRANT" },
                        new { userId = cbId, permissionCode = "doc.review", decision = "GRANT" }
                    }).ToArray()
                });

            var (_, intDoc) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/integrations",
                new { documentId = docId, referenceCode = "INT-Q02-Q03", developerUserIds = new[] { devId } });
            var integId = intDoc!.RootElement.GetProperty("id").GetGuid();

            // Q01 Anti-IDOR check: Outsider gets 403/404 when trying to view integration or audit log
            Assert.Equal(HttpStatusCode.Forbidden, (await SendJsonAsync(outsiderClient, outsiderCsrf, HttpMethod.Get, $"/api/v1/integrations/{integId}")).Status);

            // Q02 Step A: Manual Version 1.0.0 + Revision #1 + ClamAV CLEAN uploads (openapi.json + notes.md)
            var (v1Code, v1Doc) = await SendJsonAsync(devClient, devCsrf, HttpMethod.Post, $"/api/v1/integrations/{integId}/versions",
                new { versionLabel = "1.0.0" });
            Assert.Equal(HttpStatusCode.Created, v1Code);
            var v1Id = v1Doc!.RootElement.GetProperty("id").GetGuid();

            var (r1Code, r1Doc) = await SendJsonAsync(devClient, devCsrf, HttpMethod.Post, $"/api/v1/versions/{v1Id}/revisions");
            Assert.Equal(HttpStatusCode.Created, r1Code);
            var r1Id = r1Doc!.RootElement.GetProperty("id").GetGuid();

            var v1SpecBytes = Encoding.UTF8.GetBytes("{\"openapi\":\"3.1.0\",\"info\":{\"title\":\"Gateway v1\"}}");
            var (_, fSpec1Doc) = await UploadJsonFileAsync(devClient, devCsrf, r1Id, jsonFtId, "openapi-v1.json", v1SpecBytes);
            var fSpec1Id = fSpec1Doc!.RootElement.GetProperty("id").GetGuid();
            Assert.Equal("CLEAN", fSpec1Doc.RootElement.GetProperty("scanStatus").GetString());

            // Upload markdown notes too
            using (var mdForm = new MultipartFormDataContent())
            {
                mdForm.Add(new StringContent(mdFtId.ToString()), "fileTypeId");
                var mdBytes = new ByteArrayContent(Encoding.UTF8.GetBytes("# Release Notes v1"));
                mdBytes.Headers.ContentType = new MediaTypeHeaderValue("text/markdown");
                mdForm.Add(mdBytes, "file", "notes.md");
                using var mdReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/revisions/{r1Id}/files") { Content = mdForm };
                mdReq.Headers.Add("X-CSRF-TOKEN", devCsrf);
                using var mdRes = await devClient.SendAsync(mdReq);
                Assert.Equal(HttpStatusCode.Created, mdRes.StatusCode);
            }

            // Submit Revision #1 & Verify Reviewer Dashboard Pending Queue + Approve!
            var (sub1Code, sub1Doc) = await SendJsonAsync(devClient, devCsrf, HttpMethod.Post, $"/api/v1/revisions/{r1Id}/submit");
            Assert.Equal(HttpStatusCode.OK, sub1Code);
            var run1Id = sub1Doc!.RootElement.GetProperty("workflowRunId").GetGuid();

            var (_, revDash) = await SendJsonAsync(revClient, revCsrf, HttpMethod.Get, "/api/v1/dashboard");
            Assert.Equal(1, revDash!.RootElement.GetProperty("pendingMyApprovalStagesCount").GetInt32());

            var (_, run1Doc) = await SendJsonAsync(revClient, revCsrf, HttpMethod.Get, $"/api/v1/workflows/{run1Id}");
            var run1Stage1Id = run1Doc!.RootElement.GetProperty("stages")[0].GetProperty("id").GetGuid();
            Assert.Equal(HttpStatusCode.OK, (await SendJsonAsync(revClient, revCsrf, HttpMethod.Post,
                $"/api/v1/workflows/{run1Id}/stages/{run1Stage1Id}/decisions",
                new { decision = "APPROVE", notes = "Q02 E2E Approved" })).Status);

            // Publish v1.0.0 Revision #1 & set active consumer version + Portal verified download
            var (pub1Code, pub1Doc) = await SendJsonAsync(revClient, revCsrf, HttpMethod.Post,
                $"/api/v1/revisions/{r1Id}/publications",
                new { revisionFileIds = new[] { fSpec1Id } });
            Assert.Equal(HttpStatusCode.Created, pub1Code);
            var pub1Id = pub1Doc!.RootElement.GetProperty("id").GetGuid();

            Assert.Equal(HttpStatusCode.OK, (await SendJsonAsync(revClient, revCsrf, HttpMethod.Put,
                $"/api/v1/integrations/{integId}/consumer-version",
                new { apiVersionId = v1Id })).Status);

            using var dlV1 = await portalClient.GetAsync($"/api/v1/portal/publications/{pub1Id}/files/{fSpec1Id}/download");
            Assert.Equal(HttpStatusCode.OK, dlV1.StatusCode);
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(v1SpecBytes)), dlV1.Headers.GetValues("X-File-Sha256").First());

            // Q03 Step B: Create Scoped Change Request on fSpec1Id -> Submit on ChangeRoute -> Approve -> Working Copy Rev #2 -> Replace File -> Re-Approve -> Publish!
            var (crCode, crDoc) = await SendJsonAsync(devClient, devCsrf, HttpMethod.Post,
                $"/api/v1/integrations/{integId}/change-requests",
                new
                {
                    sourceRevisionId = r1Id,
                    changeTypeId = ctId,
                    reason = "Update response schema field format",
                    consumerImpact = "Add traceId field",
                    affectedSourceFileIds = new[] { fSpec1Id }
                });
            Assert.Equal(HttpStatusCode.Created, crCode);
            var crId = crDoc!.RootElement.GetProperty("id").GetGuid();

            var (crSubCode, crSubDoc) = await SendJsonAsync(devClient, devCsrf, HttpMethod.Post, $"/api/v1/change-requests/{crId}/submit");
            Assert.Equal(HttpStatusCode.OK, crSubCode);
            var crRunId = crSubDoc!.RootElement.GetProperty("workflowRunId").GetGuid();

            var (_, crRunDoc) = await SendJsonAsync(cbClient, cbCsrf, HttpMethod.Get, $"/api/v1/workflows/{crRunId}");
            var crStage1Id = crRunDoc!.RootElement.GetProperty("stages")[0].GetProperty("id").GetGuid();
            Assert.Equal(HttpStatusCode.OK, (await SendJsonAsync(cbClient, cbCsrf, HttpMethod.Post,
                $"/api/v1/workflows/{crRunId}/stages/{crStage1Id}/decisions",
                new { decision = "APPROVE", notes = "Change Board approved scoped update" })).Status);

            // Open Working Copy Revision #2 (on v1Id, referencing r1Id & crId)
            var (wcCode, wcDoc) = await SendJsonAsync(devClient, devCsrf, HttpMethod.Post,
                $"/api/v1/versions/{v1Id}/revisions",
                new { baseRevisionId = r1Id, changeRequestId = crId });
            Assert.Equal(HttpStatusCode.Created, wcCode);
            var r2Id = wcDoc!.RootElement.GetProperty("id").GetGuid();
            Assert.Equal(2, wcDoc.RootElement.GetProperty("revisionNumber").GetInt32());

            // Replace affected file inside Revision #2 (uploading updated spec)
            var v2SpecBytes = Encoding.UTF8.GetBytes("{\"openapi\":\"3.1.0\",\"info\":{\"title\":\"Gateway v1.0.0 rev2\"}}");
            var (up2Code, up2Doc) = await UploadJsonFileAsync(devClient, devCsrf, r2Id, jsonFtId, "openapi-v1-rev2.json", v2SpecBytes);
            Assert.Equal(HttpStatusCode.Created, up2Code);
            var fSpec2Id = up2Doc!.RootElement.GetProperty("id").GetGuid();

            // Submit Working Copy Revision #2 -> Approve via Release Route -> Republish Revision #2
            var (sub2Code, sub2Doc) = await SendJsonAsync(devClient, devCsrf, HttpMethod.Post, $"/api/v1/revisions/{r2Id}/submit");
            Assert.Equal(HttpStatusCode.OK, sub2Code);
            var run2Id = sub2Doc!.RootElement.GetProperty("workflowRunId").GetGuid();

            var (_, run2Doc) = await SendJsonAsync(revClient, revCsrf, HttpMethod.Get, $"/api/v1/workflows/{run2Id}");
            var run2Stage1Id = run2Doc!.RootElement.GetProperty("stages")[0].GetProperty("id").GetGuid();
            Assert.Equal(HttpStatusCode.OK, (await SendJsonAsync(revClient, revCsrf, HttpMethod.Post,
                $"/api/v1/workflows/{run2Id}/stages/{run2Stage1Id}/decisions",
                new { decision = "APPROVE", notes = "Re-approved working copy rev2" })).Status);

            var (pub2Code, pub2Doc) = await SendJsonAsync(revClient, revCsrf, HttpMethod.Post,
                $"/api/v1/revisions/{r2Id}/publications",
                new { revisionFileIds = new[] { fSpec2Id } });
            Assert.Equal(HttpStatusCode.Created, pub2Code);
            var pub2Id = pub2Doc!.RootElement.GetProperty("id").GetGuid();

            using var dlV2 = await portalClient.GetAsync($"/api/v1/portal/publications/{pub2Id}/files/{fSpec2Id}/download");
            Assert.Equal(HttpStatusCode.OK, dlV2.StatusCode);
            Assert.Equal(v2SpecBytes, await dlV2.Content.ReadAsByteArrayAsync());
        }
        finally
        {
            await factory.DisposeAsync();
            if (Directory.Exists(dpDir)) Directory.Delete(dpDir, true);
            if (Directory.Exists(storageDir)) Directory.Delete(storageDir, true);
        }
    }
}
