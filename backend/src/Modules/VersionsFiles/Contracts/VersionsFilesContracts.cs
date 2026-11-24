using System.ComponentModel.DataAnnotations;

namespace ApiGovernance.Modules.VersionsFiles.Contracts;

public static class FileScanStatusValues
{
    public const string Clean = "CLEAN";
    public const string PendingScan = "PENDING_SCAN";
    public const string Infected = "INFECTED";
    public const string Invalid = "INVALID";
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
    int UploadedPendingScanCount);

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
    string TraceId);

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
    string TraceId);

