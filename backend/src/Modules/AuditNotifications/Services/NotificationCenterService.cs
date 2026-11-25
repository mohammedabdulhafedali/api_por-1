using System.Security.Claims;
using ApiGovernance.Modules.AuditNotifications.Contracts;
using ApiGovernance.Shared.Contracts;
using ApiGovernance.Shared.Persistence;
using ApiGovernance.Shared.Persistence.Entities;
using ApiGovernance.Shared.Security;
using Microsoft.EntityFrameworkCore;

namespace ApiGovernance.Modules.AuditNotifications.Services;

public interface INotificationQueryService
{
    Task<NotificationsListResponse> GetMyNotificationsAsync(
        ClaimsPrincipal actorPrincipal,
        bool unreadOnly,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<NotificationItemDto> MarkAsReadAsync(
        ClaimsPrincipal actorPrincipal,
        Guid notificationId,
        CancellationToken cancellationToken = default);
}

public sealed class NotificationCenterService(
    ApiGovernanceDbContext dbContext,
    IServerAuthorizationService serverAuthorization)
    : INotificationQueryService, IInternalNotificationService
{
    public async Task NotifyStageActivatedAsync(
        Guid roleId,
        Guid? excludeUserId,
        string resourceKind,
        Guid resourceId,
        CancellationToken cancellationToken = default)
    {
        var memberIds = await (
            from ur in dbContext.UserRoles.AsNoTracking()
            join u in dbContext.Users.AsNoTracking() on ur.UserId equals u.Id
            join r in dbContext.Roles.AsNoTracking() on ur.RoleId equals r.Id
            where ur.RoleId == roleId && u.IsActive && r.IsActive
            select u.Id)
            .Distinct()
            .ToListAsync(cancellationToken);

        var recipients = excludeUserId.HasValue
            ? memberIds.Where(id => id != excludeUserId.Value).ToList()
            : memberIds;

        // If the only active member in the responsible role is the submitter (common in single-admin/bootstrap setup),
        // still notify that member so inbox action visibility stays consistent.
        if (recipients.Count == 0 && memberIds.Count > 0)
        {
            recipients = memberIds;
        }

        if (recipients.Count == 0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var entries = recipients.Select(userId => new NotificationEntity
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Kind = "WORKFLOW_STAGE_PENDING",
            ResourceKind = resourceKind,
            ResourceId = resourceId,
            CreatedAt = now,
            ReadAt = null
        });

        dbContext.Notifications.AddRange(entries);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task NotifyWorkflowOutcomeAsync(
        Guid targetUserId,
        string kind,
        string resourceKind,
        Guid resourceId,
        CancellationToken cancellationToken = default)
    {
        var isActive = await dbContext.Users.AsNoTracking()
            .AnyAsync(u => u.Id == targetUserId && u.IsActive, cancellationToken);
        if (!isActive)
        {
            return;
        }

        dbContext.Notifications.Add(new NotificationEntity
        {
            Id = Guid.NewGuid(),
            UserId = targetUserId,
            Kind = kind,
            ResourceKind = resourceKind,
            ResourceId = resourceId,
            CreatedAt = DateTimeOffset.UtcNow,
            ReadAt = null
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<NotificationsListResponse> GetMyNotificationsAsync(
        ClaimsPrincipal actorPrincipal,
        bool unreadOnly,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var actor = await serverAuthorization.RequireActiveAuthenticatedUserAsync(actorPrincipal, cancellationToken);

        var query = dbContext.Notifications.AsNoTracking()
            .Where(n => n.UserId == actor.Id);

        var unreadCount = await query.CountAsync(n => n.ReadAt == null, cancellationToken);

        if (unreadOnly)
        {
            query = query.Where(n => n.ReadAt == null);
        }

        var rows = await query
            .OrderByDescending(n => n.CreatedAt)
            .Take(50)
            .ToListAsync(cancellationToken);

        var dtos = rows.Select(MapToDto).ToList();
        return new NotificationsListResponse(unreadCount, dtos, traceId);
    }

    public async Task<NotificationItemDto> MarkAsReadAsync(
        ClaimsPrincipal actorPrincipal,
        Guid notificationId,
        CancellationToken cancellationToken = default)
    {
        var actor = await serverAuthorization.RequireActiveAuthenticatedUserAsync(actorPrincipal, cancellationToken);

        var entity = await dbContext.Notifications
            .FirstOrDefaultAsync(n => n.Id == notificationId && n.UserId == actor.Id, cancellationToken);

        if (entity is null)
        {
            throw ApiContractException.NotFound(
                messageAr: "الإشعار المطلوب غير موجود ضمن حسابك.",
                messageEn: "Notification was not found for the current user.");
        }

        if (!entity.ReadAt.HasValue)
        {
            entity.ReadAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return MapToDto(entity);
    }

    private static NotificationItemDto MapToDto(NotificationEntity n)
    {
        var (summaryAr, summaryEn) = (n.Kind.ToUpperInvariant(), n.ResourceKind.ToUpperInvariant()) switch
        {
            ("WORKFLOW_STAGE_PENDING", "CHANGE_REQUEST") =>
                ("توجد مرحلة اعتماد نشطة لطلب تغيير بانتظار قرارك.", "An active change-request workflow stage awaits your decision."),
            ("WORKFLOW_STAGE_PENDING", _) =>
                ("توجد مرحلة اعتماد نشطة لحزمة توثيق بانتظار قرارك.", "An active document-revision workflow stage awaits your decision."),
            ("WORKFLOW_APPROVED", "CHANGE_REQUEST") =>
                ("تم اعتماد طلب التغيير بالكامل ويمكنك فتح نسخة عمل.", "Your change request was APPROVED and can now open a working copy."),
            ("WORKFLOW_APPROVED", _) =>
                ("تم اعتماد مراجعة حزمة التوثيق بالكامل (APPROVED).", "Your documentation revision workflow was fully APPROVED."),
            ("WORKFLOW_RETURNED", "CHANGE_REQUEST") =>
                ("أُعيد طلب التغيير للعمل مع ملاحظات من المراجع.", "Your change request was RETURNED for rework with reviewer notes."),
            ("WORKFLOW_RETURNED", _) =>
                ("أُعيدت المراجعة للعمل مع ملاحظات من المراجع (RETURNED).", "Your documentation revision was RETURNED for rework."),
            ("WORKFLOW_REJECTED", "CHANGE_REQUEST") =>
                ("تم رفض طلب التغيير من مسار الاعتماد (REJECTED).", "Your change request was REJECTED by the approval workflow."),
            ("WORKFLOW_REJECTED", _) =>
                ("تم رفض مراجعة حزمة التوثيق (REJECTED).", "Your documentation revision was REJECTED."),
            _ => ("تحديث سير عمل داخلي جديد.", "New internal workflow notification.")
        };

        return new NotificationItemDto(
            n.Id,
            n.Kind,
            n.ResourceKind,
            n.ResourceId,
            summaryAr,
            summaryEn,
            n.ReadAt.HasValue,
            n.CreatedAt,
            n.ReadAt);
    }
}
