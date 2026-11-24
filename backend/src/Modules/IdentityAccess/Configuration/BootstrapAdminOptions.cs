namespace ApiGovernance.Modules.IdentityAccess.Configuration;

/// <summary>
/// Configuration options for secure one-time bootstrap of the first platform administrator (Task I02).
/// Secrets must never be committed in appsettings.json; supply via User Secrets or Environment Variables only.
/// </summary>
public sealed class BootstrapAdminOptions
{
    public const string SectionName = "BootstrapAdmin";

    public string? Email { get; set; }
    public string? FullName { get; set; }
    public string? Password { get; set; }
    public bool AutoRunOnEmptyDatabase { get; set; }
}

