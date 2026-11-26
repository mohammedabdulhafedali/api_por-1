using System.ComponentModel.DataAnnotations;
using ApiGovernance.Shared.Security;

namespace ApiGovernance.Modules.IdentityAccess.Contracts;

public static class IdentityPermissionCodes
{
    public const string UsersPasswordReset = "users.password.reset";
}

public static class PlatformClaimTypes
{
    public const string SecurityStamp = PlatformSecurityClaimTypes.SecurityStamp;
    public const string FullName = "apigov:full_name";
    public const string ConsumerEntityId = "apigov:consumer_entity_id";
    public const string AuthProvider = "apigov:auth_provider";
}

public sealed record CsrfTokenResponse(
    string Token,
    string HeaderName);

public sealed record RoleSummaryDto(
    Guid Id,
    string Name);

public sealed record AuthenticatedUserResponse(
    Guid Id,
    string FullName,
    string Email,
    bool IsActive,
    Guid? ConsumerEntityId,
    string AuthProvider,
    IReadOnlyList<RoleSummaryDto> Roles,
    IReadOnlyList<string> Permissions,
    string TraceId,
    string Username = "");

public sealed record LoginRequest : IValidatableObject
{
    [MaxLength(100)]
    public string Username { get; init; } = string.Empty;

    [MaxLength(320)]
    public string Email { get; init; } = string.Empty;

    [Required(ErrorMessage = "كلمة المرور مطلوبة / Password is required.")]
    [MinLength(1)]
    [MaxLength(256)]
    public string Password { get; init; } = string.Empty;

    public string GetEffectiveLoginIdentifier() =>
        !string.IsNullOrWhiteSpace(Username) ? Username.Trim() : Email.Trim();

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(Username) && string.IsNullOrWhiteSpace(Email))
        {
            yield return new ValidationResult(
                "اسم المستخدم مطلوب / Username is required.",
                [nameof(Username)]);
        }

        if (string.IsNullOrEmpty(Password))
        {
            yield return new ValidationResult(
                "كلمة المرور مطلوبة / Password is required.",
                [nameof(Password)]);
        }
    }
}

public sealed record ChangePasswordRequest : IValidatableObject
{
    [Required(ErrorMessage = "كلمة المرور الحالية مطلوبة / Current password is required.")]
    [MaxLength(256)]
    public string CurrentPassword { get; init; } = string.Empty;

    [Required(ErrorMessage = "كلمة المرور الجديدة مطلوبة / New password is required.")]
    [MinLength(15, ErrorMessage = "يجب ألا تقل كلمة المرور الجديدة عن 15 حرفًا / New password must be at least 15 characters.")]
    [MaxLength(256)]
    public string NewPassword { get; init; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.Equals(CurrentPassword, NewPassword, StringComparison.Ordinal))
        {
            yield return new ValidationResult(
                "يجب أن تختلف كلمة المرور الجديدة عن كلمة المرور الحالية / New password must differ from current password.",
                [nameof(NewPassword)]);
        }
    }
}

public sealed record CompletePasswordResetRequest
{
    [Required(ErrorMessage = "البريد الإلكتروني مطلوب / Email is required.")]
    [EmailAddress(ErrorMessage = "صيغة البريد الإلكتروني غير صالحة / Invalid email format.")]
    [MaxLength(320)]
    public string Email { get; init; } = string.Empty;

    [Required(ErrorMessage = "رمز التفعيل أو الاستعادة مطلوب / Activation or reset token is required.")]
    [MinLength(16)]
    [MaxLength(2048)]
    public string Token { get; init; } = string.Empty;

    [Required(ErrorMessage = "كلمة المرور الجديدة مطلوبة / New password is required.")]
    [MinLength(15, ErrorMessage = "يجب ألا تقل كلمة المرور الجديدة عن 15 حرفًا / New password must be at least 15 characters.")]
    [MaxLength(256)]
    public string NewPassword { get; init; } = string.Empty;
}

