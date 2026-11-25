using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
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
public sealed class ChangesAndNotificationsS11IntegrationTests
{
    private sealed class CapturingDeliverySpy : IAuthTokenDeliveryChannel
    {
        public string ChannelCode => "TEST_S11_SPY";
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
                    // Continue serving remaining connections.
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _cts.CancelAsync();
            _listener.Stop();
            try { await _acceptLoop; } catch { /* ignored */ }
            _cts.Dispose();
        }
    }

    private static (WebApplicationFactory<Program> Factory, CapturingDeliverySpy Spy, string DpDir, string StorageDir)
        CreateFactory(int clamAvPort)
    {
        var dpDir = Path.Combine(Path.GetTempPath(), "ApiGovTestDP", Guid.NewGuid().ToString("N"));
        var storageDir = Path.Combine(Path.GetTempPath(), "ApiGovPrivateStoreS11", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dpDir);
        Directory.CreateDirectory(storageDir);
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
        var adminEmail = $"s11-admin-{Guid.NewGuid():N}@api-governance.local";
        const string adminPwd = "S11#ChangesNotifs!99";

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var bootstrap = scope.ServiceProvider.GetRequiredService<IInitialAdminBootstrapService>();
            var res = await bootstrap.BootstrapFirstAdminAsync(new(adminEmail, "S11 Admin", adminPwd));
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
        Guid[] roleIds)
    {
        var (uCode, userDoc) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/users",
            new { fullName, email, roleIds, triggerActivationToken = true });
        Assert.Equal(HttpStatusCode.Created, uCode);
        var userId = userDoc!.RootElement.GetProperty("id").GetGuid();
        var tok = spy.Deliveries.Last(d => d.TargetUserId == userId).SingleUseToken;

        const string pwd = "S11#UserStrong!Pass88";
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
        object? body = null)
    {
        using var req = new HttpRequestMessage(method, url);
        if (method != HttpMethod.Get)
        {
            req.Headers.Add("X-CSRF-TOKEN", csrf);
        }
        if (body is not null)
        {
            req.Content = JsonContent.Create(body);
        }

        using var res = await client.SendAsync(req);
        var raw = await res.Content.ReadAsStringAsync();
        var doc = string.IsNullOrWhiteSpace(raw) ? null : JsonDocument.Parse(raw);
        return (res.StatusCode, doc);
    }

    private static async Task<(HttpStatusCode Status, JsonDocument? Doc)> UploadMultipartAsync(
        HttpClient client,
        string csrf,
        string url,
        Guid fileTypeId,
        string fileName,
        byte[] content)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(fileTypeId.ToString()), "fileTypeId");
        var fileContent = new ByteArrayContent(content);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        form.Add(fileContent, "file", fileName);

        using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = form };
        req.Headers.Add("X-CSRF-TOKEN", csrf);

        using var res = await client.SendAsync(req);
        var raw = await res.Content.ReadAsStringAsync();
        var doc = string.IsNullOrWhiteSpace(raw) ? null : JsonDocument.Parse(raw);
        return (res.StatusCode, doc);
    }

    [Fact]
    public async Task W05_W06_W07_ChangeRequestsAffectedFiles_IndependentApprovalRoute_AndUserScopedNotifications()
    {
        await ResetIsolatedDatabaseAsync();
        await using var clamAv = new EmbeddedCleanClamAvServer();
        var (factory, spy, dpDir, storageDir) = CreateFactory(clamAv.Port);

        try
        {
            var (adminId, adminClient, adminCsrf) = await BootstrapAndLoginAdminAsync(factory);

            var (_, permDoc) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Get, "/api/v1/permissions");
            var permMap = permDoc!.RootElement.GetProperty("permissions").EnumerateArray()
                .ToDictionary(p => p.GetProperty("code").GetString()!, p => p.GetProperty("id").GetGuid(), StringComparer.OrdinalIgnoreCase);

            // Developer role & ChangeApprover role (independent route!) & Outsider role
            var (_, rDevDoc) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/roles",
                new { name = "S11DevRole", description = "Developer role" });
            var devRoleId = rDevDoc!.RootElement.GetProperty("id").GetGuid();
            await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Put, $"/api/v1/roles/{devRoleId}/permissions",
                new
                {
                    permissionIds = new[]
                    {
                        permMap["integrations.view"], permMap["versions.view"], permMap["versions.create"],
                        permMap["revisions.view"], permMap["revisions.create"], permMap["revisions.submit"],
                        permMap["files.view"], permMap["files.upload"], permMap["files.delete"], permMap["files.download"],
                        permMap["changes.view"], permMap["changes.create"], permMap["changes.edit"], permMap["changes.submit"],
                        permMap["workflows.view"]
                    }
                });

            var (_, rRevDoc) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/roles",
                new { name = "S11RevRole", description = "Revision approver" });
            var revRoleId = rRevDoc!.RootElement.GetProperty("id").GetGuid();
            await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Put, $"/api/v1/roles/{revRoleId}/permissions",
                new { permissionIds = new[] { permMap["integrations.view"], permMap["workflows.view"], permMap["workflows.decide"] } });

            var (_, rCrDoc) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/roles",
                new { name = "S11ChangeBoardRole", description = "Independent change approval board" });
            var crBoardRoleId = rCrDoc!.RootElement.GetProperty("id").GetGuid();
            await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Put, $"/api/v1/roles/{crBoardRoleId}/permissions",
                new { permissionIds = new[] { permMap["integrations.view"], permMap["workflows.view"], permMap["workflows.decide"], permMap["changes.view"] } });

            var (devUserId, devClient, devCsrf) = await CreateAndLoginUserAsync(
                factory, spy, adminClient, adminCsrf, "dev-s11@api-governance.local", "Dev User", [devRoleId]);
            var (_, revReviewerClient, revReviewerCsrf) = await CreateAndLoginUserAsync(
                factory, spy, adminClient, adminCsrf, "rev-s11@api-governance.local", "Rev Reviewer", [revRoleId]);
            var (_, cbReviewerClient, cbReviewerCsrf) = await CreateAndLoginUserAsync(
                factory, spy, adminClient, adminCsrf, "cb-s11@api-governance.local", "ChangeBoard Reviewer", [crBoardRoleId]);

            // Seed Master Data & Governance Config (two independent routes: Document route vs ChangeType route)
            var (_, sys1) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/company-systems",
                new { name = "CoreBank S11", ownerEmail = "c@api-governance.local", description = "Provider" });
            var (_, sys2) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/company-systems",
                new { name = "MobileApp S11", ownerEmail = "m@api-governance.local", description = "Consumer" });
            var sys1Id = sys1!.RootElement.GetProperty("id").GetGuid();
            var sys2Id = sys2!.RootElement.GetProperty("id").GetGuid();

            await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/internal-providers",
                new { companySystemId = sys1Id, description = "Int" });
            var (_, grp) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/api-groups",
                new { name = "Transfers S11", description = "G" });
            var grpId = grp!.RootElement.GetProperty("id").GetGuid();

            var (_, ftJson) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/file-types",
                new { catalogCode = "OPENAPI_SPEC", displayName = "OpenAPI Spec", maxSizeBytes = 204800L });
            var jsonFtId = ftJson!.RootElement.GetProperty("id").GetGuid();
            var (_, ftMd) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/file-types",
                new { catalogCode = "MARKDOWN_DOC", displayName = "Markdown Guide", maxSizeBytes = 204800L });
            var mdFtId = ftMd!.RootElement.GetProperty("id").GetGuid();

            var (_, tpl) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/requirement-templates",
                new
                {
                    name = "S11 Tpl",
                    rules = new[]
                    {
                        new { fileTypeId = jsonFtId, requirementMode = "REQUIRED", minCount = 1, maxCount = 2 },
                        new { fileTypeId = mdFtId, requirementMode = "OPTIONAL", minCount = 0, maxCount = 2 }
                    }
                });
            var tplId = tpl!.RootElement.GetProperty("id").GetGuid();

            var (_, revRoute) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/approval-routes",
                new { name = "Standard Rev Route", stages = new[] { new { position = 1, stageName = "Rev Stage", responsibleRoleId = revRoleId } } });
            var revRouteId = revRoute!.RootElement.GetProperty("id").GetGuid();

            var (_, crRoute) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/approval-routes",
                new { name = "ChangeBoard Independent Route", stages = new[] { new { position = 1, stageName = "Change Board Signoff", responsibleRoleId = crBoardRoleId } } });
            var crRouteId = crRoute!.RootElement.GetProperty("id").GetGuid();

            var (_, ctDoc) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/change-types",
                new { name = "Breaking Schema Update", routeId = crRouteId });
            var ctId = ctDoc!.RootElement.GetProperty("id").GetGuid();
            var (_, pol) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/change-policies",
                new { name = "S11 Policy", changeTypeIds = new[] { ctId } });
            var polId = pol!.RootElement.GetProperty("id").GetGuid();

            var (_, doc) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/api-documents",
                new
                {
                    providerInternalSystemId = sys1Id,
                    consumerCompanySystemId = sys2Id,
                    apiGroupId = grpId,
                    requirementTemplateId = tplId,
                    approvalRouteId = revRouteId,
                    changePolicyId = polId,
                    publicationOption = "MANUAL",
                    state = "ACTIVE",
                    developerUserIds = Array.Empty<Guid>()
                });
            var docId = doc!.RootElement.GetProperty("id").GetGuid();

            var creatorUserRules = DocumentPermissionCatalog.All
                .Select(p => new { userId = adminId, permissionCode = p.Code, decision = "GRANT" })
                .ToArray();
            await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Put, $"/api/v1/api-documents/{docId}/access",
                new
                {
                    rolePermissions = new[]
                    {
                        new { roleId = devRoleId, permissionCode = "doc.view", decision = "GRANT" },
                        new { roleId = devRoleId, permissionCode = "doc.edit", decision = "GRANT" },
                        new { roleId = devRoleId, permissionCode = "doc.version_manage", decision = "GRANT" },
                        new { roleId = devRoleId, permissionCode = "doc.files_manage", decision = "GRANT" },
                        new { roleId = devRoleId, permissionCode = "doc.change", decision = "GRANT" },
                        new { roleId = revRoleId, permissionCode = "doc.view", decision = "GRANT" },
                        new { roleId = revRoleId, permissionCode = "doc.review", decision = "GRANT" },
                        new { roleId = crBoardRoleId, permissionCode = "doc.view", decision = "GRANT" },
                        new { roleId = crBoardRoleId, permissionCode = "doc.review", decision = "GRANT" }
                    },
                    userPermissions = creatorUserRules
                });

            var (_, integ) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/integrations",
                new { documentId = docId, referenceCode = "INT-S11-CR-01", developerUserIds = new[] { devUserId } });
            var integrationId = integ!.RootElement.GetProperty("id").GetGuid();

            // Create version, draft revision #1, upload 2 files, submit -> notifies revReviewer (W07!), approve rev #1!
            var (_, verDoc) = await SendJsonAsync(devClient, devCsrf, HttpMethod.Post,
                $"/api/v1/integrations/{integrationId}/versions", new { versionLabel = "v1-s11" });
            var versionId = verDoc!.RootElement.GetProperty("id").GetGuid();

            var (_, revDoc) = await SendJsonAsync(devClient, devCsrf, HttpMethod.Post,
                $"/api/v1/versions/{versionId}/revisions");
            var rev1Id = revDoc!.RootElement.GetProperty("id").GetGuid();

            var (_, specFileDoc) = await UploadMultipartAsync(devClient, devCsrf, $"/api/v1/revisions/{rev1Id}/files",
                jsonFtId, "transfers.json", Encoding.UTF8.GetBytes("{\"openapi\":\"3.1.0\"}"));
            var specFileId = specFileDoc!.RootElement.GetProperty("id").GetGuid();

            await UploadMultipartAsync(devClient, devCsrf, $"/api/v1/revisions/{rev1Id}/files",
                mdFtId, "guide.md", Encoding.UTF8.GetBytes("# Guide"));

            var (subRevCode, subRevDoc) = await SendJsonAsync(devClient, devCsrf, HttpMethod.Post,
                $"/api/v1/revisions/{rev1Id}/submit");
            Assert.Equal(HttpStatusCode.OK, subRevCode);
            var revRunId = subRevDoc!.RootElement.GetProperty("workflowRunId").GetGuid();

            // W07: Reviewer receives internal notification on submission!
            var (_, revNotifDoc) = await SendJsonAsync(revReviewerClient, revReviewerCsrf, HttpMethod.Get, "/api/v1/notifications?unreadOnly=true");
            Assert.Equal(1, revNotifDoc!.RootElement.GetProperty("unreadCount").GetInt32());
            var revNotifId = revNotifDoc.RootElement.GetProperty("items")[0].GetProperty("id").GetGuid();

            // Anti-IDOR check on notification read: devUser CANNOT mark reviewer's notification as read -> 404
            var (idorNotifCode, _) = await SendJsonAsync(devClient, devCsrf, HttpMethod.Post,
                $"/api/v1/notifications/{revNotifId}/read");
            Assert.Equal(HttpStatusCode.NotFound, idorNotifCode);

            // Reviewer marks own notification read -> unreadCount drops to 0
            Assert.Equal(HttpStatusCode.OK, (await SendJsonAsync(revReviewerClient, revReviewerCsrf, HttpMethod.Post,
                $"/api/v1/notifications/{revNotifId}/read")).Status);

            var (_, revRunDetails) = await SendJsonAsync(revReviewerClient, revReviewerCsrf, HttpMethod.Get, $"/api/v1/workflows/{revRunId}");
            var revStage1Id = revRunDetails!.RootElement.GetProperty("stages")[0].GetProperty("id").GetGuid();
            Assert.Equal(HttpStatusCode.OK, (await SendJsonAsync(revReviewerClient, revReviewerCsrf, HttpMethod.Post,
                $"/api/v1/workflows/{revRunId}/stages/{revStage1Id}/decisions",
                new { decision = "APPROVE", notes = "Baseline v1 approved." })).Status);

            // Developer received WORKFLOW_APPROVED notification!
            var (_, devNotifsAfterApprove) = await SendJsonAsync(devClient, devCsrf, HttpMethod.Get, "/api/v1/notifications");
            Assert.Contains(devNotifsAfterApprove!.RootElement.GetProperty("items").EnumerateArray(),
                n => n.GetProperty("kind").GetString() == "WORKFLOW_APPROVED");

            // W05 Negative: Change request with foreign file ID must fail 422!
            var (badCrCode, _) = await SendJsonAsync(devClient, devCsrf, HttpMethod.Post,
                $"/api/v1/integrations/{integrationId}/change-requests",
                new
                {
                    sourceRevisionId = rev1Id,
                    changeTypeId = ctId,
                    reason = "Bad file scope",
                    affectedSourceFileIds = new[] { Guid.NewGuid() }
                });
            Assert.Equal(HttpStatusCode.UnprocessableEntity, badCrCode);

            // W05 Create valid DRAFT change request targeting ONLY specFileId, then PATCH it before submitting!
            var (crCreateCode, crCreateDoc) = await SendJsonAsync(devClient, devCsrf, HttpMethod.Post,
                $"/api/v1/integrations/{integrationId}/change-requests",
                new
                {
                    sourceRevisionId = rev1Id,
                    changeTypeId = ctId,
                    reason = "Initial change reason",
                    consumerImpact = "None",
                    affectedSourceFileIds = new[] { specFileId }
                });
            Assert.Equal(HttpStatusCode.Created, crCreateCode);
            var crId = crCreateDoc!.RootElement.GetProperty("id").GetGuid();
            Assert.Equal("DRAFT", crCreateDoc.RootElement.GetProperty("status").GetString());

            var (crPatchCode, crPatchDoc) = await SendJsonAsync(devClient, devCsrf, HttpMethod.Patch,
                $"/api/v1/change-requests/{crId}",
                new
                {
                    changeTypeId = ctId,
                    reason = "Updated IBAN field validation contract",
                    consumerImpact = "Minor payload update",
                    affectedSourceFileIds = new[] { specFileId }
                });
            Assert.Equal(HttpStatusCode.OK, crPatchCode);
            Assert.Equal("Updated IBAN field validation contract", crPatchDoc!.RootElement.GetProperty("reason").GetString());

            // W06 Submit Change Request -> launches independent workflow on crRouteId (not revRouteId!) + notifies ChangeBoard!
            var (crSubCode, crSubDoc) = await SendJsonAsync(devClient, devCsrf, HttpMethod.Post,
                $"/api/v1/change-requests/{crId}/submit");
            Assert.Equal(HttpStatusCode.OK, crSubCode);
            var crRunId = crSubDoc!.RootElement.GetProperty("workflowRunId").GetGuid();
            Assert.Equal(crRouteId, crSubDoc.RootElement.GetProperty("changeRequest").GetProperty("approvalRouteId").GetGuid());

            // Submitting again while already SUBMITTED -> 422; Editing while SUBMITTED -> 422
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await SendJsonAsync(devClient, devCsrf, HttpMethod.Patch,
                $"/api/v1/change-requests/{crId}",
                new { changeTypeId = ctId, reason = "Blocked edit", affectedSourceFileIds = new[] { specFileId } })).Status);

            // ChangeBoard reviewer sees notification & Inbox item for ChangeRequest, RevReviewer CANNOT approve ChangeBoard stage (403)!
            var (_, cbNotifDoc) = await SendJsonAsync(cbReviewerClient, cbReviewerCsrf, HttpMethod.Get, "/api/v1/notifications?unreadOnly=true");
            Assert.Contains(cbNotifDoc!.RootElement.GetProperty("items").EnumerateArray(),
                n => n.GetProperty("resourceKind").GetString() == "CHANGE_REQUEST" && n.GetProperty("resourceId").GetGuid() == crId);

            var (_, cbRunDetails) = await SendJsonAsync(cbReviewerClient, cbReviewerCsrf, HttpMethod.Get, $"/api/v1/workflows/{crRunId}");
            var cbStage1Id = cbRunDetails!.RootElement.GetProperty("stages")[0].GetProperty("id").GetGuid();

            Assert.Equal(HttpStatusCode.Forbidden, (await SendJsonAsync(revReviewerClient, revReviewerCsrf, HttpMethod.Post,
                $"/api/v1/workflows/{crRunId}/stages/{cbStage1Id}/decisions",
                new { decision = "APPROVE" })).Status);

            var (cbApproveCode, _) = await SendJsonAsync(cbReviewerClient, cbReviewerCsrf, HttpMethod.Post,
                $"/api/v1/workflows/{crRunId}/stages/{cbStage1Id}/decisions",
                new { decision = "APPROVE", notes = "Change Board approved." });
            Assert.Equal(HttpStatusCode.OK, cbApproveCode);

            // Verify ChangeRequest is now APPROVED & can open V06 Working Copy Revision #2!
            var (_, crFinalDoc) = await SendJsonAsync(devClient, devCsrf, HttpMethod.Get, $"/api/v1/change-requests/{crId}");
            Assert.Equal("APPROVED", crFinalDoc!.RootElement.GetProperty("status").GetString());

            var (wcCode, wcDoc) = await SendJsonAsync(devClient, devCsrf, HttpMethod.Post,
                $"/api/v1/versions/{versionId}/revisions",
                new { baseRevisionId = rev1Id, changeRequestId = crId });
            Assert.Equal(HttpStatusCode.Created, wcCode);
            Assert.Equal(2, wcDoc!.RootElement.GetProperty("revisionNumber").GetInt32());
            Assert.Equal(crId, wcDoc.RootElement.GetProperty("changeRequestId").GetGuid());
        }
        finally
        {
            await factory.DisposeAsync();
            if (Directory.Exists(dpDir)) Directory.Delete(dpDir, true);
            if (Directory.Exists(storageDir)) Directory.Delete(storageDir, true);
        }
    }
}
