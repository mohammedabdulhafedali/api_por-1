namespace ApiGovernance.Modules.VersionsFiles.Configuration;

public sealed class FileStorageOptions
{
    public const string SectionName = "FileStorage";

    /// <summary>
    /// Absolute directory outside public web folders (www/wwwroot/Vite public).
    /// When empty, defaults to %LOCALAPPDATA%\ApiGovernance\PrivateStorage.
    /// In production on Windows Server + IIS, IT configures a dedicated non-web NTFS/UNC path.
    /// </summary>
    public string RootPath { get; set; } = string.Empty;

    /// <summary>
    /// Global hard ceiling (bytes) for any uploaded file regardless of file type MaxSizeBytes.
    /// </summary>
    public long MaxUploadBytesCeiling { get; set; } = 104_857_600L; // 100 MB

    public string ResolveRootPath()
    {
        if (!string.IsNullOrWhiteSpace(RootPath))
        {
            return Path.GetFullPath(RootPath.Trim());
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            localAppData = Path.GetTempPath();
        }

        return Path.Combine(localAppData, "ApiGovernance", "PrivateStorage");
    }
}

public sealed class FileScanningOptions
{
    public const string SectionName = "FileScanning";

    /// <summary>
    /// Real AV engine provider: "ClamAv" (TCP INSTREAM), "CommandLine" (enterprise CLI AV), or "FailClosed".
    /// Never simulates CLEAN when the engine is absent or unreachable.
    /// </summary>
    public string Provider { get; set; } = "ClamAv";

    public string ClamAvHost { get; set; } = string.Empty;

    public int ClamAvPort { get; set; } = 3310;

    public string CommandPath { get; set; } = string.Empty;

    public string CommandArguments { get; set; } = "-Scan -ScanType 3 -File \"{filePath}\" -DisableRemediation";

    public int TimeoutSeconds { get; set; } = 30;
}

