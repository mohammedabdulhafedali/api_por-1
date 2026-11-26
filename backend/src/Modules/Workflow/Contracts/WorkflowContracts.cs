using System.ComponentModel.DataAnnotations;
using ApiGovernance.Shared.Contracts;

namespace ApiGovernance.Modules.Workflow.Contracts;

public sealed record SubmitStageDecisionRequest : IValidatableObject
{
    [Required(ErrorMessage = "قرار المرحلة مطلوب / Decision is required.")]
    public string Decision { get; init; } = string.Empty;

    [MaxLength(4000, ErrorMessage = "يجب ألا تتجاوز الملاحظات 4000 حرف / Notes must not exceed 4000 characters.")]
    public string? Notes { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!WorkflowDecisionValues.IsValid(Decision))
        {
            yield return new ValidationResult(
                "قيمة القرار يجب أن تكون APPROVE أو RETURN أو REJECT / Decision must be APPROVE, RETURN, or REJECT.",
                [nameof(Decision)]);
            yield break;
        }

        var normalized = WorkflowDecisionValues.Normalize(Decision);
        if ((normalized is WorkflowDecisionValues.Return or WorkflowDecisionValues.Reject) &&
            string.IsNullOrWhiteSpace(Notes))
        {
            yield return new ValidationResult(
                "الملاحظات إلزامية عند اختيار الإعادة للعمل (RETURN) أو الرفض (REJECT) / Notes are mandatory when returning or rejecting.",
                [nameof(Notes)]);
        }
    }
}

public sealed record WorkflowDecisionDto(
    Guid Id,
    Guid RunStageId,
    Guid ActorUserId,
    string ActorUserName,
    string Decision,
    string? Notes,
    DateTimeOffset DecidedAt);

public sealed record WorkflowRunStageDto(
    Guid Id,
    Guid RunId,
    int Position,
    string StageNameSnapshot,
    Guid ResponsibleRoleIdSnapshot,
    string ResponsibleRoleName,
    string Status,
    WorkflowDecisionDto? Decision);

public sealed record WorkflowRunDetailsDto(
    Guid Id,
    Guid RouteId,
    string RouteName,
    Guid? RevisionId,
    Guid? ChangeRequestId,
    Guid IntegrationId,
    Guid DocumentId,
    string ReferenceCode,
    string DerivedTitle,
    string? VersionLabel,
    int? RevisionNumber,
    string? RevisionStatus,
    string Status,
    Guid SubmittedBy,
    string SubmittedByName,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    Guid? ActiveStageId,
    string? ActiveStageName,
    string? ActiveStageRoleName,
    bool CanCurrentUserDecideActiveStage,
    IReadOnlyList<WorkflowRunStageDto> Stages,
    string TraceId,
    IReadOnlyList<string>? AllowedDecisions = null);

public sealed record WorkflowInboxItemDto(
    Guid RunId,
    Guid StageId,
    int StagePosition,
    string StageNameSnapshot,
    Guid ResponsibleRoleIdSnapshot,
    string ResponsibleRoleName,
    Guid IntegrationId,
    Guid DocumentId,
    string ReferenceCode,
    string DerivedTitle,
    Guid? RevisionId,
    Guid? ChangeRequestId,
    string? VersionLabel,
    int? RevisionNumber,
    string RevisionStatus,
    Guid SubmittedBy,
    string SubmittedByName,
    DateTimeOffset StartedAt,
    bool CanDecide);

public sealed record WorkflowInboxResponse(
    IReadOnlyList<WorkflowInboxItemDto> Items,
    string TraceId);

public sealed record WorkflowDecisionHistoryEntryDto(
    Guid RunId,
    Guid StageId,
    int StagePosition,
    string StageNameSnapshot,
    string ResponsibleRoleName,
    string RunStatus,
    Guid? RevisionId,
    int? RevisionNumber,
    DateTimeOffset RunStartedAt,
    DateTimeOffset? RunEndedAt,
    WorkflowDecisionDto Decision);

public sealed record WorkflowHistoryResponse(
    Guid RunId,
    Guid? RevisionId,
    Guid? ChangeRequestId,
    IReadOnlyList<WorkflowDecisionHistoryEntryDto> Entries,
    string TraceId);

public sealed record SubmitStageDecisionResponse(
    WorkflowRunDetailsDto Run,
    WorkflowDecisionDto Decision,
    string OutcomeStatus,
    string TraceId);
