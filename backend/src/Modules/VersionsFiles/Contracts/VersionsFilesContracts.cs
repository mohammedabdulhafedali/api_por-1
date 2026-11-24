using System.ComponentModel.DataAnnotations;

namespace ApiGovernance.Modules.VersionsFiles.Contracts;

public static class FileScanStatusValues
{
    public const string Clean = "CLEAN";
    public const string PendingScan = "PENDING_SCAN";
    public const string Infected = "INFECTED";
    public const string Invalid = "INVALID";
}

public static class RevisionStatusValues
{
    public const string Draft = "DRAFT";
    public const string Submitted = "SUBMITTED";
    public const string InReview = "IN_REVIEW";
    public const string Approved = "APPROVED";
    public const string Returned = "RETURNED";
    public const string Rejected = "REJECTED";
    public const string Superseded = "SUPERSEDED";
}

public static class WordDerivativeStatusValues
{
    public const string Verified = "VERIFIED";
    public const string PendingScan = "PENDING_SCAN";
    public const string NoRecognizedFields = "NO_RECOGNIZED_FIELDS";
    public const string Failed = "FAILED";
}

public sealed record StagedFileWriteResult(
    string StagedFilePath,
    string Sha256,
    long SizeBytes);

public sealed record ScannerRuntimeInfo(
    bool IsConfigured,
    string Provider,
    string SummaryAr,
    string SummaryEn);

public sealed record FileScanVerdict(
    string ScanStatus,
    string ReasonCode,
    string MessageAr,
    string MessageEn,
    string? ThreatName = null);

public sealed record ApiVersionDto(
    Guid Id,
    Guid IntegrationId,
    string VersionLabel,
    bool IsArchived,
    DateTimeOffset CreatedAt,
    int RevisionsCount,
    string? LatestRevisionStatus,
    string TraceId);

public sealed record IntegrationVersionsResponse(
    Guid IntegrationId,
    Guid DocumentId,
    string ReferenceCode,
    string DerivedTitle,
    string ProviderName,
    string ConsumerName,
    string ApiGroupName,
    bool CanCreateVersion,
    bool IsCurrentUserAssignedDeveloper,
    IReadOnlyList<ApiVersionDto> Versions,
    string TraceId);

public sealed record CreateApiVersionRequest : IValidatableObject
{
    [Required(ErrorMessage = "رقم/تسمية الإصدار مطلوبة / VersionLabel is required.")]
    [MaxLength(120, ErrorMessage = "يجب ألا يتجاوز رقم الإصدار 120 حرفًا / VersionLabel must not exceed 120 characters.")]
    public string VersionLabel { get; init; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(VersionLabel))
        {
            yield return new ValidationResult(
                "رقم/تسمية الإصدار لا يمكن أن يكون فارغًا / VersionLabel cannot be blank.",
                [nameof(VersionLabel)]);
        }
    }
}

public sealed record CreateDocumentRevisionRequest
{
    public Guid? BaseRevisionId { get; init; }
    public Guid? ChangeRequestId { get; init; }
}

public sealed record RevisionRequirementProgressDto(
    Guid FileTypeId,
    string FileTypeName,
    string CatalogCode,
    IReadOnlyList<string> Extensions,
    long MaxSizeBytes,
    string RequirementMode,
    int MinCount,
    int MaxCount,
    int UploadedCleanCount,
    int UploadedPendingScanCount,
    bool IsSatisfied,
    int MissingRequiredCount,
    bool ExceedsMaxCount);

public sealed record DocumentRevisionDto(
    Guid Id,
    Guid ApiVersionId,
    string VersionLabel,
    int RevisionNumber,
    Guid? BaseRevisionId,
    Guid? ChangeRequestId,
    string Status,
    Guid CreatedBy,
    string CreatedByName,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ApprovedAt,
    int FilesCount,
    int PendingScanCount,
    IReadOnlyList<RevisionRequirementProgressDto> Requirements,
    string TraceId,
    bool IsProtected,
    bool IsReadyForSubmission,
    int MissingRequiredTypesCount,
    IReadOnlyList<string> SubmissionBlockersAr,
    IReadOnlyList<string> SubmissionBlockersEn,
    Guid? LatestWorkflowRunId = null,
    string? LatestWorkflowRunStatus = null,
    string? LatestDecisionNotes = null);

public sealed record RevisionFileDto(
    Guid Id,
    Guid RevisionId,
    Guid FileTypeId,
    string FileTypeName,
    string CatalogCode,
    Guid ObjectId,
    string OriginalName,
    string Sha256,
    long SizeBytes,
    string? MediaType,
    string ScanStatus,
    bool IsAvailableForUse,
    bool IsQuarantined,
    Guid? SourceRevisionFileId,
    Guid UploadedBy,
    string UploadedByName,
    DateTimeOffset UploadedAt,
    string TraceId);

public sealed record FileScanStatusDto(
    Guid ObjectId,
    string ScanStatus,
    bool IsAvailableForUse,
    bool IsQuarantined,
    string Sha256,
    long SizeBytes,
    string? MediaType,
    string StorageTier,
    DateTimeOffset CreatedAt,
    string TraceId);

public sealed record ApprovedChangeRequestSummaryDto(
    Guid Id,
    Guid SourceRevisionId,
    Guid ChangeTypeId,
    string ChangeTypeName,
    string Reason,
    IReadOnlyList<Guid> AffectedSourceFileIds,
    DateTimeOffset CreatedAt);

public sealed record WordFieldUpdateEntryDto(
    string FieldKey,
    string Mechanism,
    string ValueApplied,
    string Location);

public sealed record WordDerivativeRecordDto(
    Guid Id,
    Guid RevisionId,
    Guid RevisionFileId,
    Guid SourceObjectId,
    string SourceSha256,
    Guid? DerivedObjectId,
    string? DerivedSha256,
    long? DerivedSizeBytes,
    string Status,
    string TemplateKindDetected,
    int FieldsUpdatedCount,
    IReadOnlyList<WordFieldUpdateEntryDto> MatchedFields,
    IReadOnlyList<string> ValidationErrors,
    string? ErrorMessage,
    bool IsPublishable,
    Guid CreatedBy,
    string CreatedByName,
    DateTimeOffset CreatedAt,
    string TraceId);

public sealed record RevisionDetailsResponse(
    DocumentRevisionDto Revision,
    Guid IntegrationId,
    Guid DocumentId,
    string ReferenceCode,
    string DerivedTitle,
    bool CanSubmit,
    bool CanCreateWorkingCopy,
    bool CanUploadFiles,
    bool CanDeleteFiles,
    bool CanDownloadFiles,
    IReadOnlyList<RevisionFileDto> Files,
    IReadOnlyList<WordDerivativeRecordDto> WordDerivatives,
    string TraceId);

public sealed record SubmitRevisionResponse(
    DocumentRevisionDto Revision,
    int CleanFilesCount,
    string TraceId,
    Guid? WorkflowRunId = null);

public sealed record IntegrationFilesWorkspaceResponse(
    Guid IntegrationId,
    Guid DocumentId,
    string ReferenceCode,
    string DerivedTitle,
    bool CanCreateDraftRevision,
    bool CanUploadFiles,
    bool CanDeleteFiles,
    bool CanDownloadFiles,
    bool IsCurrentUserAssignedDeveloper,
    bool ScannerConfigured,
    string ScannerSummaryAr,
    string ScannerSummaryEn,
    IReadOnlyList<ApiVersionDto> Versions,
    Guid? SelectedVersionId,
    IReadOnlyList<DocumentRevisionDto> Revisions,
    DocumentRevisionDto? SelectedRevision,
    IReadOnlyList<RevisionFileDto> Files,
    string TraceId,
    bool CanSubmitRevision,
    bool CanCreateWorkingCopy,
    IReadOnlyList<ApprovedChangeRequestSummaryDto> ApprovedChangeRequests,
    IReadOnlyList<WordDerivativeRecordDto> WordDerivatives);
