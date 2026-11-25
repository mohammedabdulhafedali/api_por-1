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

[Collection("IsolatedTestDatabase")]
public sealed class PublishingPortalS12IntegrationTests
{
    private sealed class CapturingDeliverySpy : IAuthTokenDeliveryChannel
    {
        public string ChannelCode => "TEST_S12_SPY";
        public List<AuthTokenDeliveryContext> Deliveries { get; } = [];

        public Task DeliverPasswordResetTokenAsync(
            AuthTokenDeliveryContext context,
            CancellationToken cancellationToken = default)
        {
            Deliveries.Add(context);
            return Task.CompletedTask;
        }
    }

    private sealed class EmbeddedCleanClamAvServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _acceptLoop;

        public int Port { get; }

        public EmbeddedCleanClamAvServer()
        {
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _acceptLoop = Task.Run(AcceptLoopAsync);
        }

        private async Task AcceptLoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    using var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                    await using var stream = client.GetStream();
                    var cmdBuf = new byte[10];
                    var cmdRead = 0;
                    while (cmdRead < cmdBuf.Length)
                    {
                        var n = await stream.ReadAsync(cmdBuf.AsMemory(cmdRead), _cts.Token);
                        if (n == 0) return;
                        cmdRead += n;
                    }
                    var lenBuf = new byte[4];
                    while (true)
                    {
                        var off = 0;
                        while (off < 4)
                        {
                            var r = await stream.ReadAsync(lenBuf.AsMemory(off, 4 - off), _cts.Token);
                            if (r == 0) return;
                            off += r;
                        }
                        var chunkLen = BinaryPrimitives.ReadUInt32BigEndian(lenBuf);
                        if (chunkLen == 0) break;
                        var chunk = new byte[chunkLen];
                        var cOff = 0;
                        while (cOff < chunk.Length)
                        {
                            var r = await stream.ReadAsync(chunk.AsMemory(cOff, chunk.Length - cOff), _cts.Token);
                            if (r == 0) return;
                            cOff += r;
                        }
                    }
                    await stream.WriteAsync(Encoding.UTF8.GetBytes("stream: OK\0"), _cts.Token);
                    await stream.FlushAsync(_cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    // Ignore transient close
                }
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
        var dpDir = Path.Combine(Path.GetTempPath(), $"apigov-dp-s12-{Guid.NewGuid():N}");
        var storageDir = Path.Combine(Path.GetTempPath(), $"apigov-store-s12-{Guid.NewGuid():N}");
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
        var adminEmail = $"s12-admin-{Guid.NewGuid():N}@api-governance.local";
        const string adminPwd = "S12#PublishPortal!99";

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var bootstrap = scope.ServiceProvider.GetRequiredService<IInitialAdminBootstrapService>();
            var res = await bootstrap.BootstrapFirstAdminAsync(new(adminEmail, "S12 Admin", adminPwd));
            Assert.True(res.Created);
        }

        var client = CreateCookieClient(factory);
        var csrf = await FetchCsrfAsync(client);

        using var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new { email = adminEmail, password = adminPwd })
        };
        loginReq.Headers.Add("X-CSRF-TOKEN", csrf);
        using var loginRes = await client.SendAsync(loginReq);
        Assert.Equal(HttpStatusCode.OK, loginRes.StatusCode);

        var csrfAfter = await FetchCsrfAsync(client);
        using var meRes = await client.GetAsync("/api/v1/me");
        meRes.EnsureSuccessStatusCode();
        using var meDoc = JsonDocument.Parse(await meRes.Content.ReadAsStringAsync());
        var adminId = meDoc.RootElement.GetProperty("id").GetGuid();

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

        const string pwd = "S12#PortalUser!Strong88";
        var client = CreateCookieClient(factory);
        var csrf = await FetchCsrfAsync(client);
        var (resetCode, _) = await SendJsonAsync(client, csrf, HttpMethod.Post, "/api/v1/auth/password/reset/complete",
            new { email, token = tok, newPassword = pwd });
        Assert.Equal(HttpStatusCode.NoContent, resetCode);

        var (loginCode, _) = await SendJsonAsync(client, csrf, HttpMethod.Post, "/api/v1/auth/login",
            new { email, password = pwd });
        Assert.Equal(HttpStatusCode.OK, loginCode);

        var csrfAfter = await FetchCsrfAsync(client);
        return (userId, client, csrfAfter);
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
    public async Task Publishing_Withdrawal_ActiveConsumerVersion_And_ScopedPortal_Work_EndToEnd_With_Idor_Guards()
    {
        await ResetIsolatedDatabaseAsync();
        await using var clamAv = new EmbeddedCleanClamAvServer();
        var (factory, spy, dpDir, storageDir) = CreateFactory(clamAv.Port);

        try
        {
            var (adminId, adminClient, adminCsrf) = await BootstrapAndLoginAdminAsync(factory);

            // 1. Seed Master Data: Internal Provider, 2 Consumer Entities (A & B), API Group, .json FileType, Template, Route, Change Policy
            var (_, sysDoc) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/company-systems",
                new { name = "Core Banking Hub", ownerEmail = "core@api-governance.local", description = "Provider" });
            var sysId = sysDoc!.RootElement.GetProperty("id").GetGuid();

            Assert.Equal(HttpStatusCode.Created, (await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/internal-providers",
                new { companySystemId = sysId, description = "Internal Provider" })).Status);

            var (_, catDoc) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/consumer-categories",
                new { name = "Partner Banks", description = "Banks" });
            var catId = catDoc!.RootElement.GetProperty("id").GetGuid();

            var (_, consADoc) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/consumers",
                new { categoryId = catId, name = "Alpha Consumer Bank" });
            var consumerAId = consADoc!.RootElement.GetProperty("id").GetGuid();

            var (_, consBDoc) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/consumers",
                new { categoryId = catId, name = "Beta Consumer FinTech" });
            var consumerBId = consBDoc!.RootElement.GetProperty("id").GetGuid();

            var (_, grpDoc) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/api-groups",
                new { name = "Settlement APIs", description = "Settlement" });
            var groupId = grpDoc!.RootElement.GetProperty("id").GetGuid();

            var (_, ftDoc) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/file-types",
                new
                {
                    catalogCode = "OPENAPI_SPEC",
                    displayName = "OpenAPI JSON Spec",
                    maxSizeBytes = 204800L
                });
            var jsonFileTypeId = ftDoc!.RootElement.GetProperty("id").GetGuid();

            var (_, tplDoc) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/requirement-templates",
                new
                {
                    name = "S12 Standard Template",
                    rules = new[] { new { fileTypeId = jsonFileTypeId, requirementMode = "REQUIRED", minCount = 1, maxCount = 5 } }
                });
            var templateId = tplDoc!.RootElement.GetProperty("id").GetGuid();

            // Reviewer role + Portal consumer role (zero admin perms!)
            var (_, permRes) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Get, "/api/v1/permissions");
            var permIds = permRes!.RootElement.GetProperty("permissions").EnumerateArray()
                .ToDictionary(e => e.GetProperty("code").GetString()!, e => e.GetProperty("id").GetGuid(), StringComparer.OrdinalIgnoreCase);

            var (_, reviewerRoleDoc) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/roles",
                new { name = "S12 Reviewer Publisher", description = "Reviewer and publisher" });
            var reviewerRoleId = reviewerRoleDoc!.RootElement.GetProperty("id").GetGuid();
            Assert.Equal(HttpStatusCode.OK, (await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Put,
                $"/api/v1/roles/{reviewerRoleId}/permissions",
                new
                {
                    permissionIds = new[]
                    {
                        permIds["integrations.view"],
                        permIds["versions.view"],
                        permIds["files.view"],
                        permIds["workflows.view"],
                        permIds["workflows.decide"],
                        permIds["publications.view"],
                        permIds["publications.create"],
                        permIds["publications.withdraw"],
                        permIds["versions.usage.view"],
                        permIds["versions.usage.manage"]
                    }
                })).Status);

            var (_, portalRoleDoc) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/roles",
                new { name = "External Consumer Portal Role", description = "Portal user" });
            var portalRoleId = portalRoleDoc!.RootElement.GetProperty("id").GetGuid();

            var (_, routeDoc) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/approval-routes",
                new
                {
                    name = "S12 Approval Route",
                    stages = new[] { new { position = 1, stageName = "Publisher Signoff", responsibleRoleId = reviewerRoleId } }
                });
            var routeId = routeDoc!.RootElement.GetProperty("id").GetGuid();

            var (_, ctDoc) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/change-types",
                new { name = "Minor Change", routeId });
            var ctId = ctDoc!.RootElement.GetProperty("id").GetGuid();

            var (_, polDoc) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/change-policies",
                new { name = "S12 Policy", changeTypeIds = new[] { ctId } });
            var policyId = polDoc!.RootElement.GetProperty("id").GetGuid();

            // Create Users: Internal Reviewer/Publisher + Portal Consumer A + Portal Consumer B
            var (pubUserId, pubClient, pubCsrf) = await CreateAndLoginUserAsync(
                factory, spy, adminClient, adminCsrf, "publisher.s12@api-governance.local", "Publisher Staff", [reviewerRoleId], null);
            var (_, portalAClient, portalACsrf) = await CreateAndLoginUserAsync(
                factory, spy, adminClient, adminCsrf, "portal.alpha@alpha-bank.example", "Alpha Bank Portal User", [portalRoleId], consumerAId);
            var (_, portalBClient, portalBCsrf) = await CreateAndLoginUserAsync(
                factory, spy, adminClient, adminCsrf, "portal.beta@beta-fintech.example", "Beta FinTech Portal User", [portalRoleId], consumerBId);

            // Create Documents + Integration for Consumer A
            var (_, docADoc) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/api-documents",
                new
                {
                    providerInternalSystemId = sysId,
                    consumerEntityId = consumerAId,
                    apiGroupId = groupId,
                    requirementTemplateId = templateId,
                    approvalRouteId = routeId,
                    changePolicyId = policyId,
                    publicationOption = "MANUAL",
                    state = "ACTIVE",
                    developerUserIds = Array.Empty<Guid>()
                });
            var docAId = docADoc!.RootElement.GetProperty("id").GetGuid();

            // Grant docA permissions to Publisher user via role ACL while keeping creator grants
            var creatorUserRules = DocumentPermissionCatalog.All
                .Select(p => new { userId = adminId, permissionCode = p.Code, decision = "GRANT" })
                .ToArray();
            Assert.Equal(HttpStatusCode.OK, (await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Put,
                $"/api/v1/api-documents/{docAId}/access",
                new
                {
                    rolePermissions = new[]
                    {
                        new { roleId = reviewerRoleId, permissionCode = "doc.view", decision = "GRANT" },
                        new { roleId = reviewerRoleId, permissionCode = "doc.review", decision = "GRANT" },
                        new { roleId = reviewerRoleId, permissionCode = "doc.publish", decision = "GRANT" }
                    },
                    userPermissions = creatorUserRules
                })).Status);

            var (_, intADoc) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/integrations",
                new { documentId = docAId, referenceCode = "INT-ALPHA-S12", developerUserIds = new[] { adminId, pubUserId } });
            var intAId = intADoc!.RootElement.GetProperty("id").GetGuid();

            // Create two versions (v1.0 and v2.0) on Integration A
            var (v1Code, v1Doc) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, $"/api/v1/integrations/{intAId}/versions",
                new { versionLabel = "v1.0" });
            Assert.Equal(HttpStatusCode.Created, v1Code);
            var v1Id = v1Doc!.RootElement.GetProperty("id").GetGuid();

            var (v2Code, v2Doc) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, $"/api/v1/integrations/{intAId}/versions",
                new { versionLabel = "v2.0" });
            Assert.Equal(HttpStatusCode.Created, v2Code);
            var v2Id = v2Doc!.RootElement.GetProperty("id").GetGuid();

            // Revision #1 on v1.0 with TWO files: spec-selected.json and internal-notes.json
            var (_, rev1Doc) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, $"/api/v1/versions/{v1Id}/revisions");
            var rev1Id = rev1Doc!.RootElement.GetProperty("id").GetGuid();

            var selectedBytes = Encoding.UTF8.GetBytes("{\"openapi\":\"3.1.0\",\"info\":{\"title\":\"Alpha v1\"}}");
            var selectedSha = Convert.ToHexStringLower(SHA256.HashData(selectedBytes));
            var unpubBytes = Encoding.UTF8.GetBytes("{\"internalNote\":\"do not expose to external consumers\"}");

            var (up1Code, up1Doc) = await UploadJsonFileAsync(adminClient, adminCsrf, rev1Id, jsonFileTypeId, "spec-selected.json", selectedBytes);
            Assert.Equal(HttpStatusCode.Created, up1Code);
            var selectedFileId = up1Doc!.RootElement.GetProperty("id").GetGuid();

            var (up2Code, up2Doc) = await UploadJsonFileAsync(adminClient, adminCsrf, rev1Id, jsonFileTypeId, "internal-notes.json", unpubBytes);
            Assert.Equal(HttpStatusCode.Created, up2Code);
            var unpublishedFileId = up2Doc!.RootElement.GetProperty("id").GetGuid();

            // Negative P01: DRAFT revision cannot be published (422)
            var (draftPubCode, _) = await SendJsonAsync(pubClient, pubCsrf, HttpMethod.Post,
                $"/api/v1/revisions/{rev1Id}/publications",
                new { revisionFileIds = new[] { selectedFileId } });
            Assert.Equal(HttpStatusCode.UnprocessableEntity, draftPubCode);

            // Negative P03: Unpublished version cannot be set as active consumer version (422)
            var (unpubUsageCode, _) = await SendJsonAsync(pubClient, pubCsrf, HttpMethod.Put,
                $"/api/v1/integrations/{intAId}/consumer-version",
                new { apiVersionId = v1Id });
            Assert.Equal(HttpStatusCode.UnprocessableEntity, unpubUsageCode);

            // Submit & Approve rev1Id
            var (subCode, subDoc) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, $"/api/v1/revisions/{rev1Id}/submit");
            Assert.Equal(HttpStatusCode.OK, subCode);
            var run1Id = subDoc!.RootElement.GetProperty("workflowRunId").GetGuid();
            var (_, run1Doc) = await SendJsonAsync(pubClient, pubCsrf, HttpMethod.Get, $"/api/v1/workflows/{run1Id}");
            var run1Stage1Id = run1Doc!.RootElement.GetProperty("stages")[0].GetProperty("id").GetGuid();
            Assert.Equal(HttpStatusCode.OK, (await SendJsonAsync(pubClient, pubCsrf, HttpMethod.Post,
                $"/api/v1/workflows/{run1Id}/stages/{run1Stage1Id}/decisions",
                new { decision = "APPROVE", notes = "Approved v1 package." })).Status);

            // P01: Selective Publish ONLY selectedFileId (excluding internal-notes.json)
            var (pub1Code, pub1Doc) = await SendJsonAsync(pubClient, pubCsrf, HttpMethod.Post,
                $"/api/v1/revisions/{rev1Id}/publications",
                new { revisionFileIds = new[] { selectedFileId } });
            Assert.Equal(HttpStatusCode.Created, pub1Code);
            var pub1Id = pub1Doc!.RootElement.GetProperty("id").GetGuid();
            Assert.Equal("PUBLISHED", pub1Doc.RootElement.GetProperty("state").GetString());
            Assert.Single(pub1Doc.RootElement.GetProperty("files").EnumerateArray());
            Assert.Equal(selectedFileId, pub1Doc.RootElement.GetProperty("files")[0].GetProperty("revisionFileId").GetGuid());

            // Revision #1 on v2.0 -> upload, submit, approve, publish
            var (_, rev2Doc) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, $"/api/v1/versions/{v2Id}/revisions");
            var rev2Id = rev2Doc!.RootElement.GetProperty("id").GetGuid();
            var v2Bytes = Encoding.UTF8.GetBytes("{\"openapi\":\"3.1.0\",\"info\":{\"title\":\"Alpha v2\"}}");
            var (_, upV2Doc) = await UploadJsonFileAsync(adminClient, adminCsrf, rev2Id, jsonFileTypeId, "alpha-v2.json", v2Bytes);
            var v2FileId = upV2Doc!.RootElement.GetProperty("id").GetGuid();
            var (_, subV2Doc) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, $"/api/v1/revisions/{rev2Id}/submit");
            var run2Id = subV2Doc!.RootElement.GetProperty("workflowRunId").GetGuid();
            var (_, run2Doc) = await SendJsonAsync(pubClient, pubCsrf, HttpMethod.Get, $"/api/v1/workflows/{run2Id}");
            var run2Stage1Id = run2Doc!.RootElement.GetProperty("stages")[0].GetProperty("id").GetGuid();
            Assert.Equal(HttpStatusCode.OK, (await SendJsonAsync(pubClient, pubCsrf, HttpMethod.Post,
                $"/api/v1/workflows/{run2Id}/stages/{run2Stage1Id}/decisions",
                new { decision = "APPROVE", notes = "Approved v2 package." })).Status);
            var (pub2Code, _) = await SendJsonAsync(pubClient, pubCsrf, HttpMethod.Post,
                $"/api/v1/revisions/{rev2Id}/publications",
                new { revisionFileIds = new[] { v2FileId } });
            Assert.Equal(HttpStatusCode.Created, pub2Code);

            // P03: Set Consumer Active Version v1.0, then switch to v2.0 -> SINGLE active row valid_to IS NULL!
            Assert.Equal(HttpStatusCode.OK, (await SendJsonAsync(pubClient, pubCsrf, HttpMethod.Put,
                $"/api/v1/integrations/{intAId}/consumer-version",
                new { apiVersionId = v1Id })).Status);

            var (switchCode, usageDoc) = await SendJsonAsync(pubClient, pubCsrf, HttpMethod.Put,
                $"/api/v1/integrations/{intAId}/consumer-version",
                new { apiVersionId = v2Id });
            Assert.Equal(HttpStatusCode.OK, switchCode);
            Assert.Equal(v2Id, usageDoc!.RootElement.GetProperty("activeUsage").GetProperty("apiVersionId").GetGuid());
            var historyArr = usageDoc.RootElement.GetProperty("history").EnumerateArray().ToList();
            Assert.Equal(2, historyArr.Count);
            Assert.Single(historyArr, h => h.GetProperty("validTo").ValueKind == JsonValueKind.Null);

            // P04 Portal Scoping: Staff user without ConsumerEntityId is rejected (403)
            Assert.Equal(HttpStatusCode.Forbidden, (await SendJsonAsync(pubClient, pubCsrf, HttpMethod.Get, "/api/v1/portal/integrations")).Status);

            // P04 Consumer B sees ZERO integrations of Consumer A
            var (bListCode, bListDoc) = await SendJsonAsync(portalBClient, portalBCsrf, HttpMethod.Get, "/api/v1/portal/integrations");
            Assert.Equal(HttpStatusCode.OK, bListCode);
            Assert.Empty(bListDoc!.RootElement.GetProperty("integrations").EnumerateArray());

            // P04 Cross-consumer IDOR on Consumer A publication blocked (403)
            Assert.Equal(HttpStatusCode.Forbidden, (await SendJsonAsync(portalBClient, portalBCsrf, HttpMethod.Get, $"/api/v1/portal/publications/{pub1Id}/files")).Status);
            Assert.Equal(HttpStatusCode.Forbidden, (await SendJsonAsync(portalBClient, portalBCsrf, HttpMethod.Get, $"/api/v1/portal/publications/{pub1Id}/files/{selectedFileId}/download")).Status);

            // P04 Consumer A lists integration, sees ONLY selectedFileId (not internal-notes.json), and downloads verified file
            var (aListCode, aListDoc) = await SendJsonAsync(portalAClient, portalACsrf, HttpMethod.Get, "/api/v1/portal/integrations");
            Assert.Equal(HttpStatusCode.OK, aListCode);
            Assert.Single(aListDoc!.RootElement.GetProperty("integrations").EnumerateArray());
            Assert.Equal("v2.0", aListDoc.RootElement.GetProperty("integrations")[0].GetProperty("activeConsumerVersionLabel").GetString());

            var (aFilesCode, aFilesDoc) = await SendJsonAsync(portalAClient, portalACsrf, HttpMethod.Get, $"/api/v1/portal/publications/{pub1Id}/files");
            Assert.Equal(HttpStatusCode.OK, aFilesCode);
            var aFiles = aFilesDoc!.RootElement.GetProperty("files").EnumerateArray().ToList();
            Assert.Single(aFiles);
            Assert.Equal(selectedFileId, aFiles[0].GetProperty("revisionFileId").GetGuid());

            // Attempting to download unpublished file inside revision -> 404 NOT_IN_PUBLICATION
            Assert.Equal(HttpStatusCode.NotFound, (await SendJsonAsync(portalAClient, portalACsrf, HttpMethod.Get, $"/api/v1/portal/publications/{pub1Id}/files/{unpublishedFileId}/download")).Status);

            // Valid Portal Download checks security headers & SHA-256
            using var dlRes = await portalAClient.GetAsync($"/api/v1/portal/publications/{pub1Id}/files/{selectedFileId}/download");
            Assert.Equal(HttpStatusCode.OK, dlRes.StatusCode);
            Assert.Equal("nosniff", dlRes.Headers.GetValues("X-Content-Type-Options").First());
            Assert.Equal(selectedSha, dlRes.Headers.GetValues("X-File-Sha256").First());
            var downloadedBytes = await dlRes.Content.ReadAsByteArrayAsync();
            Assert.Equal(selectedBytes, downloadedBytes);

            // P02 Withdraw pub1Id -> record stays WITHDRAWN, portal access blocked (403)!
            var (wdCode, wdDoc) = await SendJsonAsync(pubClient, pubCsrf, HttpMethod.Post, $"/api/v1/publications/{pub1Id}/withdraw");
            Assert.Equal(HttpStatusCode.OK, wdCode);
            Assert.Equal("WITHDRAWN", wdDoc!.RootElement.GetProperty("state").GetString());
            Assert.NotEqual(JsonValueKind.Null, wdDoc.RootElement.GetProperty("withdrawnAt").ValueKind);

            Assert.Equal(HttpStatusCode.Forbidden, (await SendJsonAsync(portalAClient, portalACsrf, HttpMethod.Get, $"/api/v1/portal/publications/{pub1Id}/files")).Status);
            Assert.Equal(HttpStatusCode.Forbidden, (await SendJsonAsync(portalAClient, portalACsrf, HttpMethod.Get, $"/api/v1/portal/publications/{pub1Id}/files/{selectedFileId}/download")).Status);
        }
        finally
        {
            await factory.DisposeAsync();
            if (Directory.Exists(dpDir)) Directory.Delete(dpDir, true);
            if (Directory.Exists(storageDir)) Directory.Delete(storageDir, true);
        }
    }
}
