using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace ApiGovernance.Modules.VersionsFiles.Validation;

public sealed record FileContentValidationResult(
    bool IsValid,
    string? NormalizedFileName,
    string? Extension,
    string? CanonicalMediaType,
    string? ErrorCode,
    string? MessageAr,
    string? MessageEn);

/// <summary>
/// Task V02: Deep server-side content, extension, magic-byte, and internal archive verification.
/// Never trusts browser-supplied Content-Type headers; blocks executable payloads, composite
/// double extensions, active web scripts (HTML/SVG), Office VBA macros, and zip bombs/traversal.
/// </summary>
public sealed class FileUploadContentValidator
{
    private const int MaxZipEntries = 500;
    private const long MaxZipUncompressedBytes = 200L * 1024 * 1024;
    private const double MaxZipCompressionRatio = 100.0;

    private static readonly HashSet<string> DangerousExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".sys", ".bat", ".cmd", ".ps1", ".psm1", ".vbs", ".vbe", ".js", ".jse",
        ".wsf", ".wsh", ".msi", ".msp", ".scr", ".pif", ".com", ".cpl", ".hta", ".jar", ".sh",
        ".bash", ".zsh", ".elf", ".so", ".dylib", ".html", ".htm", ".xhtml", ".svg", ".asp",
        ".aspx", ".php", ".jsp", ".docm", ".xlsm", ".pptm", ".dotm", ".xltm", ".lnk", ".url", ".iso"
    };

    public FileContentValidationResult ValidateFilenameAndExtension(
        string? rawFileName,
        IReadOnlyCollection<string> allowedExtensionsForFileType)
    {
        if (string.IsNullOrWhiteSpace(rawFileName))
        {
            return Invalid(
                "FILE_NAME_MISSING",
                "اسم الملف المرفوع مطلوب.",
                "Uploaded file name is required.");
        }

        if (rawFileName.IndexOfAny(['/', '\\', '\0']) >= 0 || rawFileName.Contains("..", StringComparison.Ordinal))
        {
            return Invalid(
                "FILE_NAME_UNSAFE",
                "اسم الملف يحتوي على رموز مسار غير آمنة.",
                "File name contains unsafe path traversal characters.");
        }

        var safeName = Path.GetFileName(rawFileName.Trim());
        if (string.IsNullOrWhiteSpace(safeName) || safeName.Length > 255)
        {
            return Invalid(
                "FILE_NAME_LENGTH",
                "اسم الملف غير صالح أو يتجاوز الحد المسموح (255 حرفًا).",
                "File name is invalid or exceeds 255 characters.");
        }

        // Inspect every dot-separated segment after base name to block compound double-extensions (e.g. spec.exe.pdf).
        var segments = safeName.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2)
        {
            return Invalid(
                "FILE_EXTENSION_MISSING",
                "يجب أن يحتوي الملف على امتداد صريح ومعتمد.",
                "Uploaded file must include an explicit approved file extension.");
        }

        for (var i = 1; i < segments.Length; i++)
        {
            var segExt = "." + segments[i].Trim().ToLowerInvariant();
            if (DangerousExtensions.Contains(segExt))
            {
                return Invalid(
                    "FILE_DANGEROUS_EXTENSION",
                    $"مرفوض: تم اكتشاف امتداد خطير أو تنفيذي ({segExt}) داخل اسم الملف.",
                    $"Rejected: dangerous or executable extension segment '{segExt}' detected.");
            }
        }

        var trailingExt = Path.GetExtension(safeName).ToLowerInvariant();
        if (!allowedExtensionsForFileType.Contains(trailingExt, StringComparer.OrdinalIgnoreCase))
        {
            return Invalid(
                "FILE_EXTENSION_NOT_ALLOWED",
                $"الامتداد ({trailingExt}) غير مسموح به لنوع الملف المختار. الامتدادات المسموحة: {string.Join(", ", allowedExtensionsForFileType)}.",
                $"Extension '{trailingExt}' is not allowed for the selected file type. Allowed: {string.Join(", ", allowedExtensionsForFileType)}.");
        }

        return new FileContentValidationResult(true, safeName, trailingExt, null, null, null, null);
    }

    public async Task<FileContentValidationResult> ValidateStagedContentAsync(
        string stagedFilePath,
        string normalizedFileName,
        string extension,
        string catalogCode,
        CancellationToken cancellationToken = default)
    {
        var header = new byte[Math.Min(4096, new FileInfo(stagedFilePath).Length)];
        await using (var fs = new FileStream(stagedFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true))
        {
            var read = 0;
            while (read < header.Length)
            {
                var n = await fs.ReadAsync(header.AsMemory(read, header.Length - read), cancellationToken);
                if (n == 0) break;
                read += n;
            }
        }

        // Reject common binary executable headers regardless of extension (MZ/PE, ELF, Mach-O, class).
        if (HasExecutableMagicHeader(header))
        {
            return Invalid(
                "FILE_EXECUTABLE_SIGNATURE",
                "مرفوض أمنيًا: تم اكتشاف توقيع ملف تنفيذي داخل المحتوى رغم الامتداد الظاهر.",
                "Security violation: executable binary magic bytes detected inside file content.");
        }

        return extension.ToLowerInvariant() switch
        {
            ".pdf" => ValidatePdf(header, normalizedFileName, extension),
            ".docx" => ValidateOfficeZipPackage(stagedFilePath, normalizedFileName, extension, "word/document.xml", "word/vbaProject.bin",
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document"),
            ".xlsx" => ValidateOfficeZipPackage(stagedFilePath, normalizedFileName, extension, "xl/workbook.xml", "xl/vbaProject.bin",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"),
            ".zip" => ValidateGenericZip(stagedFilePath, normalizedFileName, extension),
            ".json" => await ValidateJsonAsync(stagedFilePath, normalizedFileName, extension, cancellationToken),
            ".yaml" or ".yml" => await ValidateTextYamlOrMarkdownAsync(stagedFilePath, normalizedFileName, extension, isYaml: true, cancellationToken),
            ".md" or ".markdown" => await ValidateTextYamlOrMarkdownAsync(stagedFilePath, normalizedFileName, extension, isYaml: false, cancellationToken),
            _ => Invalid(
                "FILE_TYPE_UNSUPPORTED",
                $"نوع المحتوى للامتداد ({extension}) غير مدعوم.",
                $"Unsupported file content type for extension '{extension}'.")
        };
    }

    private static bool HasExecutableMagicHeader(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 2 && header[0] == (byte)'M' && header[1] == (byte)'Z')
        {
            return true;
        }

        if (header.Length >= 4)
        {
            if (header[0] == 0x7F && header[1] == (byte)'E' && header[2] == (byte)'L' && header[3] == (byte)'F')
            {
                return true;
            }

            var be32 = BinaryPrimitives.ReadUInt32BigEndian(header[..4]);
            if (be32 is 0xCAFEBABE or 0xFEEDFACE or 0xFEEDFACF or 0xCEFAEDFE or 0xCFFAEDFE)
            {
                return true;
            }
        }

        return false;
    }

    private static FileContentValidationResult ValidatePdf(ReadOnlySpan<byte> header, string fileName, string extension)
    {
        // PDF must begin with %PDF- signature (allow optional UTF-8 BOM or leading whitespace).
        var textPrefix = Encoding.ASCII.GetString(header[..Math.Min(64, header.Length)]).TrimStart();
        if (!textPrefix.StartsWith("%PDF-", StringComparison.Ordinal))
        {
            return Invalid(
                "FILE_PDF_INVALID_SIGNATURE",
                "المحتوى الفعلي لا يطابق ترويسة ملف PDF صالحة (%PDF-).",
                "File bytes do not match a valid PDF header signature (%PDF-).");
        }

        return Valid(fileName, extension, "application/pdf");
    }

    private static FileContentValidationResult ValidateOfficeZipPackage(
        string filePath,
        string fileName,
        string extension,
        string requiredInternalEntry,
        string forbiddenMacroEntry,
        string canonicalMediaType)
    {
        var zipSafety = InspectZipArchive(filePath, requireContentTypes: true, requiredEntry: requiredInternalEntry, forbiddenEntry: forbiddenMacroEntry);
        if (!zipSafety.IsSafe)
        {
            return Invalid(zipSafety.ErrorCode!, zipSafety.MessageAr!, zipSafety.MessageEn!);
        }

        return Valid(fileName, extension, canonicalMediaType);
    }

    private static FileContentValidationResult ValidateGenericZip(string filePath, string fileName, string extension)
    {
        var zipSafety = InspectZipArchive(filePath, requireContentTypes: false, requiredEntry: null, forbiddenEntry: null);
        if (!zipSafety.IsSafe)
        {
            return Invalid(zipSafety.ErrorCode!, zipSafety.MessageAr!, zipSafety.MessageEn!);
        }

        return Valid(fileName, extension, "application/zip");
    }

    private static (bool IsSafe, string? ErrorCode, string? MessageAr, string? MessageEn) InspectZipArchive(
        string filePath,
        bool requireContentTypes,
        string? requiredEntry,
        string? forbiddenEntry)
    {
        try
        {
            var compressedFileSize = Math.Max(1L, new FileInfo(filePath).Length);
            using var archive = ZipFile.OpenRead(filePath);

            if (archive.Entries.Count == 0 || archive.Entries.Count > MaxZipEntries)
            {
                return (false,
                    "FILE_ARCHIVE_LIMIT",
                    $"عدد العناصر داخل الأرشيف المضغوط غير صالح أو يتجاوز الحد المسموح ({MaxZipEntries}).",
                    $"Archive entry count is empty or exceeds the maximum limit ({MaxZipEntries}).");
            }

            long totalUncompressed = 0;
            var hasContentTypes = false;
            var hasRequired = requiredEntry is null;

            foreach (var entry in archive.Entries)
            {
                var entryPath = entry.FullName.Replace('\\', '/');
                if (entryPath.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(entryPath))
                {
                    return (false,
                        "FILE_ARCHIVE_TRAVERSAL",
                        "مرفوض أمنيًا: تم اكتشاف مسار اختراق داخل الحزمة المضغوطة (Zip Slip).",
                        "Security violation: path traversal entry detected inside archive.");
                }

                if (forbiddenEntry is not null && string.Equals(entryPath, forbiddenEntry, StringComparison.OrdinalIgnoreCase))
                {
                    return (false,
                        "FILE_MACRO_FORBIDDEN",
                        "مرفوض أمنيًا: يمنع رفع مستندات تحتوي على وحدات ماكرو (VBA).",
                        "Security violation: macro-enabled Office packages (VBA) are rejected.");
                }

                var entryExt = Path.GetExtension(entry.Name);
                if (!string.IsNullOrEmpty(entryExt) && DangerousExtensions.Contains(entryExt))
                {
                    return (false,
                        "FILE_ARCHIVE_DANGEROUS_ENTRY",
                        $"مرفوض أمنيًا: الأرشيف يحتوي على ملف خطير أو نشط ({entry.Name}).",
                        $"Security violation: archive contains dangerous or active entry '{entry.Name}'.");
                }

                if (string.Equals(entryPath, "[Content_Types].xml", StringComparison.OrdinalIgnoreCase))
                {
                    hasContentTypes = true;
                }

                if (requiredEntry is not null && string.Equals(entryPath, requiredEntry, StringComparison.OrdinalIgnoreCase))
                {
                    hasRequired = true;
                }

                if (entry.Length < 0)
                {
                    return (false,
                        "FILE_ARCHIVE_CORRUPT",
                        "تم رفض الملف لتلف حجم أحد عناصر الحزمة.",
                        "Corrupt uncompressed length entry inside archive.");
                }

                totalUncompressed += entry.Length;
                if (totalUncompressed > MaxZipUncompressedBytes)
                {
                    return (false,
                        "FILE_ZIP_BOMB_SIZE",
                        "مرفوض أمنيًا: الحجم الإجمالي بعد فك الضغط يتجاوز الحد الآمن.",
                        "Security violation: uncompressed archive size exceeds safety ceiling.");
                }
            }

            if ((double)totalUncompressed / compressedFileSize > MaxZipCompressionRatio)
            {
                return (false,
                    "FILE_ZIP_BOMB_RATIO",
                    "مرفوض أمنيًا: نسبة الضغط غير طبيعية وتخالف فحص الحماية من قنبلة الضغط.",
                    "Security violation: abnormal archive compression ratio detected (zip-bomb guard).");
            }

            if (requireContentTypes && (!hasContentTypes || !hasRequired))
            {
                return (false,
                    "FILE_OOXML_STRUCTURE_INVALID",
                    "بنية مستند Office لا تطابق معيار OpenXML الصحيح للنوع المختار.",
                    "Archive structure does not match a valid OpenXML document package.");
            }

            return (true, null, null, null);
        }
        catch (InvalidDataException)
        {
            return (false,
                "FILE_ARCHIVE_INVALID",
                "الملف المضغوط تالف أو لا يطابق بنية ZIP/OOXML صالحة.",
                "File content is corrupt or not a valid ZIP/OOXML archive.");
        }
    }

    private static async Task<FileContentValidationResult> ValidateJsonAsync(
        string filePath,
        string fileName,
        string extension,
        CancellationToken cancellationToken)
    {
        try
        {
            var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
            if (Array.IndexOf(bytes, (byte)0) >= 0)
            {
                return Invalid(
                    "FILE_BINARY_IN_TEXT",
                    "مرفوض: تم العثور على محتوى ثنائي غير صالح داخل ملف JSON.",
                    "Binary NUL bytes detected inside JSON text file.");
            }

            using var doc = JsonDocument.Parse(bytes);
            if (doc.RootElement.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
            {
                return Invalid(
                    "FILE_JSON_ROOT_INVALID",
                    "ملف JSON يجب أن يحتوي على كائن أو مصفوفة صالحة.",
                    "JSON file root must be a valid JSON object or array.");
            }

            return Valid(fileName, extension, "application/json");
        }
        catch (JsonException)
        {
            return Invalid(
                "FILE_JSON_SYNTAX_INVALID",
                "تعذر قراءة الملف بوصفه JSON صالح البنية.",
                "Uploaded JSON file has invalid JSON syntax.");
        }
    }

    private static async Task<FileContentValidationResult> ValidateTextYamlOrMarkdownAsync(
        string filePath,
        string fileName,
        string extension,
        bool isYaml,
        CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
        if (Array.IndexOf(bytes, (byte)0) >= 0)
        {
            return Invalid(
                "FILE_BINARY_IN_TEXT",
                "مرفوض: المحتوى يحتوي على بيانات ثنائية داخل ملف نصي.",
                "Rejected: binary bytes detected inside a text documentation file.");
        }

        var text = Encoding.UTF8.GetString(bytes);
        if (string.IsNullOrWhiteSpace(text))
        {
            return Invalid(
                "FILE_EMPTY_TEXT",
                "محتوى الملف النصي فارغ.",
                "Uploaded text file contains no readable content.");
        }

        // Guard against disguised active HTML/SVG smuggling.
        if (text.Contains("<script", StringComparison.OrdinalIgnoreCase) ||
            text.TrimStart().StartsWith("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase) ||
            text.TrimStart().StartsWith("<html", StringComparison.OrdinalIgnoreCase) ||
            text.TrimStart().StartsWith("<svg", StringComparison.OrdinalIgnoreCase))
        {
            return Invalid(
                "FILE_ACTIVE_HTML_REJECTED",
                "مرفوض أمنيًا: تم اكتشاف وسوم HTML/Script نشطة داخل الملف.",
                "Security violation: active HTML/script markup detected inside text document.");
        }

        if (isYaml && !text.Contains(':', StringComparison.Ordinal))
        {
            return Invalid(
                "FILE_YAML_STRUCTURE_INVALID",
                "محتوى ملف YAML لا يحتوي على بنية مفاتيح وقيم صالحة.",
                "YAML file does not appear to contain valid key-value structure.");
        }

        return Valid(fileName, extension, isYaml ? "application/yaml" : "text/markdown");
    }

    private static FileContentValidationResult Valid(string fileName, string extension, string mediaType)
        => new(true, fileName, extension, mediaType, null, null, null);

    private static FileContentValidationResult Invalid(string code, string ar, string en)
        => new(false, null, null, null, code, ar, en);
}

