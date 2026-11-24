using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
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
public sealed class VersionsFilesS08IntegrationTests
{
    private sealed class CapturingDeliverySpy : IAuthTokenDeliveryChannel
    {
        public string ChannelCode => "TEST_S08_SPY";
        public List<AuthTokenDeliveryContext> Deliveries { get; } = [];

        public Task DeliverPasswordResetTokenAsync(
            AuthTokenDeliveryContext context,
            CancellationToken cancellationToken = default)
        {
            Deliveries.Add(context);
            return Task.CompletedTask;
        }
    }

    private sealed class EmbeddedClamAvTcpServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _acceptLoop;

        public int Port { get; }

        public EmbeddedClamAvTcpServer()
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
                    await HandleClientAsync(client, _cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    // Continue serving remaining requests during test.
                }
            }
        }

        private static async Task HandleClientAsync(TcpClient client, CancellationToken ct)
        {
            await using var stream = client.GetStream();
            var cmdBuf = new byte[10];
            var cmdRead = 0;
            while (cmdRead < cmdBuf.Length)
            {
                var n = await stream.ReadAsync(cmdBuf.AsMemory(cmdRead), ct);
                if (n == 0) return;
                cmdRead += n;
            }

            using var payload = new MemoryStream();
            var lenBuf = new byte[4];
            while (true)
            {
                var lr = await ReadExactAsync(stream, lenBuf, ct);
                if (!lr) return;
                var chunkLen = BinaryPrimitives.ReadUInt32BigEndian(lenBuf);
                if (chunkLen == 0)
                {
                    break;
                }

                var chunk = new byte[chunkLen];
                if (!await ReadExactAsync(stream, chunk, ct)) return;
                await payload.WriteAsync(chunk, ct);
            }

            var text = Encoding.UTF8.GetString(payload.ToArray());
            var response = text.Contains("S08-CLAMAV-MALWARE-SIGNATURE-TEST", StringComparison.Ordinal)
                ? "stream: S08-Test-Malware FOUND\0"
                : "stream: OK\0";

            await stream.WriteAsync(Encoding.UTF8.GetBytes(response), ct);
            await stream.FlushAsync(ct);
        }

        private static async Task<bool> ReadExactAsync(NetworkStream stream, byte[] buffer, CancellationToken ct)
        {
            var offset = 0;
            while (offset < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), ct);
                if (read == 0) return false;
                offset += read;
            }
            return true;
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
        CreateFactory(string? clamAvHost = null, int clamAvPort = 3310)
    {
        var dpDir = Path.Combine(Path.GetTempPath(), "ApiGovTestDP", Guid.NewGuid().ToString("N"));
        var storageDir = Path.Combine(Path.GetTempPath(), "ApiGovPrivateStore", Guid.NewGuid().ToString("N"));
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
                        ["AuthenticationPolicy:LoginRateLimitPermitLimit"] = "200",
                        ["FileStorage:RootPath"] = storageDir,
                        ["FileScanning:Provider"] = "ClamAv",
                        ["FileScanning:ClamAvHost"] = clamAvHost ?? string.Empty,
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
        WebApplicationFactory<Program> factory,
        CapturingDeliverySpy spy)
    {
        var adminEmail = $"s08-admin-{Guid.NewGuid():N}@api-governance.local";
        const string adminPwd = "S08#FilesVersions!Pass99";

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var bootstrap = scope.ServiceProvider.GetRequiredService<IInitialAdminBootstrapService>();
            var res = await bootstrap.BootstrapFirstAdminAsync(new(adminEmail, "S08 Admin", adminPwd));
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
        byte[] content,
        string browserMediaType = "application/octet-stream")
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(fileTypeId.ToString()), "fileTypeId");
        var fileContent = new ByteArrayContent(content);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(browserMediaType);
        form.Add(fileContent, "file", fileName);

        using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = form };
        req.Headers.Add("X-CSRF-TOKEN", csrf);

        using var res = await client.SendAsync(req);
        var raw = await res.Content.ReadAsStringAsync();
        var doc = string.IsNullOrWhiteSpace(raw) ? null : JsonDocument.Parse(raw);
        return (res.StatusCode, doc);
    }

    private static async Task<(Guid IntegrationId, Guid JsonFileTypeId, Guid MdFileTypeId, Guid WordFileTypeId)>
        SeedIntegrationScenarioAsync(HttpClient client, string csrf, Guid devUserId)
    {
        var (_, sys1) = await SendJsonAsync(client, csrf, HttpMethod.Post, "/api/v1/company-systems",
            new { name = "CoreBank S08", ownerEmail = "core@api-governance.local", description = "Provider" });
        var (_, sys2) = await SendJsonAsync(client, csrf, HttpMethod.Post, "/api/v1/company-systems",
            new { name = "Channel S08", ownerEmail = "ch@api-governance.local", description = "Consumer" });

        var sys1Id = sys1!.RootElement.GetProperty("id").GetGuid();
        var sys2Id = sys2!.RootElement.GetProperty("id").GetGuid();

        await SendJsonAsync(client, csrf, HttpMethod.Post, "/api/v1/internal-providers",
            new { companySystemId = sys1Id, description = "Internal S08" });

        var (_, grp) = await SendJsonAsync(client, csrf, HttpMethod.Post, "/api/v1/api-groups",
            new { name = "S08 File Group", description = "Test" });
        var grpId = grp!.RootElement.GetProperty("id").GetGuid();

        var (_, ftJson) = await SendJsonAsync(client, csrf, HttpMethod.Post, "/api/v1/file-types",
            new { catalogCode = "OPENAPI_SPEC", displayName = "OpenAPI Spec", maxSizeBytes = 102400L });
        var jsonFtId = ftJson!.RootElement.GetProperty("id").GetGuid();

        var (_, ftMd) = await SendJsonAsync(client, csrf, HttpMethod.Post, "/api/v1/file-types",
            new { catalogCode = "MARKDOWN_DOC", displayName = "Markdown Notes", maxSizeBytes = 102400L });
        var mdFtId = ftMd!.RootElement.GetProperty("id").GetGuid();

        var (_, ftWord) = await SendJsonAsync(client, csrf, HttpMethod.Post, "/api/v1/file-types",
            new { catalogCode = "WORD_DOC", displayName = "Word Doc", maxSizeBytes = 204800L });
        var wordFtId = ftWord!.RootElement.GetProperty("id").GetGuid();

        var (_, tpl) = await SendJsonAsync(client, csrf, HttpMethod.Post, "/api/v1/requirement-templates",
            new
            {
                name = "S08 Template",
                rules = new[]
                {
                    new { fileTypeId = jsonFtId, requirementMode = "REQUIRED", minCount = 1, maxCount = 2 },
                    new { fileTypeId = mdFtId, requirementMode = "OPTIONAL", minCount = 0, maxCount = 2 },
                    new { fileTypeId = wordFtId, requirementMode = "OPTIONAL", minCount = 0, maxCount = 1 }
                }
            });
        var tplId = tpl!.RootElement.GetProperty("id").GetGuid();

        var (_, rolesDoc) = await SendJsonAsync(client, csrf, HttpMethod.Get, "/api/v1/roles?pageSize=20");
        var adminRoleId = rolesDoc!.RootElement.GetProperty("items")[0].GetProperty("id").GetGuid();

        var (_, route) = await SendJsonAsync(client, csrf, HttpMethod.Post, "/api/v1/approval-routes",
            new { name = "S08 Route", stages = new[] { new { position = 1, stageName = "Security", responsibleRoleId = adminRoleId } } });
        var routeId = route!.RootElement.GetProperty("id").GetGuid();

        var (_, ct) = await SendJsonAsync(client, csrf, HttpMethod.Post, "/api/v1/change-types",
            new { name = "Spec Fix", routeId });
        var ctId = ct!.RootElement.GetProperty("id").GetGuid();

        var (_, pol) = await SendJsonAsync(client, csrf, HttpMethod.Post, "/api/v1/change-policies",
            new { name = "S08 Policy", changeTypeIds = new[] { ctId } });
        var polId = pol!.RootElement.GetProperty("id").GetGuid();

        var (_, doc) = await SendJsonAsync(client, csrf, HttpMethod.Post, "/api/v1/api-documents",
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

        var (_, integration) = await SendJsonAsync(client, csrf, HttpMethod.Post, "/api/v1/integrations",
            new
            {
                documentId = docId,
                referenceCode = "INT-S08-VER-01",
                developerUserIds = new[] { devUserId }
            });
        var integrationId = integration!.RootElement.GetProperty("id").GetGuid();

        return (integrationId, jsonFtId, mdFtId, wordFtId);
    }

    private static byte[] CreateMinimalValidDocx(bool includeVbaMacro = false)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var ct = zip.CreateEntry("[Content_Types].xml");
            using (var w = new StreamWriter(ct.Open(), Encoding.UTF8))
            {
                w.Write("<?xml version=\"1.0\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"></Types>");
            }

            var doc = zip.CreateEntry("word/document.xml");
            using (var w = new StreamWriter(doc.Open(), Encoding.UTF8))
            {
                w.Write("<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body/></w:document>");
            }

            if (includeVbaMacro)
            {
                var macro = zip.CreateEntry("word/vbaProject.bin");
                using var w = new BinaryWriter(macro.Open());
                w.Write(new byte[] { 0x01, 0x02, 0x03 });
            }
        }

        return ms.ToArray();
    }

    [Fact]
    public async Task V01_V04_UnconfiguredScanner_FailsClosed_Quarantines_And_BlocksAvailability()
    {
        await ResetIsolatedDatabaseAsync();
        var (factory, spy, dpDir, storageDir) = CreateFactory(clamAvHost: null);

        try
        {
            var (adminId, client, csrf) = await BootstrapAndLoginAdminAsync(factory, spy);
            var (integrationId, jsonFtId, mdFtId, wordFtId) = await SeedIntegrationScenarioAsync(client, csrf, adminId);

            // 1. V04: Free-text manual API version creation (zero SemVer enforcement, separate from revisions)
            var (verCode, verDoc) = await SendJsonAsync(client, csrf, HttpMethod.Post,
                $"/api/v1/integrations/{integrationId}/versions",
                new { versionLabel = "V2026-AR-إصدار-أولي" });
            Assert.Equal(HttpStatusCode.Created, verCode);
            var versionId = verDoc!.RootElement.GetProperty("id").GetGuid();
            Assert.Equal("V2026-AR-إصدار-أولي", verDoc.RootElement.GetProperty("versionLabel").GetString());
            Assert.Equal(0, verDoc.RootElement.GetProperty("revisionsCount").GetInt32());

            // Duplicate version label returns 409 Conflict
            var (dupCode, _) = await SendJsonAsync(client, csrf, HttpMethod.Post,
                $"/api/v1/integrations/{integrationId}/versions",
                new { versionLabel = "V2026-AR-إصدار-أولي" });
            Assert.Equal(HttpStatusCode.Conflict, dupCode);

            // Initialize independent DRAFT package revision #1
            var (revCode, revDoc) = await SendJsonAsync(client, csrf, HttpMethod.Post,
                $"/api/v1/versions/{versionId}/revisions");
            Assert.Equal(HttpStatusCode.Created, revCode);
            var revisionId = revDoc!.RootElement.GetProperty("id").GetGuid();
            Assert.Equal(1, revDoc.RootElement.GetProperty("revisionNumber").GetInt32());

            // 2. V02 Validation rejections:
            // Double dangerous extension (spec.exe.json) -> 422
            var (dblExtCode, _) = await UploadMultipartAsync(client, csrf, $"/api/v1/revisions/{revisionId}/files",
                jsonFtId, "spec.exe.json", Encoding.UTF8.GetBytes("{\"openapi\":\"3.1.0\"}"));
            Assert.Equal(HttpStatusCode.UnprocessableEntity, dblExtCode);

            // Executable MZ bytes disguised as .json -> 422 & quarantined INVALID
            var (mzCode, mzDoc) = await UploadMultipartAsync(client, csrf, $"/api/v1/revisions/{revisionId}/files",
                jsonFtId, "payload.json", new byte[] { (byte)'M', (byte)'Z', 0x90, 0x00, 0x03 }, "application/json");
            Assert.Equal(HttpStatusCode.UnprocessableEntity, mzCode);
            Assert.Equal("FILE_EXECUTABLE_SIGNATURE", mzDoc!.RootElement.GetProperty("details").GetProperty("validationCode").GetString());

            // Active <script> smuggling inside markdown -> 422 INVALID
            var (scriptCode, _) = await UploadMultipartAsync(client, csrf, $"/api/v1/revisions/{revisionId}/files",
                mdFtId, "notes.md", Encoding.UTF8.GetBytes("# Guide\n<script>alert(1)</script>"));
            Assert.Equal(HttpStatusCode.UnprocessableEntity, scriptCode);

            // Macro-enabled DOCX disguise (word/vbaProject.bin) -> 422 FILE_MACRO_FORBIDDEN
            var (macroCode, macroDoc) = await UploadMultipartAsync(client, csrf, $"/api/v1/revisions/{revisionId}/files",
                wordFtId, "template.docx", CreateMinimalValidDocx(includeVbaMacro: true));
            Assert.Equal(HttpStatusCode.UnprocessableEntity, macroCode);
            Assert.Equal("FILE_MACRO_FORBIDDEN", macroDoc!.RootElement.GetProperty("details").GetProperty("validationCode").GetString());

            // Oversized payload > fileType.MaxSizeBytes (102400 bytes) -> 413 PayloadTooLarge
            var oversizedBytes = Encoding.UTF8.GetBytes("{\"data\":\"" + new string('a', 110_000) + "\"}");
            var (bigCode, _) = await UploadMultipartAsync(client, csrf, $"/api/v1/revisions/{revisionId}/files",
                jsonFtId, "huge.json", oversizedBytes, "application/json");
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, bigCode);

            // 3. V03 Fail-Closed Quarantine when ClamAV host is absent (never simulated CLEAN!):
            var validJsonBytes = Encoding.UTF8.GetBytes("{\"openapi\":\"3.1.0\",\"info\":{\"title\":\"Core\",\"version\":\"1.0\"}}");
            var expectedSha = Convert.ToHexStringLower(SHA256.HashData(validJsonBytes));

            var (pendingCode, pendingDoc) = await UploadMultipartAsync(client, csrf, $"/api/v1/revisions/{revisionId}/files",
                jsonFtId, "openapi-spec.json", validJsonBytes, "application/json");
            Assert.Equal(HttpStatusCode.Accepted, pendingCode);

            var revFileId = pendingDoc!.RootElement.GetProperty("id").GetGuid();
            Assert.Equal("PENDING_SCAN", pendingDoc.RootElement.GetProperty("scanStatus").GetString());
            Assert.False(pendingDoc.RootElement.GetProperty("isAvailableForUse").GetBoolean());
            Assert.True(pendingDoc.RootElement.GetProperty("isQuarantined").GetBoolean());
            Assert.Equal(expectedSha, pendingDoc.RootElement.GetProperty("sha256").GetString());

            // Verify private objects/ has 0 files and quarantine/ holds the isolated blob!
            var objectsFiles = Directory.GetFiles(Path.Combine(storageDir, "objects"), "*", SearchOption.AllDirectories);
            var quarantineFiles = Directory.GetFiles(Path.Combine(storageDir, "quarantine"), "*", SearchOption.AllDirectories);
            Assert.Empty(objectsFiles);
            Assert.True(quarantineFiles.Length >= 1);

            // Verify scan-status endpoint & strict download block on PENDING_SCAN
            var (statusCode, statusDoc) = await SendJsonAsync(client, csrf, HttpMethod.Get, $"/api/v1/files/{revFileId}/scan-status");
            Assert.Equal(HttpStatusCode.OK, statusCode);
            Assert.Equal("QUARANTINE", statusDoc!.RootElement.GetProperty("storageTier").GetString());

            using var blockedDownloadRes = await client.GetAsync($"/api/v1/revisions/{revisionId}/files/{revFileId}/download");
            Assert.Equal(HttpStatusCode.UnprocessableEntity, blockedDownloadRes.StatusCode);

            // Delete pending link cleanly removes from DRAFT without error
            var (delCode, _) = await SendJsonAsync(client, csrf, HttpMethod.Delete, $"/api/v1/revisions/{revisionId}/files/{revFileId}");
            Assert.Equal(HttpStatusCode.NoContent, delCode);
        }
        finally
        {
            await factory.DisposeAsync();
            if (Directory.Exists(dpDir)) Directory.Delete(dpDir, true);
            if (Directory.Exists(storageDir)) Directory.Delete(storageDir, true);
        }
    }

    [Fact]
    public async Task V01_V03_ClamAvProtocol_QuarantinesInfected_CommitsCleanWithSha256_AndAllowsVerifiedDownload()
    {
        await ResetIsolatedDatabaseAsync();
        await using var clamAv = new EmbeddedClamAvTcpServer();
        var (factory, spy, dpDir, storageDir) = CreateFactory(clamAvHost: "127.0.0.1", clamAvPort: clamAv.Port);

        try
        {
            var (adminId, client, csrf) = await BootstrapAndLoginAdminAsync(factory, spy);
            var (integrationId, _, mdFtId, wordFtId) = await SeedIntegrationScenarioAsync(client, csrf, adminId);

            // Create non-assigned user with general permissions to verify assigned-developer / IDOR lock!
            var nonDevEmail = $"nondev-s08-{Guid.NewGuid():N}@api-governance.local";
            var (uCode, uDoc) = await SendJsonAsync(client, csrf, HttpMethod.Post, "/api/v1/users",
                new { email = nonDevEmail, fullName = "Non Assigned User", roleIds = Array.Empty<Guid>(), triggerActivationToken = true });
            Assert.Equal(HttpStatusCode.Created, uCode);
            var nonDevUserId = uDoc!.RootElement.GetProperty("id").GetGuid();
            var tok = spy.Deliveries.Last(d => d.TargetUserId == nonDevUserId).SingleUseToken;

            var nonDevClient = CreateCookieClient(factory);
            var nonDevCsrf = await FetchCsrfAsync(nonDevClient);
            const string nonDevPass = "NonDev#S08!Strong99";
            var (resetCode, _) = await SendJsonAsync(nonDevClient, nonDevCsrf, HttpMethod.Post, "/api/v1/auth/password/reset/complete",
                new { email = nonDevEmail, token = tok, newPassword = nonDevPass });
            Assert.Equal(HttpStatusCode.NoContent, resetCode);

            var (nonDevLoginCode, _) = await SendJsonAsync(nonDevClient, nonDevCsrf, HttpMethod.Post, "/api/v1/auth/login",
                new { email = nonDevEmail, password = nonDevPass });
            Assert.Equal(HttpStatusCode.OK, nonDevLoginCode);
            nonDevCsrf = await FetchCsrfAsync(nonDevClient);

            var (verCode, verDoc) = await SendJsonAsync(client, csrf, HttpMethod.Post,
                $"/api/v1/integrations/{integrationId}/versions",
                new { versionLabel = "pilot-release-v1" });
            Assert.Equal(HttpStatusCode.Created, verCode);
            var versionId = verDoc!.RootElement.GetProperty("id").GetGuid();

            // Non-assigned outsider gets 404 anti-IDOR when querying versions
            var (outsiderVerCode, _) = await SendJsonAsync(nonDevClient, nonDevCsrf, HttpMethod.Get,
                $"/api/v1/integrations/{integrationId}/versions");
            Assert.True(outsiderVerCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden);

            var (_, revDoc) = await SendJsonAsync(client, csrf, HttpMethod.Post, $"/api/v1/versions/{versionId}/revisions");
            var revisionId = revDoc!.RootElement.GetProperty("id").GetGuid();

            // 1. Upload file containing malware signature detected by ClamAV wire protocol -> 422 INFECTED, isolated in quarantine/, ZERO objects/ & ZERO revision_files
            var eicarBytes = Encoding.UTF8.GetBytes("# Infected Doc\nS08-CLAMAV-MALWARE-SIGNATURE-TEST");
            var (infCode, infDoc) = await UploadMultipartAsync(client, csrf, $"/api/v1/revisions/{revisionId}/files",
                mdFtId, "eicar-notes.md", eicarBytes, "text/markdown");
            Assert.Equal(HttpStatusCode.UnprocessableEntity, infCode);
            Assert.Equal("INFECTED", infDoc!.RootElement.GetProperty("details").GetProperty("scanStatus").GetString());
            Assert.Empty(Directory.GetFiles(Path.Combine(storageDir, "objects"), "*", SearchOption.AllDirectories));

            // 2. Upload valid clean DOCX -> ClamAV OK -> 201 Created, committed into objects/yyyy/MM/<id>.bin, SHA-256 verified!
            var cleanDocx = CreateMinimalValidDocx(includeVbaMacro: false);
            var expectedSha = Convert.ToHexStringLower(SHA256.HashData(cleanDocx));
            var (cleanCode, cleanDoc) = await UploadMultipartAsync(client, csrf, $"/api/v1/revisions/{revisionId}/files",
                wordFtId, "architecture-guide.docx", cleanDocx);
            Assert.Equal(HttpStatusCode.Created, cleanCode);

            var revFileId = cleanDoc!.RootElement.GetProperty("id").GetGuid();
            Assert.Equal("CLEAN", cleanDoc.RootElement.GetProperty("scanStatus").GetString());
            Assert.True(cleanDoc.RootElement.GetProperty("isAvailableForUse").GetBoolean());
            Assert.False(cleanDoc.RootElement.GetProperty("isQuarantined").GetBoolean());
            Assert.Equal(expectedSha, cleanDoc.RootElement.GetProperty("sha256").GetString());

            var permanentFiles = Directory.GetFiles(Path.Combine(storageDir, "objects"), "*.bin", SearchOption.AllDirectories);
            Assert.Single(permanentFiles);

            // 3. Verified download streams exact bytes with attachment + nosniff security headers
            using var dlRes = await client.GetAsync($"/api/v1/revisions/{revisionId}/files/{revFileId}/download");
            Assert.Equal(HttpStatusCode.OK, dlRes.StatusCode);
            Assert.Equal("nosniff", dlRes.Headers.GetValues("X-Content-Type-Options").Single());
            Assert.Equal("attachment", dlRes.Content.Headers.ContentDisposition?.DispositionType);
            var downloadedBytes = await dlRes.Content.ReadAsByteArrayAsync();
            Assert.Equal(cleanDocx, downloadedBytes);
        }
        finally
        {
            await factory.DisposeAsync();
            if (Directory.Exists(dpDir)) Directory.Delete(dpDir, true);
            if (Directory.Exists(storageDir)) Directory.Delete(storageDir, true);
        }
    }
}

