using System.ComponentModel.DataAnnotations;

namespace ApiGovernance.Modules.Documents.Contracts;

public static class DocumentPublicationOptions
{
    public const string Manual = "MANUAL";
    public const string PostApproval = "POST_APPROVAL";

    public static bool IsValid(string? value)
        => string.Equals(value, Manual, StringComparison.OrdinalIgnoreCase)
           || string.Equals(value, PostApproval, StringComparison.OrdinalIgnoreCase);

    public static string Normalize(string value) => value.Trim().ToUpperInvariant();
}

public static class DocumentStates
{
    public const string Draft = "DRAFT";
    public const string Active = "ACTIVE";
    public const string Archived = "ARCHIVED";

    public static bool IsValid(string? value)
        => string.Equals(value, Draft, StringComparison.OrdinalIgnoreCase)
           || string.Equals(value, Active, StringComparison.OrdinalIgnoreCase)
           || string.Equals(value, Archived, StringComparison.OrdinalIgnoreCase);

    public static string Normalize(string value) => value.Trim().ToUpperInvariant();
}

public sealed record LookupOptionDto(Guid Id, string Name, string? Subtitle = null);

public sealed record DocumentLookupsResponse(
    IReadOnlyList<LookupOptionDto> InternalProviders,
    IReadOnlyList<LookupOptionDto> ExternalProviders,
    IReadOnlyList<LookupOptionDto> ConsumerCompanySystems,
    IReadOnlyList<LookupOptionDto> ConsumerEntities,
    IReadOnlyList<LookupOptionDto> ApiGroups,
    IReadOnlyList<LookupOptionDto> RequirementTemplates,
    IReadOnlyList<LookupOptionDto> ApprovalRoutes,
    IReadOnlyList<LookupOptionDto> ChangePolicies,
    IReadOnlyList<LookupOptionDto> ActiveUsers,
    string TraceId);

public sealed record AssignedDeveloperDto(
    Guid UserId,
    string FullName,
    string Email,
    bool IsActive);

public sealed record ApiDocumentDto(
    Guid Id,
    string DerivedTitle,
    string ProviderKind,
    Guid? ProviderInternalSystemId,
    Guid? ProviderExternalSystemId,
    string ProviderName,
    string ConsumerKind,
    Guid? ConsumerCompanySystemId,
    Guid? ConsumerEntityId,
    string ConsumerName,
    Guid ApiGroupId,
    string ApiGroupName,
    Guid RequirementTemplateId,
    string RequirementTemplateName,
    Guid ApprovalRouteId,
    string ApprovalRouteName,
    Guid ChangePolicyId,
    string ChangePolicyName,
    string PublicationOption,
    string State,
    Guid CreatedBy,
    string CreatedByName,
    DateTimeOffset CreatedAt,
    bool HasIntegration,
    Guid? IntegrationId,
    string? IntegrationReferenceCode,
    IReadOnlyList<AssignedDeveloperDto> AssignedDevelopers,
    IReadOnlyList<string> MyDocumentPermissions,
    string TraceId);

public sealed record CreateApiDocumentRequest : IValidatableObject
{
    public Guid? ProviderInternalSystemId { get; init; }
    public Guid? ProviderExternalSystemId { get; init; }
    public Guid? ConsumerCompanySystemId { get; init; }
    public Guid? ConsumerEntityId { get; init; }

    [Required]
    public Guid ApiGroupId { get; init; }

    [Required]
    public Guid RequirementTemplateId { get; init; }

    [Required]
    public Guid ApprovalRouteId { get; init; }

    [Required]
    public Guid ChangePolicyId { get; init; }

    [Required]
    [MaxLength(32)]
    public string PublicationOption { get; init; } = DocumentPublicationOptions.Manual;

    [MaxLength(30)]
    public string State { get; init; } = DocumentStates.Active;

    public IReadOnlyList<Guid>? DeveloperUserIds { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var hasInternalProvider = ProviderInternalSystemId is not null && ProviderInternalSystemId != Guid.Empty;
        var hasExternalProvider = ProviderExternalSystemId is not null && ProviderExternalSystemId != Guid.Empty;
        if (hasInternalProvider == hasExternalProvider)
        {
            yield return new ValidationResult(
                "يجب تحديد مزوّد واحد فقط (نظام داخلي أو مزوّد خارجي) / Exactly one provider (internal or external) is required.",
                [nameof(ProviderInternalSystemId), nameof(ProviderExternalSystemId)]);
        }

        var hasCompanyConsumer = ConsumerCompanySystemId is not null && ConsumerCompanySystemId != Guid.Empty;
        var hasEntityConsumer = ConsumerEntityId is not null && ConsumerEntityId != Guid.Empty;
        if (hasCompanyConsumer == hasEntityConsumer)
        {
            yield return new ValidationResult(
                "يجب تحديد مستهلك واحد فقط (نظام شركة أو جهة مستهلكة) / Exactly one consumer (company system or entity) is required.",
                [nameof(ConsumerCompanySystemId), nameof(ConsumerEntityId)]);
        }

        if (hasInternalProvider && hasCompanyConsumer && ProviderInternalSystemId == ConsumerCompanySystemId)
        {
            yield return new ValidationResult(
                "لا يمكن اختيار النظام الداخلي نفسه مزودًا ومستهلكًا في وثيقة API واحدة / The same internal system cannot be both provider and consumer.",
                [nameof(ConsumerCompanySystemId)]);
        }

        if (ApiGroupId == Guid.Empty)
            yield return new ValidationResult("مجموعة API مطلوبة.", [nameof(ApiGroupId)]);
        if (RequirementTemplateId == Guid.Empty)
            yield return new ValidationResult("قالب متطلبات الملفات مطلوب.", [nameof(RequirementTemplateId)]);
        if (ApprovalRouteId == Guid.Empty)
            yield return new ValidationResult("مسار الاعتماد مطلوب.", [nameof(ApprovalRouteId)]);
        if (ChangePolicyId == Guid.Empty)
            yield return new ValidationResult("سياسة التغيير مطلوبة.", [nameof(ChangePolicyId)]);

        if (!DocumentPublicationOptions.IsValid(PublicationOption))
            yield return new ValidationResult("خيار النشر غير صالح (MANUAL أو POST_APPROVAL).", [nameof(PublicationOption)]);

        if (!DocumentStates.IsValid(State))
            yield return new ValidationResult("حالة الوثيقة غير صالحة (DRAFT أو ACTIVE أو ARCHIVED).", [nameof(State)]);
    }
}

public sealed record UpdateApiDocumentRequest : IValidatableObject
{
    public Guid? ProviderInternalSystemId { get; init; }
    public Guid? ProviderExternalSystemId { get; init; }
    public Guid? ConsumerCompanySystemId { get; init; }
    public Guid? ConsumerEntityId { get; init; }
    public Guid? ApiGroupId { get; init; }
    public Guid? RequirementTemplateId { get; init; }
    public Guid? ApprovalRouteId { get; init; }
    public Guid? ChangePolicyId { get; init; }
    public string? PublicationOption { get; init; }
    public string? State { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var changingProvider = ProviderInternalSystemId is not null || ProviderExternalSystemId is not null;
        if (changingProvider)
        {
            var hasInternal = ProviderInternalSystemId is not null && ProviderInternalSystemId != Guid.Empty;
            var hasExternal = ProviderExternalSystemId is not null && ProviderExternalSystemId != Guid.Empty;
            if (hasInternal == hasExternal)
            {
                yield return new ValidationResult(
                    "عند تعديل المزود يجب تحديد مزود واحد فقط (داخلي أو خارجي).",
                    [nameof(ProviderInternalSystemId), nameof(ProviderExternalSystemId)]);
            }
        }

        var changingConsumer = ConsumerCompanySystemId is not null || ConsumerEntityId is not null;
        if (changingConsumer)
        {
            var hasComp = ConsumerCompanySystemId is not null && ConsumerCompanySystemId != Guid.Empty;
            var hasEnt = ConsumerEntityId is not null && ConsumerEntityId != Guid.Empty;
            if (hasComp == hasEnt)
            {
                yield return new ValidationResult(
                    "عند تعديل المستهلك يجب تحديد مستهلك واحد فقط (نظام شركة أو جهة مستهلكة).",
                    [nameof(ConsumerCompanySystemId), nameof(ConsumerEntityId)]);
            }
        }

        if (PublicationOption is not null && !DocumentPublicationOptions.IsValid(PublicationOption))
            yield return new ValidationResult("خيار النشر غير صالح (MANUAL أو POST_APPROVAL).", [nameof(PublicationOption)]);

        if (State is not null && !DocumentStates.IsValid(State))
            yield return new ValidationResult("حالة الوثيقة غير صالحة (DRAFT أو ACTIVE أو ARCHIVED).", [nameof(State)]);

        if (!changingProvider && !changingConsumer && ApiGroupId is null && RequirementTemplateId is null &&
            ApprovalRouteId is null && ChangePolicyId is null && PublicationOption is null && State is null)
        {
            yield return new ValidationResult("يجب تحديد حقل واحد على الأقل للتحديث.", [nameof(State)]);
        }
    }
}

public sealed record ReplaceDocumentDevelopersRequest
{
    public IReadOnlyList<Guid> DeveloperUserIds { get; init; } = [];
}

// ============================================================================
// I07 & U02 • Document Access Control Matrix (UI-21A)
// ============================================================================
public sealed record DocumentPermissionColumnDto(
    Guid Id,
    string Code,
    string ActionKey,
    string LabelAr,
    string LabelEn);

public sealed record DocumentRoleAccessRowDto(
    Guid RoleId,
    string RoleName,
    bool IsActive,
    IReadOnlyDictionary<string, string?> Decisions); // code -> "GRANT" | "REVOKE" | null

public sealed record DocumentUserEffectiveCellDto(
    bool Allowed,
    string ResolutionSource,
    string? ExplicitDecision);

public sealed record DocumentUserAccessRowDto(
    Guid UserId,
    string FullName,
    string Email,
    bool IsActive,
    IReadOnlyList<string> RoleNames,
    bool IsAssignedDeveloper,
    IReadOnlyDictionary<string, DocumentUserEffectiveCellDto> Cells);

public sealed record DocumentAccessMatrixResponse(
    Guid DocumentId,
    string DerivedTitle,
    string ApiGroupName,
    Guid CreatedBy,
    string CreatedByName,
    IReadOnlyList<DocumentPermissionColumnDto> Permissions,
    IReadOnlyList<DocumentRoleAccessRowDto> Roles,
    IReadOnlyList<DocumentUserAccessRowDto> Users,
    string TraceId);

public sealed record DocumentRolePermissionRuleInput(
    Guid RoleId,
    string PermissionCode,
    string Decision);

public sealed record DocumentUserPermissionRuleInput(
    Guid UserId,
    string PermissionCode,
    string Decision);

public sealed record ReplaceDocumentAccessRequest : IValidatableObject
{
    public bool UseAtomicPermissions { get; init; }
    public IReadOnlyList<DocumentRolePermissionRuleInput> RolePermissions { get; init; } = [];
    public IReadOnlyList<DocumentUserPermissionRuleInput> UserPermissions { get; init; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var rp in RolePermissions)
        {
            if (rp.RoleId == Guid.Empty || string.IsNullOrWhiteSpace(rp.PermissionCode) ||
                (!string.Equals(rp.Decision, "GRANT", StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(rp.Decision, "REVOKE", StringComparison.OrdinalIgnoreCase)))
            {
                yield return new ValidationResult(
                    "قواعد صلاحيات الأدوار يجب أن تحتوي دورًا صالحًا ورمز صلاحية والقرار GRANT أو REVOKE.",
                    [nameof(RolePermissions)]);
                yield break;
            }
        }

        foreach (var up in UserPermissions)
        {
            if (up.UserId == Guid.Empty || string.IsNullOrWhiteSpace(up.PermissionCode) ||
                (!string.Equals(up.Decision, "GRANT", StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(up.Decision, "REVOKE", StringComparison.OrdinalIgnoreCase)))
            {
                yield return new ValidationResult(
                    "قواعد صلاحيات المستخدمين يجب أن تحتوي مستخدمًا صالحًا ورمز صلاحية والقرار GRANT أو REVOKE.",
                    [nameof(UserPermissions)]);
                yield break;
            }
        }
    }
}

