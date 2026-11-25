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
