using System.ComponentModel.DataAnnotations;

namespace ApiGovernance.Modules.PublishingPortal.Contracts;

public static class PublishingPermissionCodes
{
    public const string PublicationsView = "publications.view";
    public const string PublicationsCreate = "publications.create";
    public const string PublicationsWithdraw = "publications.withdraw";
    public const string VersionsUsageView = "versions.usage.view";
    public const string VersionsUsageManage = "versions.usage.manage";
}

public static class PublicationStateValues
{
    public const string Published = "PUBLISHED";
    public const string Withdrawn = "WITHDRAWN";
}

public sealed record PublicationFileDto(
    Guid Id,
    Guid RevisionFileId,
    Guid FileTypeId,
    string FileTypeCode,
    string FileTypeName,
    string OriginalName,
    long SizeBytes,
    string MediaType,
    string Sha256,
    string ScanStatus);

public sealed record PublicationDto(
    Guid Id,
    Guid IntegrationId,
    Guid ApiVersionId,
    string VersionLabel,
    Guid RevisionId,
    int RevisionNumber,
    string State,
    Guid PublishedBy,
    string PublishedByName,
    DateTimeOffset PublishedAt,
    DateTimeOffset? WithdrawnAt,
    IReadOnlyList<PublicationFileDto> Files);

public sealed record ConsumerVersionUsageDto(
    Guid Id,
    Guid IntegrationId,
    Guid ApiVersionId,
    string VersionLabel,
    DateTimeOffset ValidFrom,
    DateTimeOffset? ValidTo,
    Guid AssignedBy,
    string AssignedByName);

public sealed record IntegrationPublicationsResponse(
    Guid IntegrationId,
    Guid DocumentId,
    string DocumentTitle,
    string PublicationOption,
    IReadOnlyList<PublicationDto> Publications,
    ConsumerVersionUsageDto? ActiveConsumerVersion,
    bool CanCreatePublication,
    bool CanWithdrawPublication,
    bool CanManageConsumerVersion,
    string TraceId);

public sealed record ConsumerVersionResponse(
    Guid IntegrationId,
    ConsumerVersionUsageDto? ActiveUsage,
    IReadOnlyList<ConsumerVersionUsageDto> History,
    bool CanManage,
    string TraceId);

public sealed record CreatePublicationRequest : IValidatableObject
{
    [Required]
    public IReadOnlyList<Guid> RevisionFileIds { get; init; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (RevisionFileIds is null || RevisionFileIds.Count == 0 || RevisionFileIds.All(id => id == Guid.Empty))
        {
            yield return new ValidationResult(
                "يجب تحديد ملف سليم واحد على الأقل للنشر الانتقائي / At least one CLEAN revision file must be selected.",
                [nameof(RevisionFileIds)]);
        }
    }
}

public sealed record SetConsumerVersionRequest : IValidatableObject
{
    [Required]
    public Guid ApiVersionId { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ApiVersionId == Guid.Empty)
        {
            yield return new ValidationResult(
                "معرف إصدار API مطلوب / ApiVersionId is required.",
                [nameof(ApiVersionId)]);
        }
    }
}

public sealed record PortalIntegrationItemDto(
    Guid IntegrationId,
    string ReferenceCode,
    Guid DocumentId,
    string DocumentTitle,
    string ProviderName,
    string ApiGroupName,
    string? ActiveConsumerVersionLabel,
    Guid LatestPublicationId,
    string LatestPublicationVersionLabel,
    int LatestPublicationRevisionNumber,
    DateTimeOffset LatestPublishedAt,
    int PublishedFilesCount);

public sealed record PortalIntegrationsResponse(
    Guid ConsumerEntityId,
    string ConsumerEntityName,
    IReadOnlyList<PortalIntegrationItemDto> Integrations,
    string TraceId);

public sealed record PortalPublicationFilesResponse(
    Guid PublicationId,
    Guid IntegrationId,
    string ReferenceCode,
    string DocumentTitle,
    string VersionLabel,
    int RevisionNumber,
    DateTimeOffset PublishedAt,
    IReadOnlyList<PublicationFileDto> Files,
    string TraceId);
