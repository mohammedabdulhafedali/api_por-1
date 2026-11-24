using System.ComponentModel.DataAnnotations;

namespace ApiGovernance.Modules.IdentityAccess.Contracts;

public sealed record RoleDetailsDto(
    Guid Id,
    string Name,
    bool IsActive,
    bool IsPinned,
    int AssignedUsersCount,
    int GrantedPermissionsCount,
    string TraceId);

public sealed record PagedRolesResponse(
    IReadOnlyList<RoleDetailsDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages,
    string TraceId);

public sealed record CreateRoleRequest : IValidatableObject
{
    [Required(ErrorMessage = "اسم الدور مطلوب / Role name is required.")]
    [MinLength(2, ErrorMessage = "يجب ألا يقل اسم الدور عن حرفين / Role name must be at least 2 characters.")]
    [MaxLength(120)]
    public string Name { get; init; } = string.Empty;

    public bool IsPinned { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Trim().Length < 2)
        {
            yield return new ValidationResult(
                "اسم الدور مطلوب (حرفان على الأقل) / Role name must be at least 2 characters.",
                [nameof(Name)]);
        }
    }
}

public sealed record UpdateRoleRequest : IValidatableObject
{
    [MaxLength(120)]
    public string? Name { get; init; }

    public bool? IsActive { get; init; }

    public bool? IsPinned { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Name is not null && Name.Trim().Length < 2)
        {
            yield return new ValidationResult(
                "يجب ألا يقل اسم الدور عن حرفين / Role name must be at least 2 characters.",
                [nameof(Name)]);
        }

        if (Name is null && IsActive is null && IsPinned is null)
        {
            yield return new ValidationResult(
                "يجب تحديد حقل واحد على الأقل لتعديل الدور / At least one field is required to update a role.",
                [nameof(Name)]);
        }
    }
}

public sealed record ReplaceUserRolesRequest
{
    [Required(ErrorMessage = "قائمة معرفات الأدوار مطلوبة / RoleIds collection is required.")]
    public IReadOnlyList<Guid> RoleIds { get; init; } = [];
}

public sealed record ReplaceUserRolesResponse(
    Guid UserId,
    IReadOnlyList<RoleSummaryDto> Roles,
    int EffectivePermissionsCount,
    string TraceId);

