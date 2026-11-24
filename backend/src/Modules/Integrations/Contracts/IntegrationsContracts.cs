using System.ComponentModel.DataAnnotations;

namespace ApiGovernance.Modules.Integrations.Contracts;

public sealed record IntegrationPartyOptionDto(
    Guid Id,
    string FullName,
    string Email);

public sealed record EligibleIntegrationDocumentDto(
    Guid DocumentId,
    string DerivedTitle,
    string ProviderKind,
    string ProviderName,
    string ConsumerKind,
    string ConsumerName,
    Guid ApiGroupId,
    string ApiGroupName,
    string State);

public sealed record IntegrationDeveloperDto(
    Guid UserId,
    string FullName,
    string Email,
    bool IsActive,
    bool HasDocView,
    bool HasDocVersionManage,
    bool HasDocFilesManage,
    bool HasDocChange,
    bool HasDocAssign);

public sealed record IntegrationDto(
    Guid Id,
    Guid DocumentId,
    string ReferenceCode,
    string DerivedTitle,
    string ProviderKind,
    string ProviderName,
    string ConsumerKind,
    string ConsumerName,
    Guid ApiGroupId,
    string ApiGroupName,
    string RequirementTemplateName,
    string ApprovalRouteName,
    string ChangePolicyName,
    string PublicationOption,
    string DocumentState,
    Guid CreatedBy,
    string CreatedByName,
    DateTimeOffset CreatedAt,
    int VersionsCount,
    bool IsCurrentUserAssignedDeveloper,
    IReadOnlyList<IntegrationDeveloperDto> AssignedDevelopers,
    IReadOnlyList<string> MyDocumentPermissions,
    string TraceId);

public sealed record IntegrationRequirementRuleSummaryDto(
    Guid FileTypeId,
    string FileTypeName,
    string CatalogCode,
    IReadOnlyList<string> Extensions,
    string RequirementMode,
    int MinCount,
    int MaxCount);

public sealed record IntegrationApprovalStageSummaryDto(
    int Position,
    string StageName,
    Guid ResponsibleRoleId,
    string ResponsibleRoleName);

public sealed record IntegrationChangeTypeSummaryDto(
    Guid ChangeTypeId,
    string ChangeTypeName,
    Guid RouteId,
    string RouteName);

public sealed record IntegrationAuditEventSummaryDto(
    Guid Id,
    string Operation,
    string ResourceKind,
    string Result,
    Guid? ActorUserId,
    string ActorName,
    DateTimeOffset RecordedAt);

public sealed record IntegrationDetailsDto(
    IntegrationDto Summary,
    IReadOnlyList<IntegrationDeveloperDto> DocumentDevelopers,
    IReadOnlyList<IntegrationRequirementRuleSummaryDto> RequirementRules,
    IReadOnlyList<IntegrationApprovalStageSummaryDto> ApprovalStages,
    IReadOnlyList<IntegrationChangeTypeSummaryDto> AllowedChangeTypes,
    int RevisionsCount,
    int ChangeRequestsCount,
    int PublicationsCount,
    IReadOnlyList<IntegrationAuditEventSummaryDto> RecentAuditEvents,
    string TraceId);

public sealed record IntegrationDevelopersResponse(
    Guid IntegrationId,
    Guid DocumentId,
    string ReferenceCode,
    string DerivedTitle,
    bool IsCurrentUserAssignedToIntegration,
    bool IsCurrentUserAssignedToDocument,
    IReadOnlyList<IntegrationDeveloperDto> IntegrationDevelopers,
    IReadOnlyList<IntegrationDeveloperDto> DocumentDevelopers,
    string TraceId);

public sealed record ReplaceIntegrationDevelopersRequest
{
    public IReadOnlyList<Guid> DeveloperUserIds { get; init; } = [];
}

public sealed record CreateIntegrationRequest : IValidatableObject
{
    [Required]
    public Guid DocumentId { get; init; }

    [MaxLength(60)]
    public string? ReferenceCode { get; init; }

    public IReadOnlyList<Guid>? DeveloperUserIds { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (DocumentId == Guid.Empty)
        {
            yield return new ValidationResult(
                "يجب اختيار وثيقة API لإنشاء التكامل / DocumentId is required.",
                [nameof(DocumentId)]);
        }

        if (!string.IsNullOrWhiteSpace(ReferenceCode))
        {
            var trimmed = ReferenceCode.Trim();
            if (trimmed.Length < 3 || trimmed.Length > 60)
            {
                yield return new ValidationResult(
                    "الرمز المرجعي للتكامل يجب أن يكون بين 3 و60 حرفًا.",
                    [nameof(ReferenceCode)]);
            }

            if (!System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^[A-Za-z0-9\-_]+$"))
            {
                yield return new ValidationResult(
                    "الرمز المرجعي للتكامل يقبل الحروف الإنجليزية والأرقام والشرطة فقط.",
                    [nameof(ReferenceCode)]);
            }
        }
    }
}
