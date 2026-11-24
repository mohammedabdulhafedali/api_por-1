using System.Buffers.Binary;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using ApiGovernance.Modules.VersionsFiles.Configuration;
using ApiGovernance.Modules.VersionsFiles.Contracts;
using Microsoft.Extensions.Options;

namespace ApiGovernance.Modules.VersionsFiles.Scanning;

/// <summary>
/// Task V03: Fail-closed malware scanning contract.
/// When a real AV engine is not configured or unreachable, returns PENDING_SCAN and keeps the
/// file quarantined rather than ever simulating CLEAN success.
/// </summary>
public interface IFileSecurityScanner
{
    ScannerRuntimeInfo GetRuntimeInfo();

    Task<FileScanVerdict> ScanStagedFileAsync(
        string filePath,
        string originalFileName,
        CancellationToken cancellationToken = default);
}

public sealed class ConfiguredFileSecurityScanner(
    IOptions<FileScanningOptions> options) : IFileSecurityScanner
{
    private readonly FileScanningOptions _options = options.Value;

    public ScannerRuntimeInfo GetRuntimeInfo()
    {
        var provider = (_options.Provider ?? "ClamAv").Trim();

        if (string.Equals(provider, "ClamAv", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(_options.ClamAvHost))
            {
                return new ScannerRuntimeInfo(
                    IsConfigured: false,
                    Provider: "ClamAv",
                    SummaryAr: "محرك الفحص الأمني (ClamAV clamd) غير مهيأ بعد من قسم IT — تدخل الملفات في الحجر الآمن بحالة PENDING_SCAN ولا تُتاح قبل الفحص الفعلي.",
                    SummaryEn: "Security scan engine (ClamAV clamd) is not configured by IT yet — uploaded files remain quarantined as PENDING_SCAN and unavailable until scanned.");
            }

            return new ScannerRuntimeInfo(
                IsConfigured: true,
                Provider: "ClamAv",
                SummaryAr: $"متصل بمحرك الفحص الأمني ClamAV ({_options.ClamAvHost}:{_options.ClamAvPort}).",
                SummaryEn: $"Connected to ClamAV security engine ({_options.ClamAvHost}:{_options.ClamAvPort}).");
        }

        if (string.Equals(provider, "CommandLine", StringComparison.OrdinalIgnoreCase))
        {
            var configured = !string.IsNullOrWhiteSpace(_options.CommandPath) && File.Exists(_options.CommandPath);
            return new ScannerRuntimeInfo(
                IsConfigured: configured,
                Provider: "CommandLine",
                SummaryAr: configured
                    ? $"مهيأ عبر ماسح الأسطر القياسي المؤسسي ({Path.GetFileName(_options.CommandPath)})."
                    : "مسار أداة الفحص الأمني (CommandLine) غير مهيأ أو غير موجود — تطبق سياسة Fail-Closed بحالة PENDING_SCAN.",
                SummaryEn: configured
                    ? $"Configured with enterprise CLI malware scanner ({Path.GetFileName(_options.CommandPath)})."
                    : "Enterprise CLI malware scanner executable is not configured or missing — fail-closed PENDING_SCAN enforced.");
        }

        return new ScannerRuntimeInfo(
            IsConfigured: false,
            Provider: "FailClosed",
            SummaryAr: "سياسة الفحص المشددة (Fail-Closed) مفعلة لعدم توفر محرك فحص نشط.",
            SummaryEn: "Fail-closed quarantine policy active (no active malware scanner configured).");
    }

    public async Task<FileScanVerdict> ScanStagedFileAsync(
        string filePath,
        string originalFileName,
        CancellationToken cancellationToken = default)
    {
        var provider = (_options.Provider ?? "ClamAv").Trim();

        if (string.Equals(provider, "ClamAv", StringComparison.OrdinalIgnoreCase))
        {
            return await ScanViaClamAvAsync(filePath, cancellationToken);
        }

        if (string.Equals(provider, "CommandLine", StringComparison.OrdinalIgnoreCase))
        {
            return await ScanViaCommandLineAsync(filePath, cancellationToken);
        }

        return FailClosedPending(
            "SCANNER_NOT_CONFIGURED",
            "لم يتم تهيئة محرك فحص أمني فعلي؛ تم حجز الملف في الحجر الأمني (PENDING_SCAN) ومنع إتاحته.",
            "No real malware scanner engine is configured; file is held in quarantine (PENDING_SCAN) and unavailable.");
    }

    private async Task<FileScanVerdict> ScanViaClamAvAsync(string filePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ClamAvHost) || _options.ClamAvPort <= 0)
        {
            return FailClosedPending(
                "SCANNER_NOT_CONFIGURED",
                "خادم ClamAV غير مضبوط بعد في الإعدادات؛ تم عزل الملف بحالة PENDING_SCAN لحين توفر الفاحص الفعلي من قسم IT.",
                "ClamAV host is not configured; file quarantined as PENDING_SCAN until IT provides an active scan engine.");
        }

        var timeout = TimeSpan.FromSeconds(Math.Clamp(_options.TimeoutSeconds, 5, 120));
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        try
        {
            using var tcpClient = new TcpClient();
            await tcpClient.ConnectAsync(_options.ClamAvHost.Trim(), _options.ClamAvPort, cts.Token);

            await using var netStream = tcpClient.GetStream();

            // ClamAV INSTREAM protocol: "zINSTREAM\0" + 4-byte big-endian chunk lengths + data + 4-byte 0 terminator
            var cmd = Encoding.ASCII.GetBytes("zINSTREAM\0");
            await netStream.WriteAsync(cmd, cts.Token);

            var chunkBuffer = new byte[32 * 1024];
            var lenPrefix = new byte[4];

            await using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 32 * 1024, useAsync: true))
            {
                while (true)
                {
                    var read = await fs.ReadAsync(chunkBuffer, cts.Token);
                    if (read == 0)
                    {
                        break;
                    }

                    BinaryPrimitives.WriteUInt32BigEndian(lenPrefix, (uint)read);
                    await netStream.WriteAsync(lenPrefix, cts.Token);
                    await netStream.WriteAsync(chunkBuffer.AsMemory(0, read), cts.Token);
                }
            }

            Array.Clear(lenPrefix);
            await netStream.WriteAsync(lenPrefix, cts.Token);
            await netStream.FlushAsync(cts.Token);

            var respBytes = new byte[2048];
            var totalResp = 0;
            while (totalResp < respBytes.Length)
            {
                var n = await netStream.ReadAsync(respBytes.AsMemory(totalResp), cts.Token);
                if (n == 0) break;
                totalResp += n;
                if (Array.IndexOf(respBytes, (byte)0, 0, totalResp) >= 0) break;
            }

            var response = Encoding.UTF8.GetString(respBytes, 0, totalResp).Trim('\0', '\r', '\n', ' ');
            if (response.EndsWith("OK", StringComparison.OrdinalIgnoreCase))
            {
                return new FileScanVerdict(
                    FileScanStatusValues.Clean,
                    "CLAMAV_CLEAN",
                    "اجتاز الملف الفحص الأمني بنجاح.",
                    "File passed ClamAV security scan cleanly.");
            }

            var foundIdx = response.LastIndexOf("FOUND", StringComparison.OrdinalIgnoreCase);
            if (foundIdx >= 0)
            {
                var threat = response.Replace("stream:", string.Empty, StringComparison.OrdinalIgnoreCase)
                    .Replace("FOUND", string.Empty, StringComparison.OrdinalIgnoreCase)
                    .Trim();

                return new FileScanVerdict(
                    FileScanStatusValues.Infected,
                    "CLAMAV_INFECTED",
                    $"تم اكتشاف تهديد أمني ({threat}) وعزل الملف في الحجر ومنع إتاحته.",
                    $"Security threat detected ({threat}); file quarantined and rejected.",
                    ThreatName: threat);
            }

            return FailClosedPending(
                "SCANNER_UNEXPECTED_RESPONSE",
                "استجاب محرك الفحص بنتيجة غير حاسمة؛ تم إبقاء الملف محجورًا (PENDING_SCAN).",
                $"Scanner returned an inconclusive response '{response}'; held as PENDING_SCAN.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return FailClosedPending(
                "SCANNER_TIMEOUT",
                "انتهت مهلة الاتصال بمحرك الفحص الأمني؛ تم إبقاء الملف في الحجر (PENDING_SCAN).",
                "Malware scanner timed out; file kept in quarantine as PENDING_SCAN.");
        }
        catch (Exception)
        {
            return FailClosedPending(
                "SCANNER_UNREACHABLE",
                "تعذر الاتصال بمحرك الفحص الأمني؛ أُبقي الملف في الحجر الأمني (PENDING_SCAN) دون السماح بإتاحته.",
                "Malware scanner engine is unreachable; file quarantined fail-closed as PENDING_SCAN.");
        }
    }

    private async Task<FileScanVerdict> ScanViaCommandLineAsync(string filePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.CommandPath) || !File.Exists(_options.CommandPath))
        {
            return FailClosedPending(
                "SCANNER_NOT_CONFIGURED",
                "أداة الفحص الأمني المحلية غير موجودة أو غير مضبوطة؛ أُبقي الملف بحالة PENDING_SCAN.",
                "CLI scanner binary is not configured or missing; file quarantined fail-closed as PENDING_SCAN.");
        }

        var timeout = TimeSpan.FromSeconds(Math.Clamp(_options.TimeoutSeconds, 5, 120));
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = _options.CommandPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            // Split configured arguments safely without shell interpolation.
            var rawArgs = (_options.CommandArguments ?? "\"{filePath}\"").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var rawArg in rawArgs)
            {
                var arg = rawArg.Replace("{filePath}", filePath, StringComparison.Ordinal);
                if (arg.StartsWith('"') && arg.EndsWith('"') && arg.Length >= 2)
                {
                    arg = arg[1..^1];
                }

                startInfo.ArgumentList.Add(arg);
            }

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return FailClosedPending(
                    "SCANNER_START_FAILED",
                    "تعذر تشغيل أداة الفحص الأمني؛ أُبقي الملف بحالة PENDING_SCAN.",
                    "Failed to start malware scanner process; held as PENDING_SCAN.");
            }

            await process.WaitForExitAsync(cts.Token);
            if (process.ExitCode == 0)
            {
                return new FileScanVerdict(
                    FileScanStatusValues.Clean,
                    "CLI_SCAN_CLEAN",
                    "اجتاز الملف الفحص الأمني بنجاح.",
                    "File passed CLI security scan cleanly.");
            }

            if (process.ExitCode == 2)
            {
                return new FileScanVerdict(
                    FileScanStatusValues.Infected,
                    "CLI_SCAN_INFECTED",
                    "اكتشف الفاحص الأمني ملفًا مشبوهًا/ضارًا وتم عزله ومنعه.",
                    "CLI security scanner detected malware; file quarantined and rejected.",
                    ThreatName: "CLI_DETECTED_THREAT");
            }

            return FailClosedPending(
                "SCANNER_CLI_ERROR",
                $"أرجع الفاحص الأمني رمز خطأ ({process.ExitCode})؛ أُبقي الملف بحالة PENDING_SCAN.",
                $"CLI scanner exited with unexpected status {process.ExitCode}; held as PENDING_SCAN.");
        }
        catch
        {
            return FailClosedPending(
                "SCANNER_EXECUTION_FAILED",
                "تعذر إتمام الفحص الأمني؛ أُبقي الملف بحالة PENDING_SCAN.",
                "Failed to execute malware scanner; file held fail-closed as PENDING_SCAN.");
        }
    }

    private static FileScanVerdict FailClosedPending(string code, string ar, string en)
        => new(FileScanStatusValues.PendingScan, code, ar, en);
}

