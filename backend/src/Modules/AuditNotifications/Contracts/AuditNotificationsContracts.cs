namespace ApiGovernance.Modules.AuditNotifications.Contracts;

public sealed record NotificationItemDto(
    Guid Id,
    string Kind,
    string ResourceKind,
    Guid ResourceId,
    string SummaryAr,
    string SummaryEn,
    bool IsRead,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReadAt);

public sealed record NotificationsListResponse(
    int UnreadCount,
    IReadOnlyList<NotificationItemDto> Items,
    string TraceId);

public sealed record AuditEventListItemDto(
    Guid Id,
    Guid? ActorUserId,
    string? ActorName,
    string? ActorEmail,
    string Operation,
    string ResourceKind,
    Guid? ResourceId,
    string Result,
    string? ChangeSummaryJson,
    DateTimeOffset RecordedAt);

public sealed record PagedAuditEventsResponse(
    int TotalCount,
    int Page,
    int PageSize,
    IReadOnlyList<AuditEventListItemDto> Items,
    IReadOnlyList<string> DistinctOperations,
    IReadOnlyList<string> DistinctResourceKinds,
    string TraceId);

public sealed record GlobalSearchResultItemDto(
    string ResultKind,
    Guid Id,
    Guid DocumentId,
    Guid? IntegrationId,
    string? ReferenceCode,
    string Title,
    string ProviderName,
    string ConsumerName,
    string ApiGroupName,
    string State,
    string? LatestVersionLabel,
    bool HasActivePublication,
    DateTimeOffset CreatedAt);

public sealed record GlobalSearchResponse(
    string Query,
    int TotalCount,
    IReadOnlyList<GlobalSearchResultItemDto> Items,
    string TraceId);

public sealed record DashboardPendingStageDto(
    Guid RunId,
    Guid StageId,
    string StageName,
    string TargetKind,
    Guid TargetId,
    Guid DocumentId,
    Guid IntegrationId,
    string Title,
    string ReferenceCode,
    string RoleName,
    DateTimeOffset StartedAt);

public sealed record DashboardRecentAuditDto(
    Guid Id,
    string? ActorName,
    string Operation,
    string ResourceKind,
    Guid? ResourceId,
    string Result,
    DateTimeOffset RecordedAt);

public sealed record DashboardSummaryResponse(
    int VisibleDocumentsCount,
    int VisibleIntegrationsCount,
    int PendingMyApprovalStagesCount,
    int OpenChangeRequestsCount,
    int PublishedRevisionsCount,
    int UnreadNotificationsCount,
    bool CanViewDocuments,
    bool CanViewIntegrations,
    bool CanReviewWorkflows,
    bool CanViewPublications,
    bool CanViewAudit,
    bool CanViewPortal,
    IReadOnlyList<DashboardPendingStageDto> PendingApprovalStages,
    IReadOnlyList<DashboardRecentAuditDto> RecentAuditEvents,
    string TraceId);
