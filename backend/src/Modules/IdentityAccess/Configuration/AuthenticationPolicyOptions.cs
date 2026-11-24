namespace ApiGovernance.Modules.IdentityAccess.Configuration;

/// <summary>
/// Configurable local authentication, password complexity, lockout, session, and CSRF policies
/// for Sprint S02 / Task I01 (04_Authentication.md / AUTH-01).
/// </summary>
public sealed class AuthenticationPolicyOptions
{
    public const string SectionName = "AuthenticationPolicy";

    /// <summary>
    /// Active identity provider discriminator ("LocalIdentity" in MVP; extensible for future SSO/AD integration).
    /// </summary>
    public string Provider { get; set; } = "LocalIdentity";

    // Password Policy (Minimum 15 characters per approved enterprise policy)
    public int PasswordMinLength { get; set; } = 15;
    public bool PasswordRequireDigit { get; set; } = true;
    public bool PasswordRequireLowercase { get; set; } = true;
    public bool PasswordRequireUppercase { get; set; } = true;
    public bool PasswordRequireNonAlphanumeric { get; set; } = true;
    public int PasswordRequiredUniqueChars { get; set; } = 4;

    // Account Lockout & Endpoint Rate-Limiting Policy
    public int MaxFailedAccessAttempts { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 15;
    public int LoginRateLimitPermitLimit { get; set; } = 12;
    public int LoginRateLimitWindowSeconds { get; set; } = 60;

    // Session & Cookie Policy
    public string SessionCookieName { get; set; } = "ApiGov.Session";
    public string AntiforgeryCookieName { get; set; } = "ApiGov.Antiforgery";
    public string AntiforgeryHeaderName { get; set; } = "X-CSRF-TOKEN";
    public int SessionIdleTimeoutMinutes { get; set; } = 30;
    public int SessionAbsoluteTimeoutMinutes { get; set; } = 480;

    // Activation & Password Reset Single-Use Token Policy
    public int ResetTokenLifespanMinutes { get; set; } = 30;

    // Data Protection Key Persistence (outside Git repository)
    public string DataProtectionApplicationName { get; set; } = "ApiGovernance.Platform";
    public string? DataProtectionKeyDirectory { get; set; }
}

