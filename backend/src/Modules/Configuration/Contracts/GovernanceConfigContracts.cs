using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ApiGovernance.Modules.Configuration.Contracts;

// ============================================================================
// M05 • File Requirement Templates & Historical Snapshots (UI-23)
// ============================================================================
public static class RequirementRuleModes
{
    public const string Required = "REQUIRED";
    public const string Optional = "OPTIONAL";
    public const string Conditional = "CONDITIONAL";

    public static bool IsValid(string? mode)
        => string.Equals(mode, Required, StringComparison.OrdinalIgnoreCase)
           || string.Equals(mode, Optional, StringComparison.OrdinalIgnoreCase)
           || string.Equals(mode, Conditional, StringComparison.OrdinalIgnoreCase);

    public static string Normalize(string mode) => mode.Trim().ToUpperInvariant();
}

public sealed record RequirementFileTypeOptionDto(
    Guid Id,
    string CatalogCode,
    string DisplayName,
    IReadOnlyList<string> Extensions,
    long MaxSizeBytes,
    bool IsActive);

public sealed record RequirementRuleInput : IValidatableObject
{
    [Required]
    public Guid FileTypeId { get; init; }

    [Required(ErrorMessage = "وضع الإلزامية مطلوب / RequirementMode is required.")]
    [MaxLength(16)]
    public string RequirementMode { get; init; } = RequirementRuleModes.Required;

    [Range(0, 100, ErrorMessage = "الحد الأدنى يجب أن يكون بين 0 و100 / MinCount must be between 0 and 100.")]
    public int MinCount { get; init; } = 1;

    [Range(1, 100, ErrorMessage = "الحد الأقصى يجب أن يكون بين 1 و100 / MaxCount must be between 1 and 100.")]
    public int MaxCount { get; init; } = 1;

    [MaxLength(4000)]
    public string? ConditionSpecJson { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (FileTypeId == Guid.Empty)
        {
            yield return new ValidationResult("معرّف نوع الملف مطلوب / FileTypeId is required.", [nameof(FileTypeId)]);
        }

        if (!RequirementRuleModes.IsValid(RequirementMode))
        {
            yield return new ValidationResult(
                "وضع المتطلب يجب أن يكون REQUIRED أو OPTIONAL أو CONDITIONAL.",
                [nameof(RequirementMode)]);
        }

        if (MaxCount < MinCount)
        {
            yield return new ValidationResult(
                "الحد الأقصى للملفات يجب أن يكون أكبر من أو يساوي الحد الأدنى / MaxCount must be >= MinCount.",
                [nameof(MaxCount)]);
        }

        if (string.Equals(RequirementMode?.Trim(), RequirementRuleModes.Required, StringComparison.OrdinalIgnoreCase) && MinCount < 1)
        {
            yield return new ValidationResult(
                "للملفات الإلزامية يجب أن يكون الحد الأدنى 1 على الأقل / REQUIRED mode requires MinCount >= 1.",
                [nameof(MinCount)]);
        }

        if (string.Equals(RequirementMode?.Trim(), RequirementRuleModes.Optional, StringComparison.OrdinalIgnoreCase) && MinCount != 0)
        {
            yield return new ValidationResult(
                "للملفات الاختيارية يجب أن يكون الحد الأدنى 0 / OPTIONAL mode requires MinCount = 0.",
                [nameof(MinCount)]);
        }

        if (!string.IsNullOrWhiteSpace(ConditionSpecJson))
        {
            var validJson = true;
            try
            {
                using var _ = JsonDocument.Parse(ConditionSpecJson);
            }
            catch (JsonException)
            {
                validJson = false;
            }

            if (!validJson)
            {
                yield return new ValidationResult(
                    "صيغة شرط الاعتماد (JSON) غير صالحة / ConditionSpecJson must be valid JSON.",
                    [nameof(ConditionSpecJson)]);
            }
        }
    }
}

public sealed record FileRequirementRuleDto(
    Guid Id,
    Guid FileTypeId,
    string FileTypeName,
    string CatalogCode,
    IReadOnlyList<string> Extensions,
    bool FileTypeIsActive,
    string RequirementMode,
    int MinCount,
    int MaxCount,
    string? ConditionSpecJson);

public sealed record FileRequirementTemplateDto(
    Guid Id,
    string Name,
    bool IsActive,
    IReadOnlyList<FileRequirementRuleDto> Rules,
    int DocumentsCount,
    int SnapshotCount,
    DateTimeOffset? LatestSnapshotAt,
    string TraceId);

public sealed record RequirementTemplateSnapshotDto(
    Guid AuditEventId,
    int VersionNumber,
    string Operation,
    DateTimeOffset CapturedAtUtc,
    Guid? ActorUserId,
    string ActorName,
    string TemplateName,
    bool IsActive,
    IReadOnlyList<FileRequirementRuleDto> Rules);

public sealed record CreateRequirementTemplateRequest : IValidatableObject
{
    [Required(ErrorMessage = "اسم القالب مطلوب / Template name is required.")]
    [MinLength(2, ErrorMessage = "اسم القالب لا يقل عن حرفين / Name must be at least 2 characters.")]
    [MaxLength(200)]
    public string Name { get; init; } = string.Empty;

    [Required]
    public IReadOnlyList<RequirementRuleInput> Rules { get; init; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Trim().Length < 2)
        {
            yield return new ValidationResult("اسم القالب مطلوب (حرفان على الأقل).", [nameof(Name)]);
        }

        if (Rules is null || Rules.Count == 0)
        {
            yield return new ValidationResult(
                "يجب إضافة قاعدة نوع ملف واحدة على الأقل داخل القالب / At least one file requirement rule is required.",
                [nameof(Rules)]);
            yield break;
        }

        foreach (var nested in GovernanceValidationHelpers.ValidateItems(Rules, nameof(Rules)))
        {
            yield return nested;
        }
    }
}

public sealed record UpdateRequirementTemplateRequest : IValidatableObject
{
    [MaxLength(200)]
    public string? Name { get; init; }

    public bool? IsActive { get; init; }

    public IReadOnlyList<RequirementRuleInput>? Rules { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Name is not null && Name.Trim().Length < 2)
        {
            yield return new ValidationResult("اسم القالب لا يقل عن حرفين.", [nameof(Name)]);
        }

        if (Rules is not null && Rules.Count == 0)
        {
            yield return new ValidationResult(
                "يجب أن يحتوي القالب على قاعدة نوع ملف واحدة على الأقل.",
                [nameof(Rules)]);
        }
        else if (Rules is not null)
        {
            foreach (var nested in GovernanceValidationHelpers.ValidateItems(Rules, nameof(Rules)))
            {
                yield return nested;
            }
        }

        if (Name is null && IsActive is null && Rules is null)
        {
            yield return new ValidationResult(
                "يجب تحديد حقل واحد على الأقل للتحديث.",
                [nameof(Name)]);
        }
    }
}

internal static class GovernanceValidationHelpers
{
    public static IEnumerable<ValidationResult> ValidateItems<T>(IReadOnlyList<T> items, string propertyPrefix)
    {
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (item is null)
            {
                yield return new ValidationResult("العنصر غير صالح.", [$"{propertyPrefix}[{i}]"]);
                continue;
            }

            var results = new List<ValidationResult>();
            var ctx = new ValidationContext(item);
            if (!Validator.TryValidateObject(item, ctx, results, validateAllProperties: true))
            {
                foreach (var r in results)
                {
                    var members = r.MemberNames.Any()
                        ? r.MemberNames.Select(m => $"{propertyPrefix}[{i}].{m}").ToArray()
                        : [$"{propertyPrefix}[{i}]"];
                    yield return new ValidationResult(r.ErrorMessage, members);
                }
            }
        }
    }
}

// ============================================================================
// M06 • Approval Routes & Ordered Role-Based Stages (UI-05 & UI-05A)
// Explicitly prohibits StageType or AssignedUserId per DB-07 & 02_UX_UI_v1.2.md
// ============================================================================
public sealed record ApprovalRouteRoleOptionDto(
    Guid Id,
    string Name,
    bool IsActive);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ApprovalRouteStageInput : IValidatableObject
{
    [Range(1, 50, ErrorMessage = "ترتيب المرحلة يجب أن يبدأ من 1 / Position must be >= 1.")]
    public int Position { get; init; }

    [Required(ErrorMessage = "اسم المرحلة مطلوب / StageName is required.")]
    [MinLength(2, ErrorMessage = "اسم المرحلة لا يقل عن حرفين / StageName must be at least 2 characters.")]
    [MaxLength(160)]
    public string StageName { get; init; } = string.Empty;

    [Required]
    public Guid ResponsibleRoleId { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Position < 1)
        {
            yield return new ValidationResult("ترتيب المرحلة يجب أن يبدأ من 1.", [nameof(Position)]);
        }

        if (string.IsNullOrWhiteSpace(StageName) || StageName.Trim().Length < 2)
        {
            yield return new ValidationResult("اسم المرحلة مطلوب (حرفان على الأقل).", [nameof(StageName)]);
        }

        if (ResponsibleRoleId == Guid.Empty)
        {
            yield return new ValidationResult("الدور المسؤول عن المرحلة مطلوب.", [nameof(ResponsibleRoleId)]);
        }
    }
}

public sealed record ApprovalRouteStageDto(
    Guid Id,
    int Position,
    string StageName,
    Guid ResponsibleRoleId,
    string ResponsibleRoleName,
    bool ResponsibleRoleIsActive);

public sealed record ApprovalRouteDto(
    Guid Id,
    string Name,
    bool IsActive,
    IReadOnlyList<ApprovalRouteStageDto> Stages,
    int ChangeTypesCount,
    int DocumentsCount,
    int ActiveWorkflowRunsCount,
    string TraceId);

public sealed record CreateApprovalRouteRequest : IValidatableObject
{
    [Required(ErrorMessage = "اسم مسار الاعتماد مطلوب / Route name is required.")]
    [MinLength(2, ErrorMessage = "اسم مسار الاعتماد لا يقل عن حرفين.")]
    [MaxLength(200)]
    public string Name { get; init; } = string.Empty;

    public IReadOnlyList<ApprovalRouteStageInput>? Stages { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Trim().Length < 2)
        {
            yield return new ValidationResult("اسم مسار الاعتماد مطلوب (حرفان على الأقل).", [nameof(Name)]);
        }

        if (Stages is not null)
        {
            foreach (var nested in GovernanceValidationHelpers.ValidateItems(Stages, nameof(Stages)))
            {
                yield return nested;
            }
        }
    }
}

public sealed record UpdateApprovalRouteRequest : IValidatableObject
{
    [MaxLength(200)]
    public string? Name { get; init; }

    public bool? IsActive { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Name is not null && Name.Trim().Length < 2)
        {
            yield return new ValidationResult("اسم المسار لا يقل عن حرفين.", [nameof(Name)]);
        }

        if (Name is null && IsActive is null)
        {
            yield return new ValidationResult("يجب تحديد حقل واحد على الأقل للتحديث.", [nameof(Name)]);
        }
    }
}

public sealed record ReplaceApprovalRouteStagesRequest : IValidatableObject
{
    [Required]
    public IReadOnlyList<ApprovalRouteStageInput> Stages { get; init; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Stages is null || Stages.Count == 0)
        {
            yield return new ValidationResult(
                "يجب أن يحتوي مسار الاعتماد على مرحلة واحدة على الأقل (مسار غير صالح).",
                [nameof(Stages)]);
            yield break;
        }

        foreach (var nested in GovernanceValidationHelpers.ValidateItems(Stages, nameof(Stages)))
        {
            yield return nested;
        }
    }
}

// ============================================================================
// M07 • Change Types & Change Policies (UI-06)
// ============================================================================
public sealed record ApprovalRouteOptionDto(
    Guid Id,
    string Name,
    int StageCount,
    bool IsActive);

public sealed record ChangeTypeDto(
    Guid Id,
    string Name,
    Guid RouteId,
    string RouteName,
    bool RouteIsActive,
    int RouteStageCount,
    bool IsActive,
    int PoliciesCount,
    int ChangeRequestsCount,
    string TraceId);

public sealed record CreateChangeTypeRequest : IValidatableObject
{
    [Required(ErrorMessage = "اسم نوع التغيير مطلوب / Change type name is required.")]
    [MinLength(2, ErrorMessage = "اسم نوع التغيير لا يقل عن حرفين.")]
    [MaxLength(160)]
    public string Name { get; init; } = string.Empty;

    [Required]
    public Guid RouteId { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Trim().Length < 2)
        {
            yield return new ValidationResult("اسم نوع التغيير مطلوب (حرفان على الأقل).", [nameof(Name)]);
        }

        if (RouteId == Guid.Empty)
        {
            yield return new ValidationResult("مسار الاعتماد المرتبط مطلوب.", [nameof(RouteId)]);
        }
    }
}

public sealed record UpdateChangeTypeRequest : IValidatableObject
{
    [MaxLength(160)]
    public string? Name { get; init; }

    public Guid? RouteId { get; init; }

    public bool? IsActive { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Name is not null && Name.Trim().Length < 2)
        {
            yield return new ValidationResult("اسم نوع التغيير لا يقل عن حرفين.", [nameof(Name)]);
        }

        if (RouteId is not null && RouteId.Value == Guid.Empty)
        {
            yield return new ValidationResult("مسار الاعتماد غير صالح.", [nameof(RouteId)]);
        }

        if (Name is null && RouteId is null && IsActive is null)
        {
            yield return new ValidationResult("يجب تحديد حقل واحد على الأقل للتحديث.", [nameof(Name)]);
        }
    }
}

public sealed record ChangePolicyTypeSummaryDto(
    Guid ChangeTypeId,
    string ChangeTypeName,
    Guid RouteId,
    string RouteName,
    bool ChangeTypeIsActive);

public sealed record ChangePolicyDto(
    Guid Id,
    string Name,
    bool IsActive,
    IReadOnlyList<ChangePolicyTypeSummaryDto> ChangeTypes,
    int DocumentsCount,
    string TraceId);

public sealed record CreateChangePolicyRequest : IValidatableObject
{
    [Required(ErrorMessage = "اسم سياسة التغيير مطلوب / Policy name is required.")]
    [MinLength(2, ErrorMessage = "اسم سياسة التغيير لا يقل عن حرفين.")]
    [MaxLength(160)]
    public string Name { get; init; } = string.Empty;

    [Required]
    public IReadOnlyList<Guid> ChangeTypeIds { get; init; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Trim().Length < 2)
        {
            yield return new ValidationResult("اسم سياسة التغيير مطلوب (حرفان على الأقل).", [nameof(Name)]);
        }

        if (ChangeTypeIds is null || ChangeTypeIds.Count == 0)
        {
            yield return new ValidationResult(
                "يجب ربط سياسة التغيير بنوع تغيير واحد على الأقل.",
                [nameof(ChangeTypeIds)]);
        }
    }
}

public sealed record UpdateChangePolicyRequest : IValidatableObject
{
    [MaxLength(160)]
    public string? Name { get; init; }

    public bool? IsActive { get; init; }

    public IReadOnlyList<Guid>? ChangeTypeIds { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Name is not null && Name.Trim().Length < 2)
        {
            yield return new ValidationResult("اسم سياسة التغيير لا يقل عن حرفين.", [nameof(Name)]);
        }

        if (ChangeTypeIds is not null && ChangeTypeIds.Count == 0)
        {
            yield return new ValidationResult(
                "يجب ربط سياسة التغيير بنوع تغيير واحد على الأقل.",
                [nameof(ChangeTypeIds)]);
        }

        if (Name is null && IsActive is null && ChangeTypeIds is null)
        {
            yield return new ValidationResult("يجب تحديد حقل واحد على الأقل للتحديث.", [nameof(Name)]);
        }
    }
}
