using System.ComponentModel.DataAnnotations;

namespace ApiGovernance.Modules.MasterData.Contracts;

public static class CompanySystemUsageTypes
{
    public const string None = "NONE";
    public const string Provider = "PROVIDER";
    public const string Consumer = "CONSUMER";
    public const string Both = "BOTH";
}

// ============================================================================
// M01 • Company Systems & Computed Usage Types
// ============================================================================
public sealed record CompanySystemDto(
    Guid Id,
    string Name,
    string? Description,
    bool IsActive,
    string ComputedUsageType,
    bool IsRegisteredInternalProvider,
    int DocumentsAsProviderCount,
    int DocumentsAsConsumerCount,
    string TraceId);

public sealed record CreateCompanySystemRequest : IValidatableObject
{
    [Required(ErrorMessage = "اسم النظام مطلوب / Company system name is required.")]
    [MinLength(2, ErrorMessage = "يجب ألا يقل اسم النظام عن حرفين / Name must be at least 2 characters.")]
    [MaxLength(200)]
    public string Name { get; init; } = string.Empty;

    [MaxLength(2000)]
    public string? Description { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Trim().Length < 2)
        {
            yield return new ValidationResult(
                "اسم النظام مطلوب (حرفان على الأقل) / Company system name must be at least 2 characters.",
                [nameof(Name)]);
        }
    }
}

public sealed record UpdateCompanySystemRequest : IValidatableObject
{
    [MaxLength(200)]
    public string? Name { get; init; }

    [MaxLength(2000)]
    public string? Description { get; init; }

    public bool ClearDescription { get; init; }

    public bool? IsActive { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Name is not null && Name.Trim().Length < 2)
        {
            yield return new ValidationResult(
                "يجب ألا يقل اسم النظام عن حرفين / Name must be at least 2 characters.",
                [nameof(Name)]);
        }

        if (Name is null && Description is null && !ClearDescription && IsActive is null)
        {
            yield return new ValidationResult(
                "يجب تحديد حقل واحد على الأقل للتحديث / At least one field is required to update.",
                [nameof(Name)]);
        }
    }
}

// ============================================================================
// M02 • Internal & External Provider Systems
// ============================================================================
public sealed record InternalProviderDto(
    Guid Id,
    Guid CompanySystemId,
    string CompanySystemName,
    string? CompanySystemDescription,
    bool CompanySystemIsActive,
    bool IsActive,
    int DocumentsCount,
    string TraceId);

public sealed record CreateInternalProviderRequest
{
    [Required(ErrorMessage = "يجب اختيار نظام شركة / CompanySystemId is required.")]
    public Guid CompanySystemId { get; init; }
}

public sealed record UpdateInternalProviderRequest
{
    [Required(ErrorMessage = "حالة التفعيل مطلوبة / IsActive is required.")]
    public bool IsActive { get; init; }
}

public sealed record ExternalProviderDto(
    Guid Id,
    string Name,
    string? Description,
    bool IsActive,
    int DocumentsCount,
    string TraceId);

public sealed record CreateExternalProviderRequest : IValidatableObject
{
    [Required(ErrorMessage = "اسم المزود الخارجي مطلوب / External provider name is required.")]
    [MinLength(2, ErrorMessage = "يجب ألا يقل الاسم عن حرفين / Name must be at least 2 characters.")]
    [MaxLength(200)]
    public string Name { get; init; } = string.Empty;

    [MaxLength(2000)]
    public string? Description { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Trim().Length < 2)
        {
            yield return new ValidationResult(
                "اسم المزود الخارجي مطلوب (حرفان على الأقل) / External provider name must be at least 2 characters.",
                [nameof(Name)]);
        }
    }
}

public sealed record UpdateExternalProviderRequest : IValidatableObject
{
    [MaxLength(200)]
    public string? Name { get; init; }

    [MaxLength(2000)]
    public string? Description { get; init; }

    public bool ClearDescription { get; init; }

    public bool? IsActive { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Name is not null && Name.Trim().Length < 2)
        {
            yield return new ValidationResult(
                "يجب ألا يقل الاسم عن حرفين / Name must be at least 2 characters.",
                [nameof(Name)]);
        }

        if (Name is null && Description is null && !ClearDescription && IsActive is null)
        {
            yield return new ValidationResult(
                "يجب تحديد حقل واحد على الأقل للتحديث / At least one field is required to update.",
                [nameof(Name)]);
        }
    }
}

// ============================================================================
// M03 • Consumer Categories & Consumer Entities (Single flat level)
// ============================================================================
public sealed record ConsumerCategoryDto(
    Guid Id,
    string Name,
    bool IsActive,
    int ConsumersCount,
    string TraceId);

public sealed record CreateConsumerCategoryRequest : IValidatableObject
{
    [Required(ErrorMessage = "اسم التصنيف مطلوب / Category name is required.")]
    [MinLength(2, ErrorMessage = "يجب ألا يقل اسم التصنيف عن حرفين / Category name must be at least 2 characters.")]
    [MaxLength(160)]
    public string Name { get; init; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Trim().Length < 2)
        {
            yield return new ValidationResult(
                "اسم التصنيف مطلوب (حرفان على الأقل) / Category name must be at least 2 characters.",
                [nameof(Name)]);
        }
    }
}

public sealed record UpdateConsumerCategoryRequest : IValidatableObject
{
    [MaxLength(160)]
    public string? Name { get; init; }

    public bool? IsActive { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Name is not null && Name.Trim().Length < 2)
        {
            yield return new ValidationResult(
                "يجب ألا يقل اسم التصنيف عن حرفين / Category name must be at least 2 characters.",
                [nameof(Name)]);
        }

        if (Name is null && IsActive is null)
        {
            yield return new ValidationResult(
                "يجب تحديد حقل واحد على الأقل للتحديث / At least one field is required to update.",
                [nameof(Name)]);
        }
    }
}

public sealed record ConsumerDto(
    Guid Id,
    Guid CategoryId,
    string CategoryName,
    bool CategoryIsActive,
    string Name,
    bool IsActive,
    int DocumentsCount,
    int AssignedUsersCount,
    string TraceId);

public sealed record CreateConsumerRequest : IValidatableObject
{
    [Required(ErrorMessage = "تصنيف الجهة المستهلكة مطلوب / CategoryId is required.")]
    public Guid CategoryId { get; init; }

    [Required(ErrorMessage = "اسم الجهة المستهلكة مطلوب / Consumer name is required.")]
    [MinLength(2, ErrorMessage = "يجب ألا يقل اسم الجهة عن حرفين / Consumer name must be at least 2 characters.")]
    [MaxLength(200)]
    public string Name { get; init; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (CategoryId == Guid.Empty)
        {
            yield return new ValidationResult(
                "تصنيف الجهة المستهلكة مطلوب / CategoryId is required.",
                [nameof(CategoryId)]);
        }

        if (string.IsNullOrWhiteSpace(Name) || Name.Trim().Length < 2)
        {
            yield return new ValidationResult(
                "اسم الجهة المستهلكة مطلوب (حرفان على الأقل) / Consumer name must be at least 2 characters.",
                [nameof(Name)]);
        }
    }
}

public sealed record UpdateConsumerRequest : IValidatableObject
{
    public Guid? CategoryId { get; init; }

    [MaxLength(200)]
    public string? Name { get; init; }

    public bool? IsActive { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Name is not null && Name.Trim().Length < 2)
        {
            yield return new ValidationResult(
                "يجب ألا يقل الاسم عن حرفين / Consumer name must be at least 2 characters.",
                [nameof(Name)]);
        }

        if (CategoryId is not null && CategoryId == Guid.Empty)
        {
            yield return new ValidationResult(
                "معرّف التصنيف غير صالح / Invalid CategoryId.",
                [nameof(CategoryId)]);
        }

        if (CategoryId is null && Name is null && IsActive is null)
        {
            yield return new ValidationResult(
                "يجب تحديد حقل واحد على الأقل للتحديث / At least one field is required to update.",
                [nameof(Name)]);
        }
    }
}

// ============================================================================
// M04 • API Groups
// ============================================================================
public sealed record ApiGroupDto(
    Guid Id,
    string Name,
    string? Description,
    bool IsActive,
    int DocumentsCount,
    string TraceId);

public sealed record CreateApiGroupRequest : IValidatableObject
{
    [Required(ErrorMessage = "اسم مجموعة API مطلوب / API group name is required.")]
    [MinLength(2, ErrorMessage = "يجب ألا يقل الاسم عن حرفين / API group name must be at least 2 characters.")]
    [MaxLength(180)]
    public string Name { get; init; } = string.Empty;

    [MaxLength(2000)]
    public string? Description { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Trim().Length < 2)
        {
            yield return new ValidationResult(
                "اسم مجموعة API مطلوب (حرفان على الأقل) / API group name must be at least 2 characters.",
                [nameof(Name)]);
        }
    }
}

public sealed record UpdateApiGroupRequest : IValidatableObject
{
    [MaxLength(180)]
    public string? Name { get; init; }

    [MaxLength(2000)]
    public string? Description { get; init; }

    public bool ClearDescription { get; init; }

    public bool? IsActive { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Name is not null && Name.Trim().Length < 2)
        {
            yield return new ValidationResult(
                "يجب ألا يقل الاسم عن حرفين / API group name must be at least 2 characters.",
                [nameof(Name)]);
        }

        if (Name is null && Description is null && !ClearDescription && IsActive is null)
        {
            yield return new ValidationResult(
                "يجب تحديد حقل واحد على الأقل للتحديث / At least one field is required to update.",
                [nameof(Name)]);
        }
    }
}

