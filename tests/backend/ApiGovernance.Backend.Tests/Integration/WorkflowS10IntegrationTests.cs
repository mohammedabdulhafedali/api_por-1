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
public sealed class WorkflowS10IntegrationTests
{
    private sealed class CapturingDeliverySpy : IAuthTokenDeliveryChannel
    {
        public string ChannelCode => "TEST_S10_SPY";
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
        var storageDir = Path.Combine(Path.GetTempPath(), "ApiGovPrivateStoreS10", Guid.NewGuid().ToString("N"));
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
        var adminEmail = $"s10-admin-{Guid.NewGuid():N}@api-governance.local";
        const string adminPwd = "S10#WorkflowEngine!99";

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var bootstrap = scope.ServiceProvider.GetRequiredService<IInitialAdminBootstrapService>();
            var res = await bootstrap.BootstrapFirstAdminAsync(new(adminEmail, "S10 Admin", adminPwd));
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
            new
            {
                fullName,
                email,
                roleIds,
                triggerActivationToken = true
            });
        Assert.Equal(HttpStatusCode.Created, uCode);
        var userId = userDoc!.RootElement.GetProperty("id").GetGuid();
        var tok = spy.Deliveries.Last(d => d.TargetUserId == userId).SingleUseToken;

        const string pwd = "S10#Reviewer!Pass88";
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

    private sealed record WorkflowScenarioSeed(
        Guid DocumentId,
        Guid IntegrationId,
        Guid VersionId,
        Guid RouteId,
        Guid RoleAId,
        Guid RoleBId,
        Guid BaseViewerRoleId,
        Guid JsonFtId,
        Guid MdFtId);

    private static async Task<WorkflowScenarioSeed> SeedTwoStageWorkflowScenarioAsync(
        HttpClient adminClient,
        string adminCsrf,
        Guid devUserId,
        string suffix)
    {
        var (permCatCode, permCatDoc) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Get, "/api/v1/permissions");
        Assert.Equal(HttpStatusCode.OK, permCatCode);
        var permMap = permCatDoc!.RootElement.GetProperty("permissions").EnumerateArray()
            .ToDictionary(p => p.GetProperty("code").GetString()!, p => p.GetProperty("id").GetGuid(), StringComparer.OrdinalIgnoreCase);

        // Roles: BaseViewerRole (workflows.view), RoleA (workflows.view + workflows.decide), RoleB (workflows.view + workflows.decide)
        var (_, rBaseDoc) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/roles",
            new { name = $"BaseViewer_{suffix}", description = "View workflows only" });
        var baseRoleId = rBaseDoc!.RootElement.GetProperty("id").GetGuid();
        var (basePermCode, _) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Put, $"/api/v1/roles/{baseRoleId}/permissions",
            new { permissionIds = new[] { permMap["integrations.view"], permMap["workflows.view"] } });
        Assert.Equal(HttpStatusCode.OK, basePermCode);

        var (_, rADoc) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/roles",
            new { name = $"SecurityStageRole_{suffix}", description = "Stage 1 Reviewer Role" });
        var roleAId = rADoc!.RootElement.GetProperty("id").GetGuid();
        var (aPermCode, _) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Put, $"/api/v1/roles/{roleAId}/permissions",
            new
            {
                permissionIds = new[]
                {
                    permMap["integrations.view"], permMap["workflows.view"], permMap["workflows.decide"],
                    permMap["revisions.view"], permMap["files.view"]
                }
            });
        Assert.Equal(HttpStatusCode.OK, aPermCode);

        var (_, rBDoc) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/roles",
            new { name = $"ArchStageRole_{suffix}", description = "Stage 2 Reviewer Role" });
        var roleBId = rBDoc!.RootElement.GetProperty("id").GetGuid();
        var (bPermCode, _) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Put, $"/api/v1/roles/{roleBId}/permissions",
            new
            {
                permissionIds = new[]
                {
                    permMap["integrations.view"], permMap["workflows.view"], permMap["workflows.decide"],
                    permMap["revisions.view"], permMap["files.view"]
                }
            });
        Assert.Equal(HttpStatusCode.OK, bPermCode);

        var (_, sys1) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/company-systems",
            new { name = $"CoreBank {suffix}", ownerEmail = "core@api-governance.local", description = "Provider" });
        var (_, sys2) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/company-systems",
            new { name = $"PartnerApp {suffix}", ownerEmail = "partner@api-governance.local", description = "Consumer" });
        var sys1Id = sys1!.RootElement.GetProperty("id").GetGuid();
        var sys2Id = sys2!.RootElement.GetProperty("id").GetGuid();

        await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/internal-providers",
            new { companySystemId = sys1Id, description = $"Internal {suffix}" });
        var (_, grp) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/api-groups",
            new { name = $"WF Group {suffix}", description = "Workflow tests" });
        var grpId = grp!.RootElement.GetProperty("id").GetGuid();

        var (_, ftJson) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/file-types",
            new { catalogCode = "OPENAPI_SPEC", displayName = "OpenAPI Spec", maxSizeBytes = 204800L });
        var jsonFtId = ftJson!.RootElement.GetProperty("id").GetGuid();

        var (_, ftMd) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/file-types",
            new { catalogCode = "MARKDOWN_DOC", displayName = "Markdown Notes", maxSizeBytes = 204800L });
        var mdFtId = ftMd!.RootElement.GetProperty("id").GetGuid();

        var (_, tpl) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/requirement-templates",
            new
            {
                name = $"WF Template {suffix}",
                rules = new[]
                {
                    new { fileTypeId = jsonFtId, requirementMode = "REQUIRED", minCount = 1, maxCount = 2 },
                    new { fileTypeId = mdFtId, requirementMode = "OPTIONAL", minCount = 0, maxCount = 2 }
                }
            });
        var tplId = tpl!.RootElement.GetProperty("id").GetGuid();

        var (_, route) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/approval-routes",
            new
            {
                name = $"2-Stage Route {suffix}",
                stages = new[]
                {
                    new { position = 1, stageName = "Security Review", responsibleRoleId = roleAId },
                    new { position = 2, stageName = "Architecture Signoff", responsibleRoleId = roleBId }
                }
            });
        var routeId = route!.RootElement.GetProperty("id").GetGuid();

        var (_, ct) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/change-types",
            new { name = $"WF Change {suffix}", routeId });
        var ctId = ct!.RootElement.GetProperty("id").GetGuid();

        var (_, pol) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/change-policies",
            new { name = $"WF Policy {suffix}", changeTypeIds = new[] { ctId } });
        var polId = pol!.RootElement.GetProperty("id").GetGuid();

        var (_, doc) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/api-documents",
            new
            {
                providerInternalSystemId = sys1Id,
                consumerCompanySystemId = sys2Id,
                apiGroupId = grpId,
                requirementTemplateId = tplId,
                approvalRouteId = routeId,
                changePolicyId = polId,
                publicationOption = "MANUAL",
                state = "ACTIVE",
                developerUserIds = Array.Empty<Guid>()
            });
        var docId = doc!.RootElement.GetProperty("id").GetGuid();

        var (_, integ) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/integrations",
            new
            {
                documentId = docId,
                referenceCode = $"INT-WF-{suffix}",
                developerUserIds = new[] { devUserId }
            });
        var integrationId = integ!.RootElement.GetProperty("id").GetGuid();

        var (_, verDoc) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post,
            $"/api/v1/integrations/{integrationId}/versions",
            new { versionLabel = $"v1-{suffix}" });
        var versionId = verDoc!.RootElement.GetProperty("id").GetGuid();

        return new WorkflowScenarioSeed(docId, integrationId, versionId, routeId, roleAId, roleBId, baseRoleId, jsonFtId, mdFtId);
    }

    private static async Task SetDocumentAccessAsync(
        HttpClient adminClient,
        string adminCsrf,
        Guid documentId,
        Guid adminUserId,
        IEnumerable<(Guid RoleId, string PermissionCode, string Decision)> roleRules,
        IEnumerable<(Guid UserId, string PermissionCode, string Decision)> userRules)
    {
        var fullUserRules = DocumentPermissionCatalog.All
            .Select(p => new { userId = adminUserId, permissionCode = p.Code, decision = "GRANT" })
            .Concat(userRules.Select(u => new { userId = u.UserId, permissionCode = u.PermissionCode, decision = u.Decision }))
            .ToArray();

        var fullRoleRules = roleRules
            .Select(r => new { roleId = r.RoleId, permissionCode = r.PermissionCode, decision = r.Decision })
            .ToArray();

        var (code, _) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Put,
            $"/api/v1/api-documents/{documentId}/access",
            new { rolePermissions = fullRoleRules, userPermissions = fullUserRules });
        Assert.Equal(HttpStatusCode.OK, code);
    }

    [Fact]
    public async Task W01_W02_W03_SequentialApprovalEngine_EnforcesSnapshotEligibilityConcurrency_AndFinalApproval()
    {
        await ResetIsolatedDatabaseAsync();
        await using var clamAv = new EmbeddedCleanClamAvServer();
        var (factory, spy, dpDir, storageDir) = CreateFactory(clamAv.Port);

        try
        {
            var (adminId, adminClient, adminCsrf) = await BootstrapAndLoginAdminAsync(factory);
            var seed = await SeedTwoStageWorkflowScenarioAsync(adminClient, adminCsrf, adminId, "SEQ");

            // Grant RoleA and RoleB doc.view + doc.review on the document;
            // UserNoReview holds RoleA but receives explicit REVOKE on doc.review (ADR-01 precedence)
            var (_, revA1Client, revA1Csrf) = await CreateAndLoginUserAsync(
                factory, spy, adminClient, adminCsrf, "rev-a1@api-governance.local", "Reviewer A1",
                [seed.BaseViewerRoleId, seed.RoleAId]);

            var (_, revA2Client, revA2Csrf) = await CreateAndLoginUserAsync(
                factory, spy, adminClient, adminCsrf, "rev-a2@api-governance.local", "Reviewer A2",
                [seed.RoleAId]);

            var (_, revBClient, revBCsrf) = await CreateAndLoginUserAsync(
                factory, spy, adminClient, adminCsrf, "rev-b@api-governance.local", "Reviewer B",
                [seed.RoleBId]);

            var (noRevUserId, noRevClient, noRevCsrf) = await CreateAndLoginUserAsync(
                factory, spy, adminClient, adminCsrf, "no-docrev@api-governance.local", "Revoked DocReview User",
                [seed.RoleAId]);

            await SetDocumentAccessAsync(
                adminClient, adminCsrf, seed.DocumentId, adminId,
                [
                    (seed.RoleAId, "doc.view", "GRANT"),
                    (seed.RoleAId, "doc.review", "GRANT"),
                    (seed.RoleBId, "doc.view", "GRANT"),
                    (seed.RoleBId, "doc.review", "GRANT"),
                ],
                [
                    (noRevUserId, "doc.review", "REVOKE"),
                ]);

            // Create Draft Revision #1, upload clean OpenAPI JSON, submit!
            var (_, revDoc) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post,
                $"/api/v1/versions/{seed.VersionId}/revisions");
            var revisionId = revDoc!.RootElement.GetProperty("id").GetGuid();

            var (upCode, _) = await UploadMultipartAsync(adminClient, adminCsrf,
                $"/api/v1/revisions/{revisionId}/files",
                seed.JsonFtId, "payments.json", Encoding.UTF8.GetBytes("{\"openapi\":\"3.1.0\"}"));
            Assert.Equal(HttpStatusCode.Created, upCode);

            var (subCode, subDoc) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post,
                $"/api/v1/revisions/{revisionId}/submit");
            Assert.Equal(HttpStatusCode.OK, subCode);
            var runId = subDoc!.RootElement.GetProperty("workflowRunId").GetGuid();

            // W01 Snapshot Isolation test: edit the approval route AFTER submission -> snapshot must remain unchanged!
            var (routePutCode, _) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Put,
                $"/api/v1/approval-routes/{seed.RouteId}/stages",
                new
                {
                    stages = new[]
                    {
                        new { position = 1, stageName = "Completely Renamed Route Stage", responsibleRoleId = seed.BaseViewerRoleId }
                    }
                });
            Assert.Equal(HttpStatusCode.OK, routePutCode);

            var (runGetCode, runDoc) = await SendJsonAsync(revA1Client, revA1Csrf, HttpMethod.Get, $"/api/v1/workflows/{runId}");
            Assert.Equal(HttpStatusCode.OK, runGetCode);
            var stages = runDoc!.RootElement.GetProperty("stages").EnumerateArray().ToList();
            Assert.Equal(2, stages.Count);
            Assert.Equal("Security Review", stages[0].GetProperty("stageNameSnapshot").GetString());
            Assert.Equal("ACTIVE", stages[0].GetProperty("status").GetString());
            Assert.Equal("Architecture Signoff", stages[1].GetProperty("stageNameSnapshot").GetString());
            Assert.Equal("PENDING", stages[1].GetProperty("status").GetString());
            Assert.True(runDoc.RootElement.GetProperty("canCurrentUserDecideActiveStage").GetBoolean());

            var stage1Id = stages[0].GetProperty("id").GetGuid();
            var stage2Id = stages[1].GetProperty("id").GetGuid();

            // Inbox checks: RoleA sees Stage 1, RoleB does NOT see Stage 1 yet, Revoked doc.review sees canDecide=false
            var (_, inboxADoc) = await SendJsonAsync(revA1Client, revA1Csrf, HttpMethod.Get, "/api/v1/workflows/inbox");
            Assert.Single(inboxADoc!.RootElement.GetProperty("items").EnumerateArray());
            Assert.True(inboxADoc.RootElement.GetProperty("items")[0].GetProperty("canDecide").GetBoolean());

            var (_, inboxBDoc) = await SendJsonAsync(revBClient, revBCsrf, HttpMethod.Get, "/api/v1/workflows/inbox");
            Assert.Empty(inboxBDoc!.RootElement.GetProperty("items").EnumerateArray());

            var (_, inboxNoRevDoc) = await SendJsonAsync(noRevClient, noRevCsrf, HttpMethod.Get, "/api/v1/workflows/inbox");
            Assert.False(inboxNoRevDoc!.RootElement.GetProperty("items")[0].GetProperty("canDecide").GetBoolean());

            // Negative W02 checks before Stage 1 decision:
            // (a) Revoked doc.review user gets 403 Forbidden
            var (noRevDecideCode, _) = await SendJsonAsync(noRevClient, noRevCsrf, HttpMethod.Post,
                $"/api/v1/workflows/{runId}/stages/{stage1Id}/decisions",
                new { decision = "APPROVE", notes = "Should be forbidden" });
            Assert.Equal(HttpStatusCode.Forbidden, noRevDecideCode);

            // (b) Wrong role (RoleB trying to decide Stage 1 owned by RoleA) gets 403 Forbidden
            var (wrongRoleCode, _) = await SendJsonAsync(revBClient, revBCsrf, HttpMethod.Post,
                $"/api/v1/workflows/{runId}/stages/{stage1Id}/decisions",
                new { decision = "APPROVE" });
            Assert.Equal(HttpStatusCode.Forbidden, wrongRoleCode);

            // (c) Acting on PENDING Stage 2 before Stage 1 is approved gets 422 UnprocessableEntity
            var (earlyStage2Code, _) = await SendJsonAsync(revBClient, revBCsrf, HttpMethod.Post,
                $"/api/v1/workflows/{runId}/stages/{stage2Id}/decisions",
                new { decision = "APPROVE" });
            Assert.Equal(HttpStatusCode.UnprocessableEntity, earlyStage2Code);

            // W02 Concurrent double-member APPROVE on Stage 1: only ONE succeeds (200), other gets 409 Conflict!
            var tA1 = SendJsonAsync(revA1Client, revA1Csrf, HttpMethod.Post,
                $"/api/v1/workflows/{runId}/stages/{stage1Id}/decisions",
                new { decision = "APPROVE", notes = "Looks solid from Security." });
            var tA2 = SendJsonAsync(revA2Client, revA2Csrf, HttpMethod.Post,
                $"/api/v1/workflows/{runId}/stages/{stage1Id}/decisions",
                new { decision = "APPROVE", notes = "Concurrent duplicate attempt." });
            var concurrentResults = await Task.WhenAll(tA1, tA2);

            Assert.Single(concurrentResults, r => r.Status == HttpStatusCode.OK);
            Assert.Single(concurrentResults, r => r.Status == HttpStatusCode.Conflict);

            var stage1Outcome = concurrentResults.First(r => r.Status == HttpStatusCode.OK).Doc!;
            Assert.Equal("IN_PROGRESS", stage1Outcome.RootElement.GetProperty("outcomeStatus").GetString());
            Assert.Equal("IN_REVIEW", stage1Outcome.RootElement.GetProperty("run").GetProperty("revisionStatus").GetString());
            Assert.Equal("APPROVED", stage1Outcome.RootElement.GetProperty("run").GetProperty("stages")[0].GetProperty("status").GetString());
            Assert.Equal("ACTIVE", stage1Outcome.RootElement.GetProperty("run").GetProperty("stages")[1].GetProperty("status").GetString());

            // Stage 2 now appears in Reviewer B's inbox, and vanishes from Reviewer A1's inbox!
            var (_, inboxBNowDoc) = await SendJsonAsync(revBClient, revBCsrf, HttpMethod.Get, "/api/v1/workflows/inbox");
            Assert.Single(inboxBNowDoc!.RootElement.GetProperty("items").EnumerateArray());
            var (_, inboxANowDoc) = await SendJsonAsync(revA1Client, revA1Csrf, HttpMethod.Get, "/api/v1/workflows/inbox");
            Assert.Empty(inboxANowDoc!.RootElement.GetProperty("items").EnumerateArray());

            // W03: Final stage (Stage 2) APPROVE completes WorkflowRun & approves Revision!
            var (finalCode, finalDoc) = await SendJsonAsync(revBClient, revBCsrf, HttpMethod.Post,
                $"/api/v1/workflows/{runId}/stages/{stage2Id}/decisions",
                new { decision = "APPROVE", notes = "Architecture approved." });
            Assert.Equal(HttpStatusCode.OK, finalCode);
            Assert.Equal("APPROVED", finalDoc!.RootElement.GetProperty("outcomeStatus").GetString());
            Assert.Equal("APPROVED", finalDoc.RootElement.GetProperty("run").GetProperty("revisionStatus").GetString());

            // Revision is now APPROVED & protected; History returns 2 stage approvals
            var (_, histDoc) = await SendJsonAsync(revA1Client, revA1Csrf, HttpMethod.Get, $"/api/v1/workflows/{runId}/history");
            Assert.Equal(2, histDoc!.RootElement.GetProperty("entries").GetArrayLength());
        }
        finally
        {
            await factory.DisposeAsync();
            if (Directory.Exists(dpDir)) Directory.Delete(dpDir, true);
            if (Directory.Exists(storageDir)) Directory.Delete(storageDir, true);
        }
    }

    [Fact]
    public async Task W04_ReturnForRework_And_Reject_RequireMandatoryNotes_SupportResubmission_AndPreserveHistory()
    {
        await ResetIsolatedDatabaseAsync();
        await using var clamAv = new EmbeddedCleanClamAvServer();
        var (factory, spy, dpDir, storageDir) = CreateFactory(clamAv.Port);

        try
        {
            var (adminId, adminClient, adminCsrf) = await BootstrapAndLoginAdminAsync(factory);
            var seed = await SeedTwoStageWorkflowScenarioAsync(adminClient, adminCsrf, adminId, "RWK");

            await SetDocumentAccessAsync(
                adminClient, adminCsrf, seed.DocumentId, adminId,
                [
                    (seed.RoleAId, "doc.view", "GRANT"),
                    (seed.RoleAId, "doc.review", "GRANT"),
                    (seed.RoleBId, "doc.view", "GRANT"),
                    (seed.RoleBId, "doc.review", "GRANT"),
                ],
                []);

            var (_, revAClient, revACsrf) = await CreateAndLoginUserAsync(
                factory, spy, adminClient, adminCsrf, "rev-a@api-governance.local", "Reviewer A", [seed.RoleAId]);
            var (_, revBClient, revBCsrf) = await CreateAndLoginUserAsync(
                factory, spy, adminClient, adminCsrf, "rev-b@api-governance.local", "Reviewer B", [seed.RoleBId]);

            var (_, revDoc) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post,
                $"/api/v1/versions/{seed.VersionId}/revisions");
            var revisionId = revDoc!.RootElement.GetProperty("id").GetGuid();

            await UploadMultipartAsync(adminClient, adminCsrf,
                $"/api/v1/revisions/{revisionId}/files",
                seed.JsonFtId, "draft-v1.json", Encoding.UTF8.GetBytes("{\"openapi\":\"3.1.0\"}"));

            var (sub1Code, sub1Doc) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post,
                $"/api/v1/revisions/{revisionId}/submit");
            Assert.Equal(HttpStatusCode.OK, sub1Code);
            var run1Id = sub1Doc!.RootElement.GetProperty("workflowRunId").GetGuid();

            var (_, run1Doc) = await SendJsonAsync(revAClient, revACsrf, HttpMethod.Get, $"/api/v1/workflows/{run1Id}");
            var run1Stage1Id = run1Doc!.RootElement.GetProperty("stages")[0].GetProperty("id").GetGuid();

            // Mandatory notes guard for RETURN & REJECT -> 400 BadRequest
            var (emptyReturnCode, _) = await SendJsonAsync(revAClient, revACsrf, HttpMethod.Post,
                $"/api/v1/workflows/{run1Id}/stages/{run1Stage1Id}/decisions",
                new { decision = "RETURN", notes = "   " });
            Assert.Equal(HttpStatusCode.BadRequest, emptyReturnCode);

            var (emptyRejectCode, _) = await SendJsonAsync(revAClient, revACsrf, HttpMethod.Post,
                $"/api/v1/workflows/{run1Id}/stages/{run1Stage1Id}/decisions",
                new { decision = "REJECT", notes = "" });
            Assert.Equal(HttpStatusCode.BadRequest, emptyRejectCode);

            // RETURN with mandatory notes -> Run #1 becomes RETURNED, Stage 2 CANCELLED, Revision becomes RETURNED (editable!)
            var (retCode, retDoc) = await SendJsonAsync(revAClient, revACsrf, HttpMethod.Post,
                $"/api/v1/workflows/{run1Id}/stages/{run1Stage1Id}/decisions",
                new { decision = "RETURN", notes = "Missing OAuth2 scope definitions in securitySchemes." });
            Assert.Equal(HttpStatusCode.OK, retCode);
            Assert.Equal("RETURNED", retDoc!.RootElement.GetProperty("outcomeStatus").GetString());
            Assert.Equal("CANCELLED", retDoc.RootElement.GetProperty("run").GetProperty("stages")[1].GetProperty("status").GetString());
            Assert.Equal("RETURNED", retDoc.RootElement.GetProperty("run").GetProperty("revisionStatus").GetString());

            // Developer edits RETURNED revision by adding updated OpenAPI file & re-submits -> starts WorkflowRun #2!
            var (reworkUpCode, _) = await UploadMultipartAsync(adminClient, adminCsrf,
                $"/api/v1/revisions/{revisionId}/files",
                seed.JsonFtId, "draft-v2-fixed.json", Encoding.UTF8.GetBytes("{\"openapi\":\"3.1.0\",\"fixed\":true}"));
            Assert.Equal(HttpStatusCode.Created, reworkUpCode);

            var (sub2Code, sub2Doc) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post,
                $"/api/v1/revisions/{revisionId}/submit");
            Assert.Equal(HttpStatusCode.OK, sub2Code);
            var run2Id = sub2Doc!.RootElement.GetProperty("workflowRunId").GetGuid();
            Assert.NotEqual(run1Id, run2Id);

            // Approve Stage 1 on Run #2, then REJECT Stage 2 with mandatory notes -> Revision locked REJECTED!
            var (_, run2Doc) = await SendJsonAsync(revAClient, revACsrf, HttpMethod.Get, $"/api/v1/workflows/{run2Id}");
            var run2Stage1Id = run2Doc!.RootElement.GetProperty("stages")[0].GetProperty("id").GetGuid();
            var run2Stage2Id = run2Doc.RootElement.GetProperty("stages")[1].GetProperty("id").GetGuid();

            Assert.Equal(HttpStatusCode.OK, (await SendJsonAsync(revAClient, revACsrf, HttpMethod.Post,
                $"/api/v1/workflows/{run2Id}/stages/{run2Stage1Id}/decisions",
                new { decision = "APPROVE", notes = "Rework verified." })).Status);

            var (rejCode, rejDoc) = await SendJsonAsync(revBClient, revBCsrf, HttpMethod.Post,
                $"/api/v1/workflows/{run2Id}/stages/{run2Stage2Id}/decisions",
                new { decision = "REJECT", notes = "Rejected due to incompatible breaking contract." });
            Assert.Equal(HttpStatusCode.OK, rejCode);
            Assert.Equal("REJECTED", rejDoc!.RootElement.GetProperty("outcomeStatus").GetString());
            Assert.Equal("REJECTED", rejDoc.RootElement.GetProperty("run").GetProperty("revisionStatus").GetString());

            // REJECTED revision must be immutable & block further uploads/resubmissions (422)
            var (blockedAfterRejectCode, _) = await UploadMultipartAsync(adminClient, adminCsrf,
                $"/api/v1/revisions/{revisionId}/files",
                seed.MdFtId, "after-reject.md", Encoding.UTF8.GetBytes("# Blocked"));
            Assert.Equal(HttpStatusCode.UnprocessableEntity, blockedAfterRejectCode);

            // Full multi-run history preserved cleanly (Run #1 RETURN + Run #2 APPROVE & REJECT = 3 decisions!)
            var (_, histDoc) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Get, $"/api/v1/workflows/{run2Id}/history");
            var entries = histDoc!.RootElement.GetProperty("entries").EnumerateArray().ToList();
            Assert.Equal(3, entries.Count);
            Assert.Contains(entries, e =>
                e.GetProperty("decision").GetProperty("decision").GetString() == "RETURN" &&
                e.GetProperty("decision").GetProperty("notes").GetString()!.Contains("OAuth2 scope"));
            Assert.Contains(entries, e =>
                e.GetProperty("decision").GetProperty("decision").GetString() == "REJECT" &&
                e.GetProperty("decision").GetProperty("notes").GetString()!.Contains("breaking contract"));
        }
        finally
        {
            await factory.DisposeAsync();
            if (Directory.Exists(dpDir)) Directory.Delete(dpDir, true);
            if (Directory.Exists(storageDir)) Directory.Delete(storageDir, true);
        }
    }
}
