using System.ComponentModel.DataAnnotations;

namespace ApiGovernance.Modules.IdentityAccess.Contracts;

public static partial class UserPermissionCodes
{
    public const string UsersView = "users.view";
    public const string UsersCreate = "users.create";
    public const string UsersEdit = "users.edit";
    public const string UsersDelete = "users.delete";
    public const string UsersAssignRoles = "users.assign_roles";
    public const string UsersPasswordReset = IdentityPermissionCodes.UsersPasswordReset;
    public const string RolesView = "roles.view";
    public const string RolesCreate = "roles.create";
    public const string RolesEdit = "roles.edit";
    public const string RolesDelete = "roles.delete";
    public const string PermissionsView = "permissions.view";
    public const string PermissionsManage = "permissions.manage";
}

public sealed record UserSummaryDto(
    Guid Id,
    string FullName,
    string Email,
    bool IsActive,
    bool IsActivated,
    bool IsLockedOut,
    DateTimeOffset? LockoutEnd,
    Guid? ConsumerEntityId,
    DateTimeOffset CreatedAt,
    IReadOnlyList<RoleSummaryDto> Roles,
    string TraceId,
    string Username = "");

public sealed record PagedUsersResponse(
    IReadOnlyList<UserSummaryDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages,
    string TraceId);

public sealed record CreateUserRequest : IValidatableObject
{
    [Required(ErrorMessage = "الاسم الكامل مطلوب / Full name is required.")]
    [MinLength(2, ErrorMessage = "يجب ألا يقل الاسم عن حرفين / Full name must be at least 2 characters.")]
    [MaxLength(200)]
    public string FullName { get; init; } = string.Empty;

    [MaxLength(100)]
    public string? Username { get; init; }

    [Required(ErrorMessage = "البريد الإلكتروني مطلوب / Email is required.")]
    [EmailAddress(ErrorMessage = "صيغة البريد الإلكتروني غير صالحة / Invalid email format.")]
    [MaxLength(320)]
    public string Email { get; init; } = string.Empty;

    public Guid? ConsumerEntityId { get; init; }

    public bool TriggerActivationToken { get; init; }

    public string? InitialPassword { get; init; }

    public IReadOnlyList<Guid>? RoleIds { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(FullName) || FullName.Trim().Length < 2)
        {
            yield return new ValidationResult(
                "الاسم الكامل مطلوب (حرفان على الأقل) / Full name must be at least 2 characters.",
                [nameof(FullName)]);
        }

        if (Username is not null && Username.Trim().Length < 2)
        {
            yield return new ValidationResult(
                "اسم المستخدم يجب ألا يقل عن حرفين / Username must be at least 2 characters.",
                [nameof(Username)]);
        }

        if (string.IsNullOrWhiteSpace(Email))
        {
            yield return new ValidationResult(
                "البريد الإلكتروني مطلوب / Email is required.",
                [nameof(Email)]);
        }
    }
}

public sealed record UpdateUserRequest : IValidatableObject
{
    [MaxLength(200)]
    public string? FullName { get; init; }

    [MaxLength(100)]
    public string? Username { get; init; }

    public bool? IsActive { get; init; }

    public Guid? ConsumerEntityId { get; init; }

    public bool ClearConsumerEntity { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (FullName is not null && FullName.Trim().Length < 2)
        {
            yield return new ValidationResult(
                "يجب ألا يقل الاسم عن حرفين / Full name must be at least 2 characters.",
                [nameof(FullName)]);
        }

        if (Username is not null && Username.Trim().Length < 2)
        {
            yield return new ValidationResult(
                "يجب ألا يقل اسم المستخدم عن حرفين / Username must be at least 2 characters.",
                [nameof(Username)]);
        }

        if (FullName is null && Username is null && IsActive is null && ConsumerEntityId is null && !ClearConsumerEntity)
        {
            yield return new ValidationResult(
                "يجب تحديد حقل واحد على الأقل للتعديل / At least one updatable field is required.",
                [nameof(FullName)]);
        }
    }
}

