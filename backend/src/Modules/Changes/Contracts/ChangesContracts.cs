using System.ComponentModel.DataAnnotations;

namespace ApiGovernance.Modules.Changes.Contracts;

public static class ChangeRequestStatusValues
{
    public const string Draft = "DRAFT";
    public const string Submitted = "SUBMITTED";
    public const string InReview = "IN_REVIEW";
    public const string Approved = "APPROVED";
    public const string Returned = "RETURNED";
    public const string Rejected = "REJECTED";
}

public sealed record CreateChangeRequestInput : IValidatableObject
{
    public Guid SourceRevisionId { get; init; }
    public Guid ChangeTypeId { get; init; }

    [Required(ErrorMessage = "سبب طلب التغيير مطلوب / Change reason is required.")]
    [MaxLength(2000)]
    public string Reason { get; init; } = string.Empty;

    [MaxLength(2000)]
    public string? ConsumerImpact { get; init; }

    public IReadOnlyList<Guid> AffectedSourceFileIds { get; init; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (SourceRevisionId == Guid.Empty)
        {
            yield return new ValidationResult("يجب تحديد المراجعة المعتمدة المرجعية / SourceRevisionId is required.", [nameof(SourceRevisionId)]);
        }
        if (ChangeTypeId == Guid.Empty)
        {
            yield return new ValidationResult("يجب تحديد نوع التغيير / ChangeTypeId is required.", [nameof(ChangeTypeId)]);
        }
        if (string.IsNullOrWhiteSpace(Reason))
        {
            yield return new ValidationResult("سبب طلب التغيير لا يمكن أن يكون فارغًا / Reason cannot be blank.", [nameof(Reason)]);
        }
        if (AffectedSourceFileIds.Count == 0 || AffectedSourceFileIds.Any(id => id == Guid.Empty))
        {
            yield return new ValidationResult("يجب تحديد ملف متأثر واحد على الأقل من المراجعة المعتمدة / At least one valid AffectedSourceFileId is required.", [nameof(AffectedSourceFileIds)]);
        }
    }
}

public sealed record UpdateChangeRequestInput : IValidatableObject
{
    public Guid ChangeTypeId { get; init; }

    [Required(ErrorMessage = "سبب طلب التغيير مطلوب / Change reason is required.")]
    [MaxLength(2000)]
    public string Reason { get; init; } = string.Empty;

    [MaxLength(2000)]
    public string? ConsumerImpact { get; init; }

    public IReadOnlyList<Guid> AffectedSourceFileIds { get; init; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ChangeTypeId == Guid.Empty)
        {
            yield return new ValidationResult("يجب تحديد نوع التغيير / ChangeTypeId is required.", [nameof(ChangeTypeId)]);
        }
        if (string.IsNullOrWhiteSpace(Reason))
        {
            yield return new ValidationResult("سبب طلب التغيير لا يمكن أن يكون فارغًا / Reason cannot be blank.", [nameof(Reason)]);
        }
        if (AffectedSourceFileIds.Count == 0 || AffectedSourceFileIds.Any(id => id == Guid.Empty))
        {
            yield return new ValidationResult("يجب تحديد ملف متأثر واحد على الأقل / At least one valid AffectedSourceFileId is required.", [nameof(AffectedSourceFileIds)]);
        }
    }
}

public sealed record AffectedSourceFileDto(
    Guid RevisionFileId,
    string OriginalName,
    string FileTypeName,
    string Sha256,
    long SizeBytes);

public sealed record ChangeRequestDto(
    Guid Id,
    Guid IntegrationId,
    Guid DocumentId,
    string ReferenceCode,
    Guid SourceRevisionId,
    int SourceRevisionNumber,
    string VersionLabel,
    Guid ChangeTypeId,
    string ChangeTypeName,
    Guid ApprovalRouteId,
    string ApprovalRouteName,
    string Reason,
    string? ConsumerImpact,
    string Status,
    bool IsEditable,
    Guid RequestedBy,
    string RequestedByName,
    DateTimeOffset CreatedAt,
    Guid? LatestWorkflowRunId,
    string? LatestWorkflowRunStatus,
    string? LatestDecisionNotes,
    IReadOnlyList<AffectedSourceFileDto> AffectedFiles,
    string TraceId);

public sealed record EligibleApprovedRevisionForChangeDto(
    Guid RevisionId,
    Guid ApiVersionId,
    string VersionLabel,
    int RevisionNumber,
    IReadOnlyList<AffectedSourceFileDto> AvailableFiles);

public sealed record AllowedChangeTypeOptionDto(
    Guid ChangeTypeId,
    string Name,
    Guid RouteId,
    string RouteName);

public sealed record IntegrationChangeRequestsWorkspaceResponse(
    Guid IntegrationId,
    Guid DocumentId,
    string ReferenceCode,
    string DerivedTitle,
    bool CanCreate,
    bool CanEdit,
    bool CanSubmit,
    IReadOnlyList<EligibleApprovedRevisionForChangeDto> EligibleRevisions,
    IReadOnlyList<AllowedChangeTypeOptionDto> AllowedChangeTypes,
    IReadOnlyList<ChangeRequestDto> Items,
    string TraceId);

public sealed record SubmitChangeRequestResponse(
    ChangeRequestDto ChangeRequest,
    Guid WorkflowRunId,
    string TraceId);
