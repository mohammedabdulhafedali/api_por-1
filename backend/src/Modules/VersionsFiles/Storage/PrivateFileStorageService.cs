using System.Security.Cryptography;
using ApiGovernance.Modules.VersionsFiles.Configuration;
using ApiGovernance.Modules.VersionsFiles.Contracts;
using ApiGovernance.Shared.Contracts;
using Microsoft.Extensions.Options;

namespace ApiGovernance.Modules.VersionsFiles.Storage;

/// <summary>
/// Task V01: Private immutable object storage strictly outside public web folders.
/// Storage keys are opaque server-generated paths; original filenames are metadata-only.
/// Objects in permanent storage (objects/) are write-once and SHA-256 verified on write and read.
/// </summary>
public interface IPrivateFileStore
{
    string RootDirectory { get; }

    string CreateStagingFilePath(string extension);

    Task<string> ComputeFileSha256Async(string filePath, CancellationToken cancellationToken = default);

    Task<StagedFileWriteResult> StageStreamAsync(
        Stream source,
        long maxAllowedBytes,
        CancellationToken cancellationToken = default);

    Task<string> CommitImmutableObjectAsync(
        string stagedOrQuarantinedPath,
        Guid fileObjectId,
        string expectedSha256,
        CancellationToken cancellationToken = default);

    Task<string> MoveToQuarantineAsync(
        string stagedFilePath,
        Guid fileObjectId,
        CancellationToken cancellationToken = default);

    string ResolveQuarantineFullPath(string storageKey);

    Task DeleteFileIfExistsAsync(string path, CancellationToken cancellationToken = default);

    Task<(Stream Stream, long SizeBytes)> OpenVerifiedImmutableObjectReadStreamAsync(
        string storageKey,
        string expectedSha256,
        CancellationToken cancellationToken = default);
}

public sealed class LocalPrivateFileStore : IPrivateFileStore, IPublishedObjectReadAccessor
{
    private const string ObjectsPrefix = "objects/";
    private const string QuarantinePrefix = "quarantine/";
    private const string StagingFolderName = "staging";

    private readonly string _rootPath;
    private readonly string _stagingPath;
    private readonly string _quarantineRoot;
    private readonly string _objectsRoot;

    public LocalPrivateFileStore(IOptions<FileStorageOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _rootPath = options.Value.ResolveRootPath();

        // Guard against misconfiguring private storage inside wwwroot or frontend/public web directories.
        var normalizedLower = _rootPath.Replace('\\', '/').ToLowerInvariant();
        if (normalizedLower.Contains("/wwwroot") || normalizedLower.EndsWith("/public", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"FileStorage:RootPath '{_rootPath}' must reside outside public web directories.");
        }

        _stagingPath = Path.Combine(_rootPath, StagingFolderName);
        _quarantineRoot = Path.Combine(_rootPath, "quarantine");
        _objectsRoot = Path.Combine(_rootPath, "objects");

        Directory.CreateDirectory(_stagingPath);
        Directory.CreateDirectory(_quarantineRoot);
        Directory.CreateDirectory(_objectsRoot);
    }

    public string RootDirectory => _rootPath;

    public string CreateStagingFilePath(string extension)
    {
        Directory.CreateDirectory(_stagingPath);
        var ext = string.IsNullOrWhiteSpace(extension) ? ".stage" : (extension.StartsWith('.') ? extension : "." + extension);
        return Path.Combine(_stagingPath, $"{Guid.NewGuid():N}{ext}");
    }

    public async Task<string> ComputeFileSha256Async(string filePath, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 64 * 1024, useAsync: true);
        var hashBytes = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexStringLower(hashBytes);
    }

    public async Task<StagedFileWriteResult> StageStreamAsync(
        Stream source,
        long maxAllowedBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        Directory.CreateDirectory(_stagingPath);

        var stagingFile = Path.Combine(_stagingPath, $"{Guid.NewGuid():N}.stage");
        var buffer = new byte[64 * 1024];
        long totalBytes = 0;

        try
        {
            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var fs = new FileStream(
                stagingFile,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 64 * 1024,
                useAsync: true))
            {
                while (true)
                {
                    var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                    if (read == 0)
                    {
                        break;
                    }

                    totalBytes += read;
                    if (totalBytes > maxAllowedBytes)
                    {
                        throw ApiContractException.PayloadTooLarge(
                            messageAr: $"يتجاوز حجم الملف الحد الأقصى المسموح به ({maxAllowedBytes:N0} بايت).",
                            messageEn: $"Uploaded file exceeds the maximum allowed size ({maxAllowedBytes:N0} bytes).");
                    }

                    hasher.AppendData(buffer, 0, read);
                    await fs.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }

                await fs.FlushAsync(cancellationToken);
            }

            if (totalBytes <= 0)
            {
                throw ApiContractException.UnprocessableEntity(
                    messageAr: "الملف المرفوع فارغ (0 بايت) ولا يمكن قبوله.",
                    messageEn: "Uploaded file is empty (0 bytes) and cannot be accepted.");
            }

            var sha256 = Convert.ToHexStringLower(hasher.GetHashAndReset());
            return new StagedFileWriteResult(stagingFile, sha256, totalBytes);
        }
        catch
        {
            await DeleteFileIfExistsAsync(stagingFile, CancellationToken.None);
            throw;
        }
    }

    public async Task<string> CommitImmutableObjectAsync(
        string stagedOrQuarantinedPath,
        Guid fileObjectId,
        string expectedSha256,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var relativeKey = $"{ObjectsPrefix}{now:yyyy}/{now:MM}/{fileObjectId:N}.bin";
        var targetPath = ResolveSafePathInside(_objectsRoot, relativeKey[ObjectsPrefix.Length..]);

        var targetDir = Path.GetDirectoryName(targetPath)!;
        Directory.CreateDirectory(targetDir);

        // Verify SHA-256 right before moving/writing into permanent store to detect tampering.
        var actualSha = await ComputeFileSha256Async(stagedOrQuarantinedPath, cancellationToken);
        if (!string.Equals(actualSha, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Staged object SHA-256 integrity mismatch prior to permanent commit.");
        }

        // Write-once enforcement: never overwrite an existing immutable object key.
        if (File.Exists(targetPath))
        {
            throw new InvalidOperationException($"Immutable storage key '{relativeKey}' already exists.");
        }

        File.Move(stagedOrQuarantinedPath, targetPath, overwrite: false);
        return relativeKey;
    }

    public Task<string> MoveToQuarantineAsync(
        string stagedFilePath,
        Guid fileObjectId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = DateTimeOffset.UtcNow;
        var relativeKey = $"{QuarantinePrefix}{now:yyyy}/{now:MM}/{fileObjectId:N}.qbin";
        var targetPath = ResolveSafePathInside(_quarantineRoot, relativeKey[QuarantinePrefix.Length..]);

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        File.Move(stagedFilePath, targetPath, overwrite: false);
        return Task.FromResult(relativeKey);
    }

    public string ResolveQuarantineFullPath(string storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey) || !storageKey.StartsWith(QuarantinePrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Storage key is not inside the quarantine namespace.");
        }

        return ResolveSafePathInside(_quarantineRoot, storageKey[QuarantinePrefix.Length..]);
    }

    public Task DeleteFileIfExistsAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup of staging files.
        }

        return Task.CompletedTask;
    }

    public Task<(Stream Stream, long SizeBytes)> OpenVerifiedObjectStreamAsync(
        string storageKey,
        string expectedSha256,
        CancellationToken cancellationToken = default)
        => OpenVerifiedImmutableObjectReadStreamAsync(storageKey, expectedSha256, cancellationToken);

    public async Task<(Stream Stream, long SizeBytes)> OpenVerifiedImmutableObjectReadStreamAsync(
        string storageKey,
        string expectedSha256,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(storageKey) || !storageKey.StartsWith(ObjectsPrefix, StringComparison.Ordinal))
        {
            throw ApiContractException.UnprocessableEntity(
                messageAr: "لا يمكن الوصول إلى هذا الملف لأنه غير مخزن في المخزن الدائم الموثوق.",
                messageEn: "File object is not stored in the verified immutable store.");
        }

        var fullPath = ResolveSafePathInside(_objectsRoot, storageKey[ObjectsPrefix.Length..]);
        if (!File.Exists(fullPath))
        {
            throw ApiContractException.NotFound(
                messageAr: "تعذر العثور على الكائن الثنائي للملف في المخزن الخاص.",
                messageEn: "Binary file object was not found in private storage.");
        }

        var actualSha = await ComputeFileSha256Async(fullPath, cancellationToken);
        if (!string.Equals(actualSha, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw ApiContractException.UnprocessableEntity(
                messageAr: "فشل التحقق من بصمة سلامة الملف (SHA-256)؛ تم منع التنزيل حمايةً للوثيقة.",
                messageEn: "File SHA-256 integrity verification failed; download blocked.");
        }

        var info = new FileInfo(fullPath);
        var readStream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 64 * 1024, useAsync: true);
        return (readStream, info.Length);
    }

    private static string ResolveSafePathInside(string rootFolder, string relativeSubPath)
    {
        var sanitizedRel = relativeSubPath.Replace('/', Path.DirectorySeparatorChar);
        var fullRoot = Path.GetFullPath(rootFolder);
        var combined = Path.GetFullPath(Path.Combine(fullRoot, sanitizedRel));
        if (!combined.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Resolved storage path escapes the configured private root directory.");
        }

        return combined;
    }
}

