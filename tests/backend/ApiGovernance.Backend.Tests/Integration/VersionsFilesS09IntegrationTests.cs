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
using ApiGovernance.Shared.Persistence.Entities;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.CustomProperties;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.VariantTypes;
using DocumentFormat.OpenXml.Wordprocessing;
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
public sealed class VersionsFilesS09IntegrationTests
{
    private sealed class CapturingDeliverySpy : IAuthTokenDeliveryChannel
    {
        public string ChannelCode => "TEST_S09_SPY";
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
        public bool FailAllConnections { get; set; }

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
                    if (FailAllConnections)
                    {
                        client.Close();
                        continue;
                    }
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
                if (!await ReadExactAsync(stream, lenBuf, ct)) return;
                var chunkLen = BinaryPrimitives.ReadUInt32BigEndian(lenBuf);
                if (chunkLen == 0) break;

                var chunk = new byte[chunkLen];
                if (!await ReadExactAsync(stream, chunk, ct)) return;
                await payload.WriteAsync(chunk, ct);
            }

            await stream.WriteAsync(Encoding.UTF8.GetBytes("stream: OK\0"), ct);
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
        var storageDir = Path.Combine(Path.GetTempPath(), "ApiGovPrivateStoreS09", Guid.NewGuid().ToString("N"));
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
                        ["AuthenticationPolicy:LoginRateLimitPermitLimit"] = "250",
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
        WebApplicationFactory<Program> factory)
    {
        var adminEmail = $"s09-admin-{Guid.NewGuid():N}@api-governance.local";
        const string adminPwd = "S09#RevisionsWordPoc!99";

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var bootstrap = scope.ServiceProvider.GetRequiredService<IInitialAdminBootstrapService>();
            var res = await bootstrap.BootstrapFirstAdminAsync(new(adminEmail, "S09 Admin", adminPwd));
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

    private static async Task<(HttpClient Client, string Csrf, Guid UserId)> CreateAndLoginUserWithSinglePermissionAsync(
        WebApplicationFactory<Program> factory,
        CapturingDeliverySpy spy,
        HttpClient adminClient,
        string adminCsrf,
        string email,
        string roleName,
        string[] permissionCodes)
    {
        var (_, roleDoc) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/roles",
            new { name = roleName, description = "Scoped test role" });
        var roleId = roleDoc!.RootElement.GetProperty("id").GetGuid();

        var (permCode, _) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Put,
            $"/api/v1/roles/{roleId}/permissions",
            new { permissionCodes });
        Assert.Equal(HttpStatusCode.OK, permCode);

        const string pwd = "S09#ScopedUser!Pass88";
        var (uCode, userDoc) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post, "/api/v1/users",
            new
            {
                fullName = roleName + " User",
                email,
                roleIds = new[] { roleId },
                triggerActivationToken = true
            });
        Assert.Equal(HttpStatusCode.Created, uCode);
        var userId = userDoc!.RootElement.GetProperty("id").GetGuid();
        var tok = spy.Deliveries.Last(d => d.TargetUserId == userId).SingleUseToken;

        var userClient = CreateCookieClient(factory);
        var csrf = await FetchCsrfAsync(userClient);
        var (resetCode, _) = await SendJsonAsync(userClient, csrf, HttpMethod.Post, "/api/v1/auth/password/reset/complete",
            new { email, token = tok, newPassword = pwd });
        Assert.Equal(HttpStatusCode.NoContent, resetCode);

        var (loginCode, _) = await SendJsonAsync(userClient, csrf, HttpMethod.Post, "/api/v1/auth/login",
            new { email, password = pwd });
        Assert.Equal(HttpStatusCode.OK, loginCode);

        var csrfAfter = await FetchCsrfAsync(userClient);
        return (userClient, csrfAfter, userId);
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

    private static async Task<(Guid IntegrationId, Guid ChangeTypeId, Guid JsonFtId, Guid MdFtId, Guid WordFtId)>
        SeedIntegrationScenarioAsync(HttpClient client, string csrf, Guid devUserId, string suffix)
    {
        var (_, sys1) = await SendJsonAsync(client, csrf, HttpMethod.Post, "/api/v1/company-systems",
            new { name = $"CoreBank {suffix}", ownerEmail = "core@api-governance.local", description = "Provider" });
        var (_, sys2) = await SendJsonAsync(client, csrf, HttpMethod.Post, "/api/v1/company-systems",
            new { name = $"Channel {suffix}", ownerEmail = "ch@api-governance.local", description = "Consumer" });

        var sys1Id = sys1!.RootElement.GetProperty("id").GetGuid();
        var sys2Id = sys2!.RootElement.GetProperty("id").GetGuid();

        await SendJsonAsync(client, csrf, HttpMethod.Post, "/api/v1/internal-providers",
            new { companySystemId = sys1Id, description = $"Internal {suffix}" });

        var (_, grp) = await SendJsonAsync(client, csrf, HttpMethod.Post, "/api/v1/api-groups",
            new { name = $"Payments Group {suffix}", description = "Test" });
        var grpId = grp!.RootElement.GetProperty("id").GetGuid();

        var (_, ftJson) = await SendJsonAsync(client, csrf, HttpMethod.Post, "/api/v1/file-types",
            new { catalogCode = "OPENAPI_SPEC", displayName = "OpenAPI Spec", maxSizeBytes = 204800L });
        var jsonFtId = ftJson!.RootElement.GetProperty("id").GetGuid();

        var (_, ftMd) = await SendJsonAsync(client, csrf, HttpMethod.Post, "/api/v1/file-types",
            new { catalogCode = "MARKDOWN_DOC", displayName = "Markdown Notes", maxSizeBytes = 204800L });
        var mdFtId = ftMd!.RootElement.GetProperty("id").GetGuid();

        var (_, ftWord) = await SendJsonAsync(client, csrf, HttpMethod.Post, "/api/v1/file-types",
            new { catalogCode = "WORD_DOC", displayName = "Word Doc", maxSizeBytes = 409600L });
        var wordFtId = ftWord!.RootElement.GetProperty("id").GetGuid();

        var (_, tpl) = await SendJsonAsync(client, csrf, HttpMethod.Post, "/api/v1/requirement-templates",
            new
            {
                name = $"S09 Template {suffix}",
                rules = new[]
                {
                    new { fileTypeId = jsonFtId, requirementMode = "REQUIRED", minCount = 1, maxCount = 2 },
                    new { fileTypeId = mdFtId, requirementMode = "OPTIONAL", minCount = 0, maxCount = 2 },
                    new { fileTypeId = wordFtId, requirementMode = "OPTIONAL", minCount = 0, maxCount = 2 }
                }
            });
        var tplId = tpl!.RootElement.GetProperty("id").GetGuid();

        var (_, rolesDoc) = await SendJsonAsync(client, csrf, HttpMethod.Get, "/api/v1/roles?pageSize=20");
        var adminRoleId = rolesDoc!.RootElement.GetProperty("items")[0].GetProperty("id").GetGuid();

        var (_, route) = await SendJsonAsync(client, csrf, HttpMethod.Post, "/api/v1/approval-routes",
            new { name = $"S09 Route {suffix}", stages = new[] { new { position = 1, stageName = "Security", responsibleRoleId = adminRoleId } } });
        var routeId = route!.RootElement.GetProperty("id").GetGuid();

        var (_, ct) = await SendJsonAsync(client, csrf, HttpMethod.Post, "/api/v1/change-types",
            new { name = $"Spec Change {suffix}", routeId });
        var ctId = ct!.RootElement.GetProperty("id").GetGuid();

        var (_, pol) = await SendJsonAsync(client, csrf, HttpMethod.Post, "/api/v1/change-policies",
            new { name = $"S09 Policy {suffix}", changeTypeIds = new[] { ctId } });
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
                referenceCode = $"INT-S09-{suffix}",
                developerUserIds = new[] { devUserId }
            });
        var integrationId = integration!.RootElement.GetProperty("id").GetGuid();

        return (integrationId, ctId, jsonFtId, mdFtId, wordFtId);
    }

    private static byte[] CreateSdtDocx(string tag1, string initial1, string tag2, string initial2)
    {
        using var ms = new MemoryStream();
        using (var wordDoc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document, true))
        {
            var main = wordDoc.AddMainDocumentPart();
            main.Document = new Document(
                new Body(
                    new Paragraph(
                        new SdtRun(
                            new SdtProperties(new Tag { Val = tag1 }, new SdtAlias { Val = tag1 }),
                            new SdtContentRun(new Run(new Text(initial1)))),
                        new SdtRun(
                            new SdtProperties(new Tag { Val = tag2 }, new SdtAlias { Val = tag2 }),
                            new SdtContentRun(new Run(new Text(initial2)))))));
            main.Document.Save();
        }
        return ms.ToArray();
    }

    private static byte[] CreateSplitRunPlaceholderDocx()
    {
        using var ms = new MemoryStream();
        using (var wordDoc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document, true))
        {
            var main = wordDoc.AddMainDocumentPart();
            // Intentionally split placeholder across adjacent runs (simulating Word editing fragmentation):
            var bodyPara = new Paragraph(
                new Run(new Text("Revision Package: {{APIGOV_")),
                new Run(new Text("REVISION_")),
                new Run(new Text("NUMBER}} | Title: [[APIGOV_DOCUMENT_TITLE]]")));

            var headerPart = main.AddNewPart<HeaderPart>();
            headerPart.Header = new Header(
                new Paragraph(new Run(new Text("Version Header: {{APIGOV_VERSION_LABEL}}"))));
            headerPart.Header.Save();
            var headerRelId = main.GetIdOfPart(headerPart);

            main.Document = new Document(
                new Body(
                    bodyPara,
                    new SectionProperties(
                        new HeaderReference { Type = HeaderFooterValues.Default, Id = headerRelId })));
            main.Document.Save();
        }
        return ms.ToArray();
    }

    private static byte[] CreateCustomPropertiesDocx()
    {
        using var ms = new MemoryStream();
        using (var wordDoc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document, true))
        {
            var main = wordDoc.AddMainDocumentPart();
            main.Document = new Document(new Body(new Paragraph(new Run(new Text("Governance Cover Document")))));
            main.Document.Save();

            var customPart = wordDoc.AddCustomFilePropertiesPart();
            customPart.Properties = new Properties(
                new CustomDocumentProperty
                {
                    FormatId = "{D5CDD505-2E9C-101B-9397-08002B2CF9AE}",
                    PropertyId = 2,
                    Name = "APIGOV_REVISION_STATUS",
                    VTLPWSTR = new VTLPWSTR("OLD_STATUS")
                },
                new CustomDocumentProperty
                {
                    FormatId = "{D5CDD505-2E9C-101B-9397-08002B2CF9AE}",
                    PropertyId = 3,
                    Name = "APIGOV_INTEGRATION_CODE",
                    VTLPWSTR = new VTLPWSTR("OLD_CODE")
                });
            customPart.Properties.Save();
        }
        return ms.ToArray();
    }

    private static byte[] CreatePlainArbitraryDocx()
    {
        using var ms = new MemoryStream();
        using (var wordDoc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document, true))
        {
            var main = wordDoc.AddMainDocumentPart();
            main.Document = new Document(
                new Body(
                    new Paragraph(new Run(new Text("General architecture memorandum without any recognized governance markers.")))));
            main.Document.Save();
        }
        return ms.ToArray();
    }

    [Fact]
    public async Task V05_Revisions_ShouldValidateMandatoryFiles_AndAllowSubmitOnlyWhenCompleteAndClean()
    {
        await ResetIsolatedDatabaseAsync();
        await using var clamAv = new EmbeddedClamAvTcpServer();
        var (factory, _, dpDir, storageDir) = CreateFactory("127.0.0.1", clamAv.Port);

        try
        {
            var (adminId, client, csrf) = await BootstrapAndLoginAdminAsync(factory);
            var (integrationId, _, jsonFtId, mdFtId, _) = await SeedIntegrationScenarioAsync(client, csrf, adminId, "V05");

            var (_, verDoc) = await SendJsonAsync(client, csrf, HttpMethod.Post,
                $"/api/v1/integrations/{integrationId}/versions",
                new { versionLabel = "2026-Q4-BETA" });
            var versionId = verDoc!.RootElement.GetProperty("id").GetGuid();

            var (revCode, revDoc) = await SendJsonAsync(client, csrf, HttpMethod.Post,
                $"/api/v1/versions/{versionId}/revisions");
            Assert.Equal(HttpStatusCode.Created, revCode);
            var revisionId = revDoc!.RootElement.GetProperty("id").GetGuid();
            Assert.False(revDoc.RootElement.GetProperty("isReadyForSubmission").GetBoolean());
            Assert.Equal(1, revDoc.RootElement.GetProperty("missingRequiredTypesCount").GetInt32());

            // Upload ONLY optional markdown first -> submission still blocked (missing REQUIRED OpenAPI Spec)
            var (mdCode, _) = await UploadMultipartAsync(client, csrf, $"/api/v1/revisions/{revisionId}/files",
                mdFtId, "guide.md", Encoding.UTF8.GetBytes("# Guide\nReady."));
            Assert.Equal(HttpStatusCode.Created, mdCode);

            var (blockedSubmitCode, blockedDoc) = await SendJsonAsync(client, csrf, HttpMethod.Post,
                $"/api/v1/revisions/{revisionId}/submit");
            Assert.Equal(HttpStatusCode.UnprocessableEntity, blockedSubmitCode);
            Assert.Equal(1, blockedDoc!.RootElement.GetProperty("details").GetProperty("missingRequiredTypesCount").GetInt32());
            Assert.Contains("Required file type 'OpenAPI Spec'", blockedDoc.RootElement.GetProperty("details").GetProperty("blockersEn")[0].GetString()!, StringComparison.OrdinalIgnoreCase);

            // Disconnect ClamAV temporarily and upload OpenAPI JSON -> enters PENDING_SCAN -> submission STILL blocked!
            clamAv.FailAllConnections = true;
            var specBytes = Encoding.UTF8.GetBytes("{\"openapi\":\"3.1.0\",\"info\":{\"title\":\"Pay\",\"version\":\"1.0\"}}");
            var (pendCode, pendDoc) = await UploadMultipartAsync(client, csrf, $"/api/v1/revisions/{revisionId}/files",
                jsonFtId, "spec.json", specBytes, "application/json");
            Assert.Equal(HttpStatusCode.Accepted, pendCode);
            var specFileId = pendDoc!.RootElement.GetProperty("id").GetGuid();

            var (pendSubmitCode, _) = await SendJsonAsync(client, csrf, HttpMethod.Post,
                $"/api/v1/revisions/{revisionId}/submit");
            Assert.Equal(HttpStatusCode.UnprocessableEntity, pendSubmitCode);

            // Restore ClamAV & rescan -> CLEAN -> GET /revisions/{id} & GET /versions/{vId}/revisions confirm readiness!
            clamAv.FailAllConnections = false;
            var (rescanCode, _) = await SendJsonAsync(client, csrf, HttpMethod.Post,
                $"/api/v1/revisions/{revisionId}/files/{specFileId}/rescan");
            Assert.Equal(HttpStatusCode.OK, rescanCode);

            var (listCode, listDoc) = await SendJsonAsync(client, csrf, HttpMethod.Get,
                $"/api/v1/versions/{versionId}/revisions");
            Assert.Equal(HttpStatusCode.OK, listCode);
            Assert.Single(listDoc!.RootElement.EnumerateArray());
            Assert.True(listDoc.RootElement[0].GetProperty("isReadyForSubmission").GetBoolean());

            var (detailsCode, detailsDoc) = await SendJsonAsync(client, csrf, HttpMethod.Get,
                $"/api/v1/revisions/{revisionId}");
            Assert.Equal(HttpStatusCode.OK, detailsCode);
            Assert.True(detailsDoc!.RootElement.GetProperty("canSubmit").GetBoolean());
            Assert.Equal(2, detailsDoc.RootElement.GetProperty("files").GetArrayLength());

            // Submit Revision -> 200 OK, status transitions DRAFT -> SUBMITTED & protected
            var (submitCode, submitDoc) = await SendJsonAsync(client, csrf, HttpMethod.Post,
                $"/api/v1/revisions/{revisionId}/submit");
            Assert.Equal(HttpStatusCode.OK, submitCode);
            Assert.Equal("SUBMITTED", submitDoc!.RootElement.GetProperty("revision").GetProperty("status").GetString());
            Assert.True(submitDoc.RootElement.GetProperty("revision").GetProperty("isProtected").GetBoolean());
            Assert.Equal(2, submitDoc.RootElement.GetProperty("cleanFilesCount").GetInt32());
        }
        finally
        {
            await factory.DisposeAsync();
            if (Directory.Exists(dpDir)) Directory.Delete(dpDir, true);
            if (Directory.Exists(storageDir)) Directory.Delete(storageDir, true);
        }
    }

    [Fact]
    public async Task V06_Revisions_ShouldProtectApprovedRevisions_EnforceWorkingCopyChangeScope_AndConcurrencyOP05()
    {
        await ResetIsolatedDatabaseAsync();
        await using var clamAv = new EmbeddedClamAvTcpServer();
        var (factory, _, dpDir, storageDir) = CreateFactory("127.0.0.1", clamAv.Port);

        try
        {
            var (adminId, client, csrf) = await BootstrapAndLoginAdminAsync(factory);
            var (integrationId, changeTypeId, jsonFtId, mdFtId, _) =
                await SeedIntegrationScenarioAsync(client, csrf, adminId, "V06");

            var (_, verDoc) = await SendJsonAsync(client, csrf, HttpMethod.Post,
                $"/api/v1/integrations/{integrationId}/versions",
                new { versionLabel = "v1-prod" });
            var versionId = verDoc!.RootElement.GetProperty("id").GetGuid();

            var (_, rev1Doc) = await SendJsonAsync(client, csrf, HttpMethod.Post,
                $"/api/v1/versions/{versionId}/revisions");
            var rev1Id = rev1Doc!.RootElement.GetProperty("id").GetGuid();

            // Upload 1 clean OpenAPI JSON + 1 clean Markdown doc
            var (_, fJsonDoc) = await UploadMultipartAsync(client, csrf, $"/api/v1/revisions/{rev1Id}/files",
                jsonFtId, "openapi-v1.json", Encoding.UTF8.GetBytes("{\"openapi\":\"3.1.0\"}"), "application/json");
            var fJsonId = fJsonDoc!.RootElement.GetProperty("id").GetGuid();

            var (_, fMdDoc) = await UploadMultipartAsync(client, csrf, $"/api/v1/revisions/{rev1Id}/files",
                mdFtId, "overview-v1.md", Encoding.UTF8.GetBytes("# Overview v1"));
            var fMdId = fMdDoc!.RootElement.GetProperty("id").GetGuid();

            var (subCode, _) = await SendJsonAsync(client, csrf, HttpMethod.Post, $"/api/v1/revisions/{rev1Id}/submit");
            Assert.Equal(HttpStatusCode.OK, subCode);

            // Protected SUBMITTED revision rejects upload and delete (422)
            var (protUpCode, _) = await UploadMultipartAsync(client, csrf, $"/api/v1/revisions/{rev1Id}/files",
                mdFtId, "extra.md", Encoding.UTF8.GetBytes("# Nope"));
            Assert.Equal(HttpStatusCode.UnprocessableEntity, protUpCode);

            var (protDelCode, _) = await SendJsonAsync(client, csrf, HttpMethod.Delete,
                $"/api/v1/revisions/{rev1Id}/files/{fMdId}");
            Assert.Equal(HttpStatusCode.UnprocessableEntity, protDelCode);

            // Re-submit on protected SUBMITTED revision -> 422 immutability violation
            var (dupSubCode, _) = await SendJsonAsync(client, csrf, HttpMethod.Post, $"/api/v1/revisions/{rev1Id}/submit");
            Assert.Equal(HttpStatusCode.UnprocessableEntity, dupSubCode);

            // Simulate Stage/Workflow approval of Revision #1 + insert APPROVED Change Request targeting fJsonId only
            Guid crPendingId;
            Guid crApprovedId;
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiGovernanceDbContext>();
                var r1 = await db.DocumentRevisions.FirstAsync(r => r.Id == rev1Id);
                r1.Status = "APPROVED";
                r1.ApprovedAt = DateTimeOffset.UtcNow;

                crPendingId = Guid.NewGuid();
                db.ChangeRequests.Add(new ChangeRequestEntity
                {
                    Id = crPendingId,
                    IntegrationId = integrationId,
                    SourceRevisionId = rev1Id,
                    ChangeTypeId = changeTypeId,
                    Reason = "Unapproved draft CR",
                    Status = "SUBMITTED",
                    RequestedBy = adminId,
                    CreatedAt = DateTimeOffset.UtcNow
                });

                crApprovedId = Guid.NewGuid();
                db.ChangeRequests.Add(new ChangeRequestEntity
                {
                    Id = crApprovedId,
                    IntegrationId = integrationId,
                    SourceRevisionId = rev1Id,
                    ChangeTypeId = changeTypeId,
                    Reason = "Update OpenAPI endpoint schema only",
                    Status = "APPROVED",
                    RequestedBy = adminId,
                    CreatedAt = DateTimeOffset.UtcNow
                });
                db.ChangeRequestFiles.Add(new ChangeRequestFileEntity
                {
                    Id = Guid.NewGuid(),
                    ChangeRequestId = crApprovedId,
                    SourceRevisionFileId = fJsonId
                });
                await db.SaveChangesAsync();
            }

            // Protected APPROVED revision rejects plain initial draft creation without change request -> 422
            var (plainAfterAppCode, _) = await SendJsonAsync(client, csrf, HttpMethod.Post,
                $"/api/v1/versions/{versionId}/revisions");
            Assert.Equal(HttpStatusCode.UnprocessableEntity, plainAfterAppCode);

            // Unapproved change request cannot open working copy -> 422
            var (unapprovedCrCode, _) = await SendJsonAsync(client, csrf, HttpMethod.Post,
                $"/api/v1/versions/{versionId}/revisions",
                new { baseRevisionId = rev1Id, changeRequestId = crPendingId });
            Assert.Equal(HttpStatusCode.UnprocessableEntity, unapprovedCrCode);

            // Open working copy concurrently (OP-05): fire 2 parallel POST /revisions requests with identical valid payload!
            var t1 = SendJsonAsync(client, csrf, HttpMethod.Post, $"/api/v1/versions/{versionId}/revisions",
                new { baseRevisionId = rev1Id, changeRequestId = crApprovedId });
            var t2 = SendJsonAsync(client, csrf, HttpMethod.Post, $"/api/v1/versions/{versionId}/revisions",
                new { baseRevisionId = rev1Id, changeRequestId = crApprovedId });
            var results = await Task.WhenAll(t1, t2);

            Assert.Single(results, r => r.Status == HttpStatusCode.Created);
            Assert.Single(results, r => r.Status == HttpStatusCode.Conflict);

            var rev2Doc = results.First(r => r.Status == HttpStatusCode.Created).Doc!;
            var rev2Id = rev2Doc.RootElement.GetProperty("id").GetGuid();
            Assert.Equal(2, rev2Doc.RootElement.GetProperty("revisionNumber").GetInt32());
            Assert.Equal(rev1Id, rev2Doc.RootElement.GetProperty("baseRevisionId").GetGuid());
            Assert.Equal(crApprovedId, rev2Doc.RootElement.GetProperty("changeRequestId").GetGuid());

            // Verify baseline files carried forward into Working Copy #2 with SourceRevisionFileId lineage preserved
            var (_, rev2DetailsDoc) = await SendJsonAsync(client, csrf, HttpMethod.Get, $"/api/v1/revisions/{rev2Id}");
            var copiedFiles = rev2DetailsDoc!.RootElement.GetProperty("files").EnumerateArray().ToList();
            Assert.Equal(2, copiedFiles.Count);
            Assert.Contains(copiedFiles, f => f.GetProperty("sourceRevisionFileId").GetGuid() == fMdId);
            Assert.Contains(copiedFiles, f => f.GetProperty("sourceRevisionFileId").GetGuid() == fJsonId);
        }
        finally
        {
            await factory.DisposeAsync();
            if (Directory.Exists(dpDir)) Directory.Delete(dpDir, true);
            if (Directory.Exists(storageDir)) Directory.Delete(storageDir, true);
        }
    }

    [Fact]
    public async Task V07_VerifiedDownloads_ShouldEnforceAuthz_DetectSha256Tamper_AndWriteAuditEvents()
    {
        await ResetIsolatedDatabaseAsync();
        await using var clamAv = new EmbeddedClamAvTcpServer();
        var (factory, spy, dpDir, storageDir) = CreateFactory("127.0.0.1", clamAv.Port);

        try
        {
            var (adminId, adminClient, adminCsrf) = await BootstrapAndLoginAdminAsync(factory);
            var (integrationId, _, jsonFtId, _, _) =
                await SeedIntegrationScenarioAsync(adminClient, adminCsrf, adminId, "V07");

            var (_, verDoc) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post,
                $"/api/v1/integrations/{integrationId}/versions", new { versionLabel = "v1" });
            var versionId = verDoc!.RootElement.GetProperty("id").GetGuid();

            var (_, revDoc) = await SendJsonAsync(adminClient, adminCsrf, HttpMethod.Post,
                $"/api/v1/versions/{versionId}/revisions");
            var revisionId = revDoc!.RootElement.GetProperty("id").GetGuid();

            var payloadBytes = Encoding.UTF8.GetBytes("{\"openapi\":\"3.1.0\",\"info\":{\"title\":\"AuditDL\",\"version\":\"1.0\"}}");
            var expectedSha = Convert.ToHexStringLower(SHA256.HashData(payloadBytes));

            var (upCode, upDoc) = await UploadMultipartAsync(adminClient, adminCsrf, $"/api/v1/revisions/{revisionId}/files",
                jsonFtId, "clean-spec.json", payloadBytes, "application/json");
            Assert.Equal(HttpStatusCode.Created, upCode);
            var fileId = upDoc!.RootElement.GetProperty("id").GetGuid();
            var objectId = upDoc.RootElement.GetProperty("objectId").GetGuid();

            // 1. Authorized download returns 200 OK with security headers & SHA-256 verification
            using var dlRes = await adminClient.GetAsync($"/api/v1/revisions/{revisionId}/files/{fileId}/download");
            Assert.Equal(HttpStatusCode.OK, dlRes.StatusCode);
            Assert.Equal("nosniff", dlRes.Headers.GetValues("X-Content-Type-Options").Single());
            Assert.Equal("sandbox", dlRes.Headers.GetValues("Content-Security-Policy").Single());
            Assert.Equal(expectedSha, dlRes.Headers.GetValues("X-File-Sha256").Single());
            var downloadedBytes = await dlRes.Content.ReadAsByteArrayAsync();
            Assert.Equal(payloadBytes, downloadedBytes);

            // 2. Unprivileged user (no files.download permission) -> 403 Forbidden
            var (viewerClient, _, _) = await CreateAndLoginUserWithSinglePermissionAsync(
                factory, spy, adminClient, adminCsrf, "no-dl@api-governance.local", "NoDownloadRole",
                ["integrations.view", "files.view"]);
            using var deniedRes = await viewerClient.GetAsync($"/api/v1/revisions/{revisionId}/files/{fileId}/download");
            Assert.Equal(HttpStatusCode.Forbidden, deniedRes.StatusCode);

            // 3. Simulate external tampering of private storage file -> server detects SHA-256 mismatch -> 422 + audit log!
            string storageKey;
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiGovernanceDbContext>();
                storageKey = await db.FileObjects
                    .Where(f => f.Id == objectId)
                    .Select(f => f.StorageKey)
                    .FirstAsync();
            }

            var diskPath = Path.Combine(storageDir, storageKey.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(diskPath));
            await File.WriteAllBytesAsync(diskPath, Encoding.UTF8.GetBytes("{\"tampered\":true}"));

            using var tamperRes = await adminClient.GetAsync($"/api/v1/revisions/{revisionId}/files/{fileId}/download");
            Assert.Equal(HttpStatusCode.UnprocessableEntity, tamperRes.StatusCode);

            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiGovernanceDbContext>();
                var actions = await db.AuditEvents.Select(a => a.Operation).ToListAsync();
                Assert.Contains("FILE_DOWNLOADED", actions);
                Assert.Contains("FILE_DOWNLOAD_INTEGRITY_FAILED", actions);
            }
        }
        finally
        {
            await factory.DisposeAsync();
            if (Directory.Exists(dpDir)) Directory.Delete(dpDir, true);
            if (Directory.Exists(storageDir)) Directory.Delete(storageDir, true);
        }
    }

    [Fact]
    public async Task V08_OpenXmlWordDerivativePoc_ShouldHandleSdtSplitRunsCustomProps_PreserveOriginal_AndBlockUnverified()
    {
        await ResetIsolatedDatabaseAsync();
        await using var clamAv = new EmbeddedClamAvTcpServer();
        var (factory, _, dpDir, storageDir) = CreateFactory("127.0.0.1", clamAv.Port);

        try
        {
            var (adminId, client, csrf) = await BootstrapAndLoginAdminAsync(factory);
            var (integrationId, _, _, _, wordFtId) =
                await SeedIntegrationScenarioAsync(client, csrf, adminId, "V08");

            var (_, verDoc) = await SendJsonAsync(client, csrf, HttpMethod.Post,
                $"/api/v1/integrations/{integrationId}/versions", new { versionLabel = "2026-WORD-POC" });
            var versionId = verDoc!.RootElement.GetProperty("id").GetGuid();

            // Revision #1 covers SDT + Split-Run Placeholders (maxCount=2 on WORD_DOC)
            var (_, rev1Doc) = await SendJsonAsync(client, csrf, HttpMethod.Post,
                $"/api/v1/versions/{versionId}/revisions");
            var rev1Id = rev1Doc!.RootElement.GetProperty("id").GetGuid();

            // Pattern 1: SDT Content Controls
            var sdtBytes = CreateSdtDocx("APIGOV_INTEGRATION_CODE", "OLD-INT", "APIGOV_VERSION_LABEL", "OLD-VER");
            var sdtOrigSha = Convert.ToHexStringLower(SHA256.HashData(sdtBytes));
            var (sdtUpCode, sdtUpDoc) = await UploadMultipartAsync(client, csrf, $"/api/v1/revisions/{rev1Id}/files",
                wordFtId, "pattern1-sdt.docx", sdtBytes);
            Assert.Equal(HttpStatusCode.Created, sdtUpCode);
            var sdtFileId = sdtUpDoc!.RootElement.GetProperty("id").GetGuid();

            var (sdtGenCode, sdtGenDoc) = await SendJsonAsync(client, csrf, HttpMethod.Post,
                $"/api/v1/revisions/{rev1Id}/files/{sdtFileId}/word-derivative");
            Assert.Equal(HttpStatusCode.OK, sdtGenCode);
            Assert.Equal("VERIFIED", sdtGenDoc!.RootElement.GetProperty("status").GetString());
            Assert.True(sdtGenDoc.RootElement.GetProperty("isPublishable").GetBoolean());
            Assert.Contains("SDT_CONTROLS", sdtGenDoc.RootElement.GetProperty("templateKindDetected").GetString()!);
            Assert.Equal(2, sdtGenDoc.RootElement.GetProperty("fieldsUpdatedCount").GetInt32());
            Assert.Equal(sdtOrigSha, sdtGenDoc.RootElement.GetProperty("sourceSha256").GetString());
            Assert.NotEqual(sdtOrigSha, sdtGenDoc.RootElement.GetProperty("derivedSha256").GetString());

            // Original download MUST remain untouched!
            using (var origDl = await client.GetAsync($"/api/v1/revisions/{rev1Id}/files/{sdtFileId}/download"))
            {
                Assert.Equal(HttpStatusCode.OK, origDl.StatusCode);
                var origDlBytes = await origDl.Content.ReadAsByteArrayAsync();
                Assert.Equal(sdtBytes, origDlBytes);
            }

            // Verified derivative download MUST succeed and validate with 0 OpenXML schema errors!
            using (var derivDl = await client.GetAsync($"/api/v1/revisions/{rev1Id}/files/{sdtFileId}/word-derivative/download"))
            {
                Assert.Equal(HttpStatusCode.OK, derivDl.StatusCode);
                var derivedBytes = await derivDl.Content.ReadAsByteArrayAsync();
                Assert.Equal(
                    sdtGenDoc.RootElement.GetProperty("derivedSha256").GetString(),
                    Convert.ToHexStringLower(SHA256.HashData(derivedBytes)));

                using var checkMs = new MemoryStream(derivedBytes);
                using var opened = WordprocessingDocument.Open(checkMs, false);
                var validator = new OpenXmlValidator(FileFormatVersions.Microsoft365);
                Assert.Empty(validator.Validate(opened));
                var text = opened.MainDocumentPart!.Document!.InnerText;
                Assert.Contains("INT-S09-V08", text);
                Assert.Contains("2026-WORD-POC", text);
            }

            // Pattern 2: Split-Run Placeholder Tokens across Body & Header
            var splitBytes = CreateSplitRunPlaceholderDocx();
            var (_, splitUpDoc) = await UploadMultipartAsync(client, csrf, $"/api/v1/revisions/{rev1Id}/files",
                wordFtId, "pattern2-splitrun.docx", splitBytes);
            var splitFileId = splitUpDoc!.RootElement.GetProperty("id").GetGuid();

            var (splitGenCode, splitGenDoc) = await SendJsonAsync(client, csrf, HttpMethod.Post,
                $"/api/v1/revisions/{rev1Id}/files/{splitFileId}/word-derivative");
            Assert.Equal(HttpStatusCode.OK, splitGenCode);
            Assert.Equal("VERIFIED", splitGenDoc!.RootElement.GetProperty("status").GetString());
            Assert.Equal(3, splitGenDoc.RootElement.GetProperty("fieldsUpdatedCount").GetInt32());

            using (var splitDl = await client.GetAsync($"/api/v1/revisions/{rev1Id}/files/{splitFileId}/word-derivative/download"))
            {
                Assert.Equal(HttpStatusCode.OK, splitDl.StatusCode);
                using var ms = new MemoryStream(await splitDl.Content.ReadAsByteArrayAsync());
                using var opened = WordprocessingDocument.Open(ms, false);
                Assert.Contains("#1", opened.MainDocumentPart!.Document!.InnerText);
                Assert.Contains("2026-WORD-POC", opened.MainDocumentPart.HeaderParts.Single().Header!.InnerText);
            }

            // Pattern 3 & Pattern 4 on a second API version so WORD_DOC maxCount=2 is respected cleanly
            var (_, ver2Doc) = await SendJsonAsync(client, csrf, HttpMethod.Post,
                $"/api/v1/integrations/{integrationId}/versions", new { versionLabel = "2026-WORD-POC-2" });
            var ver2Id = ver2Doc!.RootElement.GetProperty("id").GetGuid();
            var (_, rev2Doc) = await SendJsonAsync(client, csrf, HttpMethod.Post,
                $"/api/v1/versions/{ver2Id}/revisions");
            var rev2Id = rev2Doc!.RootElement.GetProperty("id").GetGuid();

            // Pattern 3: Custom Properties
            var customBytes = CreateCustomPropertiesDocx();
            var (_, customUpDoc) = await UploadMultipartAsync(client, csrf, $"/api/v1/revisions/{rev2Id}/files",
                wordFtId, "pattern3-customprops.docx", customBytes);
            var customFileId = customUpDoc!.RootElement.GetProperty("id").GetGuid();

            var (customGenCode, customGenDoc) = await SendJsonAsync(client, csrf, HttpMethod.Post,
                $"/api/v1/revisions/{rev2Id}/files/{customFileId}/word-derivative");
            Assert.Equal(HttpStatusCode.OK, customGenCode);
            Assert.Equal("VERIFIED", customGenDoc!.RootElement.GetProperty("status").GetString());
            Assert.Contains("CUSTOM_PROPERTIES", customGenDoc.RootElement.GetProperty("templateKindDetected").GetString()!);

            // Pattern 4: Arbitrary unannotated Word document -> NO_RECOGNIZED_FIELDS & download blocked (OP-03 = 422)
            var plainBytes = CreatePlainArbitraryDocx();
            var (_, plainUpDoc) = await UploadMultipartAsync(client, csrf, $"/api/v1/revisions/{rev2Id}/files",
                wordFtId, "pattern4-plain.docx", plainBytes);
            var plainFileId = plainUpDoc!.RootElement.GetProperty("id").GetGuid();

            var (plainGenCode, plainGenDoc) = await SendJsonAsync(client, csrf, HttpMethod.Post,
                $"/api/v1/revisions/{rev2Id}/files/{plainFileId}/word-derivative");
            Assert.Equal(HttpStatusCode.OK, plainGenCode);
            Assert.Equal("NO_RECOGNIZED_FIELDS", plainGenDoc!.RootElement.GetProperty("status").GetString());
            Assert.False(plainGenDoc.RootElement.GetProperty("isPublishable").GetBoolean());

            using (var blockedDl = await client.GetAsync($"/api/v1/revisions/{rev2Id}/files/{plainFileId}/word-derivative/download"))
            {
                Assert.Equal(HttpStatusCode.UnprocessableEntity, blockedDl.StatusCode);
            }

            // Pattern 5: Scanner offline when generating derivative on Pattern 3 file -> PENDING_SCAN (OP-03 blocks download)
            clamAv.FailAllConnections = true;
            var (pendGenCode, pendGenDoc) = await SendJsonAsync(client, csrf, HttpMethod.Post,
                $"/api/v1/revisions/{rev2Id}/files/{customFileId}/word-derivative");
            Assert.Equal(HttpStatusCode.OK, pendGenCode);
            Assert.Equal("PENDING_SCAN", pendGenDoc!.RootElement.GetProperty("status").GetString());
            Assert.False(pendGenDoc.RootElement.GetProperty("isPublishable").GetBoolean());

            using (var blockedPendDl = await client.GetAsync($"/api/v1/revisions/{rev2Id}/files/{customFileId}/word-derivative/download"))
            {
                Assert.Equal(HttpStatusCode.UnprocessableEntity, blockedPendDl.StatusCode);
            }
        }
        finally
        {
            await factory.DisposeAsync();
            if (Directory.Exists(dpDir)) Directory.Delete(dpDir, true);
            if (Directory.Exists(storageDir)) Directory.Delete(storageDir, true);
        }
    }
}
