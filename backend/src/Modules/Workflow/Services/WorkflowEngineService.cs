using System.Security.Claims;
using ApiGovernance.Modules.Workflow.Contracts;
using ApiGovernance.Shared.Contracts;
using ApiGovernance.Shared.Persistence;
using ApiGovernance.Shared.Persistence.Entities;
using ApiGovernance.Shared.Security;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ApiGovernance.Modules.Workflow.Services;

public interface IWorkflowEngineService
{
    Task<WorkflowInboxResponse> GetInboxAsync(
        ClaimsPrincipal actorPrincipal,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<WorkflowRunDetailsDto> GetWorkflowRunDetailsAsync(
        ClaimsPrincipal actorPrincipal,
        Guid runId,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<WorkflowHistoryResponse> GetWorkflowHistoryAsync(
        ClaimsPrincipal actorPrincipal,
        Guid runId,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<SubmitStageDecisionResponse> SubmitStageDecisionAsync(
        ClaimsPrincipal actorPrincipal,
        Guid runId,
        Guid stageId,
        SubmitStageDecisionRequest request,
        string traceId,
        CancellationToken cancellationToken = default);
}

public sealed class WorkflowEngineService(
    ApiGovernanceDbContext dbContext,
    IServerAuthorizationService serverAuthorization,
    IDocumentAuthorizationService documentAuthorization,
    IInternalNotificationService internalNotificationService)
    : IWorkflowEngineService, IWorkflowRunStarter
{
    public Task<Guid> StartRevisionWorkflowRunAsync(
        Guid revisionId,
        Guid routeId,
        Guid actorUserId,
        Guid integrationId,
        CancellationToken cancellationToken = default)
        => StartWorkflowRunCoreAsync(
            revisionId: revisionId,
            changeRequestId: null,
            routeId: routeId,
            actorUserId: actorUserId,
            integrationId: integrationId,
            cancellationToken: cancellationToken);

    public Task<Guid> StartChangeRequestWorkflowRunAsync(
        Guid changeRequestId,
        Guid routeId,
        Guid actorUserId,
        Guid integrationId,
        CancellationToken cancellationToken = default)
        => StartWorkflowRunCoreAsync(
            revisionId: null,
            changeRequestId: changeRequestId,
            routeId: routeId,
            actorUserId: actorUserId,
            integrationId: integrationId,
            cancellationToken: cancellationToken);

    private async Task<Guid> StartWorkflowRunCoreAsync(
        Guid? revisionId,
        Guid? changeRequestId,
        Guid routeId,
        Guid actorUserId,
        Guid integrationId,
        CancellationToken cancellationToken)
    {
        var routeStages = await dbContext.ApprovalRouteStages.AsNoTracking()
            .Where(s => s.RouteId == routeId)
            .OrderBy(s => s.Position)
            .ToListAsync(cancellationToken);

        if (routeStages.Count == 0)
        {
            throw ApiContractException.UnprocessableEntity(
                messageAr: "لا يمكن بدء مسار الاعتماد لأن المسار المرتبط لا يحتوي على مراحل.",
                messageEn: "Cannot start workflow run because the linked approval route has no configured stages.");
        }

        var hasActiveRun = revisionId.HasValue
            ? await dbContext.WorkflowRuns.AsNoTracking()
                .AnyAsync(r => r.RevisionId == revisionId && r.Status == WorkflowRunStatusValues.InProgress, cancellationToken)
            : await dbContext.WorkflowRuns.AsNoTracking()
                .AnyAsync(r => r.ChangeRequestId == changeRequestId && r.Status == WorkflowRunStatusValues.InProgress, cancellationToken);

        if (hasActiveRun)
        {
            throw ApiContractException.Conflict(
                messageAr: "يوجد تشغيل مسار اعتماد نشط بالفعل لهذا العنصر.",
                messageEn: "An active workflow run already exists for this target.");
        }

        var now = DateTimeOffset.UtcNow;
        var run = new WorkflowRunEntity
        {
            Id = Guid.NewGuid(),
            RouteId = routeId,
            RevisionId = revisionId,
            ChangeRequestId = changeRequestId,
            Status = WorkflowRunStatusValues.InProgress,
            SubmittedBy = actorUserId,
            StartedAt = now,
            EndedAt = null
        };

        var snapshotStages = routeStages.Select((s, index) => new WorkflowRunStageEntity
        {
            Id = Guid.NewGuid(),
            RunId = run.Id,
            Position = s.Position,
            StageNameSnapshot = s.StageName,
            ResponsibleRoleIdSnapshot = s.ResponsibleRoleId,
            Status = index == 0 ? WorkflowStageStatusValues.Active : WorkflowStageStatusValues.Pending
        }).ToList();

        dbContext.WorkflowRuns.Add(run);
        dbContext.WorkflowRunStages.AddRange(snapshotStages);
        await dbContext.SaveChangesAsync(cancellationToken);

        var resourceKind = changeRequestId.HasValue ? "CHANGE_REQUEST" : "DOCUMENT_REVISION";
        var resourceId = changeRequestId ?? revisionId!.Value;

        await internalNotificationService.NotifyStageActivatedAsync(
            snapshotStages[0].ResponsibleRoleIdSnapshot,
            actorUserId,
            resourceKind,
            resourceId,
            cancellationToken);

        await serverAuthorization.WriteAuditEventAsync(
            actorUserId,
            "WORKFLOW_RUN_CREATED",
            "WORKFLOW_RUN",
            run.Id,
            "SUCCESS",
            new
            {
                integrationId,
                revisionId,
                changeRequestId,
                routeId,
                stagesCount = snapshotStages.Count,
                firstStageId = snapshotStages[0].Id,
                firstStageRoleId = snapshotStages[0].ResponsibleRoleIdSnapshot
            },
            cancellationToken);

        return run.Id;
    }

    public async Task<WorkflowInboxResponse> GetInboxAsync(
        ClaimsPrincipal actorPrincipal,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var actor = await serverAuthorization.RequireActiveAuthenticatedUserAsync(actorPrincipal, cancellationToken);

        // Multi-role eligibility across ALL active roles assigned to the user (ADR-01).
        var actorRoleIds = await (
            from ur in dbContext.UserRoles.AsNoTracking()
            join r in dbContext.Roles.AsNoTracking() on ur.RoleId equals r.Id
            where ur.UserId == actor.Id && r.IsActive
            select r.Id)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (actorRoleIds.Count == 0)
        {
            return new WorkflowInboxResponse([], traceId);
        }

        var activeStages = await (
            from st in dbContext.WorkflowRunStages.AsNoTracking()
            join run in dbContext.WorkflowRuns.AsNoTracking() on st.RunId equals run.Id
            join role in dbContext.Roles.AsNoTracking() on st.ResponsibleRoleIdSnapshot equals role.Id
            join sub in dbContext.Users.AsNoTracking() on run.SubmittedBy equals sub.Id
            where st.Status == WorkflowStageStatusValues.Active &&
                  run.Status == WorkflowRunStatusValues.InProgress &&
                  actorRoleIds.Contains(st.ResponsibleRoleIdSnapshot) &&
                  (run.RevisionId != null || run.ChangeRequestId != null)
            orderby run.StartedAt descending
            select new
            {
                run,
                st,
                RoleName = role.Name,
                SubmittedByName = sub.FullName
            })
            .ToListAsync(cancellationToken);

        if (activeStages.Count == 0)
        {
            return new WorkflowInboxResponse([], traceId);
        }

        var revIds = activeStages.Where(x => x.run.RevisionId.HasValue).Select(x => x.run.RevisionId!.Value).Distinct().ToList();
        var crIds = activeStages.Where(x => x.run.ChangeRequestId.HasValue).Select(x => x.run.ChangeRequestId!.Value).Distinct().ToList();

        var revInfos = revIds.Count == 0
            ? new Dictionary<Guid, InboxTargetInfo>()
            : await (
                from rev in dbContext.DocumentRevisions.AsNoTracking()
                join ver in dbContext.ApiVersions.AsNoTracking() on rev.ApiVersionId equals ver.Id
                join integ in dbContext.Integrations.AsNoTracking() on ver.IntegrationId equals integ.Id
                join doc in dbContext.ApiDocuments.AsNoTracking() on integ.DocumentId equals doc.Id
                where revIds.Contains(rev.Id)
                select new InboxTargetInfo(
                    rev.Id,
                    integ.Id,
                    doc.Id,
                    integ.ReferenceCode,
                    ver.VersionLabel,
                    rev.RevisionNumber,
                    rev.Status,
                    doc.ProviderInternalSystemId,
                    doc.ProviderExternalSystemId,
                    doc.ConsumerCompanySystemId,
                    doc.ConsumerEntityId))
                .ToDictionaryAsync(x => x.TargetId, cancellationToken);

        var crInfos = crIds.Count == 0
            ? new Dictionary<Guid, InboxTargetInfo>()
            : await (
                from cr in dbContext.ChangeRequests.AsNoTracking()
                join rev in dbContext.DocumentRevisions.AsNoTracking() on cr.SourceRevisionId equals rev.Id
                join ver in dbContext.ApiVersions.AsNoTracking() on rev.ApiVersionId equals ver.Id
                join integ in dbContext.Integrations.AsNoTracking() on cr.IntegrationId equals integ.Id
                join doc in dbContext.ApiDocuments.AsNoTracking() on integ.DocumentId equals doc.Id
                where crIds.Contains(cr.Id)
                select new InboxTargetInfo(
                    cr.Id,
                    integ.Id,
                    doc.Id,
                    integ.ReferenceCode,
                    ver.VersionLabel,
                    rev.RevisionNumber,
                    cr.Status,
                    doc.ProviderInternalSystemId,
                    doc.ProviderExternalSystemId,
                    doc.ConsumerCompanySystemId,
                    doc.ConsumerEntityId))
                .ToDictionaryAsync(x => x.TargetId, cancellationToken);

        var allTargetDocs = revInfos.Values
            .Concat(crInfos.Values)
            .Select(x => (x.DocumentId, x.IntProvId, x.ExtProvId, x.ConsSysId, x.ConsEntId))
            .Distinct()
            .ToList();

        var docIds = allTargetDocs.Select(v => v.DocumentId).Distinct().ToList();
        var effectiveDocPerms = await documentAuthorization.EvaluateDocumentPermissionsBatchAsync(
            actor.Id, docIds, cancellationToken);
        var hasGlobalDecide = await serverAuthorization.HasGeneralPermissionAsync(
            actorPrincipal, "workflows.decide", cancellationToken);

        var titleLookup = await BuildDocumentTitleLookupAsync(allTargetDocs, cancellationToken);

        var items = new List<WorkflowInboxItemDto>();
        foreach (var entry in activeStages)
        {
            var targetInfo = entry.run.RevisionId.HasValue
                ? revInfos.GetValueOrDefault(entry.run.RevisionId.Value)
                : crInfos.GetValueOrDefault(entry.run.ChangeRequestId!.Value);

            if (targetInfo is null)
            {
                continue;
            }

            if (!effectiveDocPerms.TryGetValue(targetInfo.DocumentId, out var permsMap))
            {
                continue;
            }

            var canViewDoc = permsMap.TryGetValue(DocumentPermissionCatalog.View, out var vRes) && vRes.Allowed;
            if (!canViewDoc)
            {
                continue;
            }

            var canReviewDoc = permsMap.TryGetValue(DocumentPermissionCatalog.Review, out var rRes) && rRes.Allowed;
            var derivedTitle = titleLookup.GetValueOrDefault(targetInfo.DocumentId, targetInfo.ReferenceCode);

            items.Add(new WorkflowInboxItemDto(
                RunId: entry.run.Id,
                StageId: entry.st.Id,
                StagePosition: entry.st.Position,
                StageNameSnapshot: entry.st.StageNameSnapshot,
                ResponsibleRoleIdSnapshot: entry.st.ResponsibleRoleIdSnapshot,
                ResponsibleRoleName: entry.RoleName,
                IntegrationId: targetInfo.IntegrationId,
                DocumentId: targetInfo.DocumentId,
                ReferenceCode: targetInfo.ReferenceCode,
                DerivedTitle: derivedTitle,
                RevisionId: entry.run.RevisionId,
                ChangeRequestId: entry.run.ChangeRequestId,
                VersionLabel: targetInfo.VersionLabel,
                RevisionNumber: targetInfo.RevisionNumber,
                RevisionStatus: targetInfo.Status,
                SubmittedBy: entry.run.SubmittedBy,
                SubmittedByName: entry.SubmittedByName,
                StartedAt: entry.run.StartedAt,
                CanDecide: hasGlobalDecide && canReviewDoc));
        }

        return new WorkflowInboxResponse(items, traceId);
    }

    public async Task<WorkflowRunDetailsDto> GetWorkflowRunDetailsAsync(
        ClaimsPrincipal actorPrincipal,
        Guid runId,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var context = await ResolveAuthorizedWorkflowRunAsync(
            actorPrincipal,
            runId,
            DocumentPermissionCatalog.View,
            "WORKFLOW_RUN_READ_DENIED",
            cancellationToken);

        return await BuildRunDetailsDtoAsync(
            actorPrincipal, context.Actor.Id, context.Run, context.Document, context.Integration,
            context.Version, context.Revision, traceId, cancellationToken);
    }

    public async Task<WorkflowHistoryResponse> GetWorkflowHistoryAsync(
        ClaimsPrincipal actorPrincipal,
        Guid runId,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var context = await ResolveAuthorizedWorkflowRunAsync(
            actorPrincipal,
            runId,
            DocumentPermissionCatalog.View,
            "WORKFLOW_HISTORY_DENIED",
            cancellationToken);

        var runQuery = context.Run.RevisionId.HasValue
            ? dbContext.WorkflowRuns.AsNoTracking().Where(r => r.RevisionId == context.Run.RevisionId)
            : context.Run.ChangeRequestId.HasValue
                ? dbContext.WorkflowRuns.AsNoTracking().Where(r => r.ChangeRequestId == context.Run.ChangeRequestId)
                : dbContext.WorkflowRuns.AsNoTracking().Where(r => r.Id == context.Run.Id);

        var relatedRuns = await runQuery
            .OrderByDescending(r => r.StartedAt)
            .ToListAsync(cancellationToken);

        var relatedRunIds = relatedRuns.Select(r => r.Id).ToList();
        var stageRows = await (
            from st in dbContext.WorkflowRunStages.AsNoTracking()
            join role in dbContext.Roles.AsNoTracking() on st.ResponsibleRoleIdSnapshot equals role.Id
            join dec in dbContext.WorkflowDecisions.AsNoTracking() on st.Id equals dec.RunStageId
            join usr in dbContext.Users.AsNoTracking() on dec.ActorUserId equals usr.Id
            where relatedRunIds.Contains(st.RunId)
            orderby dec.DecidedAt descending
            select new
            {
                st,
                RoleName = role.Name,
                dec,
                ActorName = usr.FullName
            })
            .ToListAsync(cancellationToken);

        var runMap = relatedRuns.ToDictionary(r => r.Id);
        var entries = stageRows.Select(item =>
        {
            var r = runMap[item.st.RunId];
            return new WorkflowDecisionHistoryEntryDto(
                RunId: r.Id,
                StageId: item.st.Id,
                StagePosition: item.st.Position,
                StageNameSnapshot: item.st.StageNameSnapshot,
                ResponsibleRoleName: item.RoleName,
                RunStatus: r.Status,
                RevisionId: r.RevisionId,
                RevisionNumber: context.Revision?.RevisionNumber,
                RunStartedAt: r.StartedAt,
                RunEndedAt: r.EndedAt,
                Decision: new WorkflowDecisionDto(
                    item.dec.Id,
                    item.dec.RunStageId,
                    item.dec.ActorUserId,
                    item.ActorName,
                    item.dec.Decision,
                    item.dec.Notes,
                    item.dec.DecidedAt));
        }).ToList();

        return new WorkflowHistoryResponse(
            context.Run.Id,
            context.Run.RevisionId,
            context.Run.ChangeRequestId,
            entries,
            traceId);
    }

    public async Task<SubmitStageDecisionResponse> SubmitStageDecisionAsync(
        ClaimsPrincipal actorPrincipal,
        Guid runId,
        Guid stageId,
        SubmitStageDecisionRequest request,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var normalizedDecision = WorkflowDecisionValues.Normalize(request.Decision);
        var trimmedNotes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();

        if ((normalizedDecision is WorkflowDecisionValues.Return or WorkflowDecisionValues.Reject) &&
            string.IsNullOrWhiteSpace(trimmedNotes))
        {
            throw ApiContractException.BadRequest(
                messageAr: "الملاحظات مطلوبة عند إعادة المراجعة للعمل أو رفضها.",
                messageEn: "Notes are required when returning or rejecting a stage.");
        }

        // 1. Central + Document ACL check (doc.view first -> 404 anti-IDOR, then doc.review -> 403 Forbidden)
        var context = await ResolveAuthorizedWorkflowRunAsync(
            actorPrincipal,
            runId,
            DocumentPermissionCatalog.Review,
            "WORKFLOW_DECISION_DENIED",
            cancellationToken);

        var stage = await dbContext.WorkflowRunStages.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == stageId && s.RunId == runId, cancellationToken);
        if (stage is null)
        {
            throw ApiContractException.NotFound(
                messageAr: "مرحلة سير العمل المطلوبة غير موجودة ضمن هذا التشغيل.",
                messageEn: "Workflow run stage was not found in this workflow run.");
        }

        if (!string.Equals(context.Run.Status, WorkflowRunStatusValues.InProgress, StringComparison.OrdinalIgnoreCase))
        {
            throw ApiContractException.Conflict(
                messageAr: $"تم إغلاق مسار الاعتماد بالفعل في حالة ({context.Run.Status}).",
                messageEn: $"Workflow run is already closed in status '{context.Run.Status}'.");
        }

        // 2. Active role membership check across ALL user's active roles (W02 / ADR-01)
        var holdsResponsibleRole = await (
            from ur in dbContext.UserRoles.AsNoTracking()
            join r in dbContext.Roles.AsNoTracking() on ur.RoleId equals r.Id
            where ur.UserId == context.Actor.Id && r.Id == stage.ResponsibleRoleIdSnapshot && r.IsActive
            select r.Id)
            .AnyAsync(cancellationToken);

        if (!holdsResponsibleRole)
        {
            await serverAuthorization.WriteAuditEventAsync(
                context.Actor.Id,
                "WORKFLOW_DECISION_ROLE_MISMATCH",
                "WORKFLOW_STAGE",
                stage.Id,
                "DENIED",
                new
                {
                    runId,
                    stageId,
                    requiredRoleId = stage.ResponsibleRoleIdSnapshot,
                    integrationId = context.Integration.Id
                },
                cancellationToken);

            throw ApiContractException.Forbidden(
                messageAr: "لا ينتمي حسابك إلى الدور النشط المسؤول عن هذه المرحلة.",
                messageEn: "Your active roles do not include the role assigned to this workflow stage.");
        }

        // 3. Stage must currently be ACTIVE (PENDING or already decided stages cannot be acted upon)
        if (string.Equals(stage.Status, WorkflowStageStatusValues.Pending, StringComparison.OrdinalIgnoreCase))
        {
            throw ApiContractException.UnprocessableEntity(
                messageAr: "لا يمكن اتخاذ قرار على هذه المرحلة قبل اكتمال المراحل السابقة لها.",
                messageEn: "This stage is still pending earlier sequential stages.");
        }

        if (!string.Equals(stage.Status, WorkflowStageStatusValues.Active, StringComparison.OrdinalIgnoreCase))
        {
            await serverAuthorization.WriteAuditEventAsync(
                context.Actor.Id,
                "WORKFLOW_DECISION_CONFLICT",
                "WORKFLOW_STAGE",
                stage.Id,
                "CONFLICT",
                new { runId, stageId, currentStageStatus = stage.Status },
                cancellationToken);

            throw ApiContractException.Conflict(
                messageAr: "تم اتخاذ قرار نهائي على هذه المرحلة مسبقًا (قرار واحد لكل مرحلة).",
                messageEn: "A decision has already been recorded for this stage.");
        }

        var now = DateTimeOffset.UtcNow;
        var stageOutcome = normalizedDecision switch
        {
            WorkflowDecisionValues.Approve => WorkflowStageStatusValues.Approved,
            WorkflowDecisionValues.Return => WorkflowStageStatusValues.Returned,
            _ => WorkflowStageStatusValues.Rejected
        };

        var decisionEntity = new WorkflowDecisionEntity
        {
            Id = Guid.NewGuid(),
            RunStageId = stage.Id,
            ActorUserId = context.Actor.Id,
            Decision = normalizedDecision,
            Notes = trimmedNotes,
            DecidedAt = now
        };

        string outcomeStatus;

        try
        {
            await using var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken);

            // Atomic conditional claim of the ACTIVE stage ensures single-member decision concurrency (W02).
            var claimed = await dbContext.WorkflowRunStages
                .Where(s => s.Id == stage.Id && s.RunId == runId && s.Status == WorkflowStageStatusValues.Active)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, stageOutcome), cancellationToken);

            if (claimed != 1)
            {
                await tx.RollbackAsync(cancellationToken);
                throw ApiContractException.Conflict(
                    messageAr: "تم تسجيل قرار متزامن على هذه المرحلة من عضو آخر بالفعل.",
                    messageEn: "A concurrent decision was already recorded for this stage.");
            }

            dbContext.WorkflowDecisions.Add(decisionEntity);
            await dbContext.SaveChangesAsync(cancellationToken);

            WorkflowRunStageEntity? activatedNextStage = null;

            if (normalizedDecision == WorkflowDecisionValues.Approve)
            {
                var nextStage = await dbContext.WorkflowRunStages
                    .Where(s => s.RunId == runId && s.Position > stage.Position)
                    .OrderBy(s => s.Position)
                    .FirstOrDefaultAsync(cancellationToken);

                if (nextStage is not null)
                {
                    nextStage.Status = WorkflowStageStatusValues.Active;
                    activatedNextStage = nextStage;
                    outcomeStatus = WorkflowRunStatusValues.InProgress;

                    if (context.Run.RevisionId.HasValue)
                    {
                        await dbContext.DocumentRevisions
                            .Where(r => r.Id == context.Run.RevisionId.Value)
                            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, "IN_REVIEW"), cancellationToken);
                    }
                    else if (context.Run.ChangeRequestId.HasValue)
                    {
                        await dbContext.ChangeRequests
                            .Where(cr => cr.Id == context.Run.ChangeRequestId.Value)
                            .ExecuteUpdateAsync(s => s.SetProperty(cr => cr.Status, "IN_REVIEW"), cancellationToken);
                    }
                }
                else
                {
                    outcomeStatus = WorkflowRunStatusValues.Approved;
                    await dbContext.WorkflowRuns
                        .Where(r => r.Id == runId)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(r => r.Status, WorkflowRunStatusValues.Approved)
                            .SetProperty(r => r.EndedAt, now), cancellationToken);

                    if (context.Run.RevisionId.HasValue)
                    {
                        await dbContext.DocumentRevisions
                            .Where(r => r.Id == context.Run.RevisionId.Value)
                            .ExecuteUpdateAsync(s => s
                                .SetProperty(r => r.Status, "APPROVED")
                                .SetProperty(r => r.ApprovedAt, now), cancellationToken);

                        dbContext.ReleaseHistoryEvents.Add(new ReleaseHistoryEventEntity
                        {
                            Id = Guid.NewGuid(),
                            RevisionId = context.Run.RevisionId.Value,
                            EventType = "REVISION_APPROVED",
                            ActorUserId = context.Actor.Id,
                            ChangeRequestId = context.Revision?.ChangeRequestId,
                            RecordedAt = now
                        });
                    }
                    else if (context.Run.ChangeRequestId.HasValue)
                    {
                        await dbContext.ChangeRequests
                            .Where(cr => cr.Id == context.Run.ChangeRequestId.Value)
                            .ExecuteUpdateAsync(s => s.SetProperty(cr => cr.Status, "APPROVED"), cancellationToken);
                    }
                }
            }
            else
            {
                outcomeStatus = normalizedDecision == WorkflowDecisionValues.Return
                    ? WorkflowRunStatusValues.Returned
                    : WorkflowRunStatusValues.Rejected;

                await dbContext.WorkflowRunStages
                    .Where(s => s.RunId == runId && s.Status == WorkflowStageStatusValues.Pending)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, WorkflowStageStatusValues.Cancelled), cancellationToken);

                await dbContext.WorkflowRuns
                    .Where(r => r.Id == runId)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(r => r.Status, outcomeStatus)
                        .SetProperty(r => r.EndedAt, now), cancellationToken);

                if (context.Run.RevisionId.HasValue)
                {
                    await dbContext.DocumentRevisions
                        .Where(r => r.Id == context.Run.RevisionId.Value)
                        .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, outcomeStatus), cancellationToken);

                    dbContext.ReleaseHistoryEvents.Add(new ReleaseHistoryEventEntity
                    {
                        Id = Guid.NewGuid(),
                        RevisionId = context.Run.RevisionId.Value,
                        EventType = outcomeStatus == WorkflowRunStatusValues.Returned
                            ? "REVISION_RETURNED"
                            : "REVISION_REJECTED",
                        ActorUserId = context.Actor.Id,
                        ChangeRequestId = context.Revision?.ChangeRequestId,
                        RecordedAt = now
                    });
                }
                else if (context.Run.ChangeRequestId.HasValue)
                {
                    await dbContext.ChangeRequests
                        .Where(cr => cr.Id == context.Run.ChangeRequestId.Value)
                        .ExecuteUpdateAsync(s => s.SetProperty(cr => cr.Status, outcomeStatus), cancellationToken);
                }
            }

            await dbContext.SaveChangesAsync(cancellationToken);

            var notifResourceKind = context.Run.ChangeRequestId.HasValue ? "CHANGE_REQUEST" : "DOCUMENT_REVISION";
            var notifResourceId = context.Run.ChangeRequestId ?? context.Run.RevisionId!.Value;

            if (activatedNextStage is not null)
            {
                await internalNotificationService.NotifyStageActivatedAsync(
                    activatedNextStage.ResponsibleRoleIdSnapshot,
                    context.Actor.Id,
                    notifResourceKind,
                    notifResourceId,
                    cancellationToken);
            }
            else
            {
                var outcomeKind = outcomeStatus switch
                {
                    WorkflowRunStatusValues.Approved => "WORKFLOW_APPROVED",
                    WorkflowRunStatusValues.Returned => "WORKFLOW_RETURNED",
                    _ => "WORKFLOW_REJECTED"
                };
                await internalNotificationService.NotifyWorkflowOutcomeAsync(
                    context.Run.SubmittedBy,
                    outcomeKind,
                    notifResourceKind,
                    notifResourceId,
                    cancellationToken);
            }

            await tx.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException dbEx) when (dbEx.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            dbContext.ChangeTracker.Clear();

            await serverAuthorization.WriteAuditEventAsync(
                context.Actor.Id,
                "WORKFLOW_DECISION_CONFLICT",
                "WORKFLOW_STAGE",
                stage.Id,
                "CONFLICT",
                new { runId, stageId },
                cancellationToken);

            throw ApiContractException.Conflict(
                messageAr: "لا يُسمح بأكثر من قرار واحد على نفس المرحلة (تعارض تزامن).",
                messageEn: "Concurrent decision rejected: only one decision per stage is allowed.");
        }

        await serverAuthorization.WriteAuditEventAsync(
            context.Actor.Id,
            $"WORKFLOW_STAGE_{stageOutcome}",
            "WORKFLOW_STAGE",
            stage.Id,
            "SUCCESS",
            new
            {
                runId,
                stageId,
                stage.Position,
                stage.StageNameSnapshot,
                decision = normalizedDecision,
                outcomeStatus,
                revisionId = context.Revision?.Id,
                integrationId = context.Integration.Id,
                hasNotes = trimmedNotes is not null
            },
            cancellationToken);

        dbContext.ChangeTracker.Clear();
        var refreshedRun = await dbContext.WorkflowRuns.AsNoTracking().FirstAsync(r => r.Id == runId, cancellationToken);
        var refreshedRevision = context.Revision is not null
            ? await dbContext.DocumentRevisions.AsNoTracking().FirstOrDefaultAsync(r => r.Id == context.Revision.Id, cancellationToken)
            : null;

        var runDetails = await BuildRunDetailsDtoAsync(
            actorPrincipal,
            context.Actor.Id,
            refreshedRun,
            context.Document,
            context.Integration,
            context.Version,
            refreshedRevision,
            traceId,
            cancellationToken);

        var decisionDto = new WorkflowDecisionDto(
            decisionEntity.Id,
            decisionEntity.RunStageId,
            context.Actor.Id,
            context.Actor.FullName,
            decisionEntity.Decision,
            decisionEntity.Notes,
            decisionEntity.DecidedAt);

        return new SubmitStageDecisionResponse(runDetails, decisionDto, outcomeStatus, traceId);
    }

    private sealed record InboxTargetInfo(
        Guid TargetId,
        Guid IntegrationId,
        Guid DocumentId,
        string ReferenceCode,
        string VersionLabel,
        int RevisionNumber,
        string Status,
        Guid? IntProvId,
        Guid? ExtProvId,
        Guid? ConsSysId,
        Guid? ConsEntId);

    private sealed record ResolvedWorkflowContext(
        UserEntity Actor,
        WorkflowRunEntity Run,
        ApiDocumentEntity Document,
        IntegrationEntity Integration,
        ApiVersionEntity? Version,
        DocumentRevisionEntity? Revision);

    private async Task<ResolvedWorkflowContext> ResolveAuthorizedWorkflowRunAsync(
        ClaimsPrincipal actorPrincipal,
        Guid runId,
        string requiredDocPermission,
        string auditDenyOp,
        CancellationToken cancellationToken)
    {
        var actor = await serverAuthorization.RequireActiveAuthenticatedUserAsync(actorPrincipal, cancellationToken);

        var run = await dbContext.WorkflowRuns.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);

        if (run is null)
        {
            throw ApiContractException.NotFound(
                messageAr: "تشغيل مسار الاعتماد المطلوب غير موجود.",
                messageEn: "Workflow run was not found.");
        }

        DocumentRevisionEntity? revision = null;
        ApiVersionEntity? version = null;
        IntegrationEntity? integration = null;

        if (run.RevisionId.HasValue)
        {
            revision = await dbContext.DocumentRevisions.AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == run.RevisionId.Value, cancellationToken);
            if (revision is not null)
            {
                version = await dbContext.ApiVersions.AsNoTracking()
                    .FirstOrDefaultAsync(v => v.Id == revision.ApiVersionId, cancellationToken);
            }
            if (version is not null)
            {
                integration = await dbContext.Integrations.AsNoTracking()
                    .FirstOrDefaultAsync(i => i.Id == version.IntegrationId, cancellationToken);
            }
        }
        else if (run.ChangeRequestId.HasValue)
        {
            var cr = await dbContext.ChangeRequests.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == run.ChangeRequestId.Value, cancellationToken);
            if (cr is not null)
            {
                integration = await dbContext.Integrations.AsNoTracking()
                    .FirstOrDefaultAsync(i => i.Id == cr.IntegrationId, cancellationToken);
                revision = await dbContext.DocumentRevisions.AsNoTracking()
                    .FirstOrDefaultAsync(r => r.Id == cr.SourceRevisionId, cancellationToken);
                if (revision is not null)
                {
                    version = await dbContext.ApiVersions.AsNoTracking()
                        .FirstOrDefaultAsync(v => v.Id == revision.ApiVersionId, cancellationToken);
                }
            }
        }

        if (integration is null)
        {
            throw ApiContractException.NotFound(
                messageAr: "سياق التكامل المرتبط بمسار الاعتماد غير موجود.",
                messageEn: "Associated integration for workflow run was not found.");
        }

        var doc = await dbContext.ApiDocuments.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == integration.DocumentId, cancellationToken);
        if (doc is null)
        {
            throw ApiContractException.NotFound(
                messageAr: "وثيقة API المرتبطة بمسار الاعتماد غير موجودة.",
                messageEn: "Associated API document for workflow run was not found.");
        }

        await documentAuthorization.RequireDocumentPermissionAsync(
            actorPrincipal,
            doc.Id,
            requiredDocPermission,
            auditDenyOp,
            cancellationToken);

        return new ResolvedWorkflowContext(actor, run, doc, integration, version, revision);
    }

    private async Task<WorkflowRunDetailsDto> BuildRunDetailsDtoAsync(
        ClaimsPrincipal actorPrincipal,
        Guid actorUserId,
        WorkflowRunEntity run,
        ApiDocumentEntity document,
        IntegrationEntity integration,
        ApiVersionEntity? version,
        DocumentRevisionEntity? revision,
        string traceId,
        CancellationToken cancellationToken)
    {
        var routeName = await dbContext.ApprovalRoutes.AsNoTracking()
            .Where(r => r.Id == run.RouteId)
            .Select(r => r.Name)
            .FirstOrDefaultAsync(cancellationToken) ?? "Approval Route";

        var submittedByName = await dbContext.Users.AsNoTracking()
            .Where(u => u.Id == run.SubmittedBy)
            .Select(u => u.FullName)
            .FirstOrDefaultAsync(cancellationToken) ?? "User";

        var rawStages = await (
            from st in dbContext.WorkflowRunStages.AsNoTracking()
            join role in dbContext.Roles.AsNoTracking() on st.ResponsibleRoleIdSnapshot equals role.Id
            where st.RunId == run.Id
            orderby st.Position
            select new { st, RoleName = role.Name })
            .ToListAsync(cancellationToken);

        var stageIds = rawStages.Select(s => s.st.Id).ToList();
        var decisionsMap = await (
            from dec in dbContext.WorkflowDecisions.AsNoTracking()
            join usr in dbContext.Users.AsNoTracking() on dec.ActorUserId equals usr.Id
            where stageIds.Contains(dec.RunStageId)
            select new { dec, usr.FullName })
            .ToDictionaryAsync(x => x.dec.RunStageId, cancellationToken);

        var stageDtos = rawStages.Select(x =>
        {
            WorkflowDecisionDto? decDto = decisionsMap.TryGetValue(x.st.Id, out var found)
                ? new WorkflowDecisionDto(
                    found.dec.Id,
                    found.dec.RunStageId,
                    found.dec.ActorUserId,
                    found.FullName,
                    found.dec.Decision,
                    found.dec.Notes,
                    found.dec.DecidedAt)
                : null;

            return new WorkflowRunStageDto(
                x.st.Id,
                x.st.RunId,
                x.st.Position,
                x.st.StageNameSnapshot,
                x.st.ResponsibleRoleIdSnapshot,
                x.RoleName,
                x.st.Status,
                decDto);
        }).ToList();

        var activeStage = stageDtos.FirstOrDefault(s => s.Status == WorkflowStageStatusValues.Active);
        var canDecideActive = false;

        if (activeStage is not null &&
            run.Status == WorkflowRunStatusValues.InProgress)
        {
            var hasDecidePerm = await serverAuthorization.HasGeneralPermissionAsync(
                actorPrincipal, "workflows.decide", cancellationToken);
            if (hasDecidePerm)
            {
                var docPerms = await documentAuthorization.EvaluateDocumentPermissionsBatchAsync(
                    actorUserId, [document.Id], cancellationToken);
                var hasDocReview = docPerms.TryGetValue(document.Id, out var map) &&
                                   map.TryGetValue(DocumentPermissionCatalog.Review, out var res) &&
                                   res.Allowed;
                if (hasDocReview)
                {
                    canDecideActive = await (
                        from ur in dbContext.UserRoles.AsNoTracking()
                        join r in dbContext.Roles.AsNoTracking() on ur.RoleId equals r.Id
                        where ur.UserId == actorUserId &&
                              r.Id == activeStage.ResponsibleRoleIdSnapshot &&
                              r.IsActive
                        select r.Id)
                        .AnyAsync(cancellationToken);
                }
            }
        }

        var titles = await BuildDocumentTitleLookupAsync(
            [(document.Id, document.ProviderInternalSystemId, document.ProviderExternalSystemId, document.ConsumerCompanySystemId, document.ConsumerEntityId)],
            cancellationToken);

        return new WorkflowRunDetailsDto(
            Id: run.Id,
            RouteId: run.RouteId,
            RouteName: routeName,
            RevisionId: run.RevisionId,
            ChangeRequestId: run.ChangeRequestId,
            IntegrationId: integration.Id,
            DocumentId: document.Id,
            ReferenceCode: integration.ReferenceCode,
            DerivedTitle: titles.GetValueOrDefault(document.Id, integration.ReferenceCode),
            VersionLabel: version?.VersionLabel,
            RevisionNumber: revision?.RevisionNumber,
            RevisionStatus: revision?.Status,
            Status: run.Status,
            SubmittedBy: run.SubmittedBy,
            SubmittedByName: submittedByName,
            StartedAt: run.StartedAt,
            EndedAt: run.EndedAt,
            ActiveStageId: activeStage?.Id,
            ActiveStageName: activeStage?.StageNameSnapshot,
            ActiveStageRoleName: activeStage?.ResponsibleRoleName,
            CanCurrentUserDecideActiveStage: canDecideActive,
            Stages: stageDtos,
            TraceId: traceId);
    }

    private async Task<Dictionary<Guid, string>> BuildDocumentTitleLookupAsync(
        IReadOnlyList<(Guid DocumentId, Guid? InternalId, Guid? ExternalId, Guid? ConsumerSystemId, Guid? ConsumerEntityId)> docs,
        CancellationToken cancellationToken)
    {
        if (docs.Count == 0)
        {
            return [];
        }

        var internalIds = docs.Where(d => d.InternalId.HasValue).Select(d => d.InternalId!.Value).Distinct().ToList();
        var externalIds = docs.Where(d => d.ExternalId.HasValue).Select(d => d.ExternalId!.Value).Distinct().ToList();
        var consumerSystemIds = docs.Where(d => d.ConsumerSystemId.HasValue).Select(d => d.ConsumerSystemId!.Value).Distinct().ToList();
        var consumerEntityIds = docs.Where(d => d.ConsumerEntityId.HasValue).Select(d => d.ConsumerEntityId!.Value).Distinct().ToList();

        var internalNames = internalIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await (
                from ips in dbContext.InternalProviderSystems.AsNoTracking()
                join cs in dbContext.CompanySystems.AsNoTracking() on ips.CompanySystemId equals cs.Id
                where internalIds.Contains(ips.Id)
                select new { ips.Id, cs.Name })
                .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        var externalNames = externalIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await dbContext.ExternalProviderSystems.AsNoTracking()
                .Where(e => externalIds.Contains(e.Id))
                .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        var consumerSystemNames = consumerSystemIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await dbContext.CompanySystems.AsNoTracking()
                .Where(c => consumerSystemIds.Contains(c.Id))
                .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        var consumerEntityNames = consumerEntityIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await dbContext.ConsumerEntities.AsNoTracking()
                .Where(c => consumerEntityIds.Contains(c.Id))
                .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        var result = new Dictionary<Guid, string>();
        foreach (var d in docs)
        {
            var provider = d.InternalId.HasValue
                ? internalNames.GetValueOrDefault(d.InternalId.Value, "Internal Provider")
                : d.ExternalId.HasValue
                    ? externalNames.GetValueOrDefault(d.ExternalId.Value, "External Provider")
                    : "Provider";
            var consumer = d.ConsumerSystemId.HasValue
                ? consumerSystemNames.GetValueOrDefault(d.ConsumerSystemId.Value, "Consumer")
                : d.ConsumerEntityId.HasValue
                    ? consumerEntityNames.GetValueOrDefault(d.ConsumerEntityId.Value, "Consumer")
                    : "Consumer";
            result[d.DocumentId] = $"{provider} ← {consumer}";
        }

        return result;
    }
}
