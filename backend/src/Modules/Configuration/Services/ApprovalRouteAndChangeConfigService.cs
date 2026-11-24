using System.Security.Claims;
using ApiGovernance.Modules.Configuration.Contracts;
using ApiGovernance.Shared.Contracts;
using ApiGovernance.Shared.Persistence;
using ApiGovernance.Shared.Persistence.Entities;
using ApiGovernance.Shared.Security;
using Microsoft.EntityFrameworkCore;

namespace ApiGovernance.Modules.Configuration.Services;

public interface IApprovalRouteAndChangeConfigService
{
    // M06 • Approval Routes & Stages
    Task<IReadOnlyList<ApprovalRouteRoleOptionDto>> GetAssignableRolesAsync(CancellationToken cancellationToken = default);

    Task<PagedResult<ApprovalRouteDto>> GetApprovalRoutesAsync(
        int? page,
        int? pageSize,
        string? search,
        bool? isActive,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<ApprovalRouteDto> CreateApprovalRouteAsync(
        ClaimsPrincipal actorPrincipal,
        CreateApprovalRouteRequest request,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<ApprovalRouteDto> UpdateApprovalRouteAsync(
        ClaimsPrincipal actorPrincipal,
        Guid routeId,
        UpdateApprovalRouteRequest request,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<ApprovalRouteDto> ReplaceApprovalRouteStagesAsync(
        ClaimsPrincipal actorPrincipal,
        Guid routeId,
        ReplaceApprovalRouteStagesRequest request,
        string traceId,
        CancellationToken cancellationToken = default);

    // M07 • Change Types & Change Policies
    Task<IReadOnlyList<ApprovalRouteOptionDto>> GetAssignableRoutesForChangeTypesAsync(CancellationToken cancellationToken = default);

    Task<PagedResult<ChangeTypeDto>> GetChangeTypesAsync(
        int? page,
        int? pageSize,
        string? search,
        bool? isActive,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<ChangeTypeDto> CreateChangeTypeAsync(
        ClaimsPrincipal actorPrincipal,
        CreateChangeTypeRequest request,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<ChangeTypeDto> UpdateChangeTypeAsync(
        ClaimsPrincipal actorPrincipal,
        Guid typeId,
        UpdateChangeTypeRequest request,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<PagedResult<ChangePolicyDto>> GetChangePoliciesAsync(
        int? page,
        int? pageSize,
        string? search,
        bool? isActive,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<ChangePolicyDto> CreateChangePolicyAsync(
        ClaimsPrincipal actorPrincipal,
        CreateChangePolicyRequest request,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<ChangePolicyDto> UpdateChangePolicyAsync(
        ClaimsPrincipal actorPrincipal,
        Guid policyId,
        UpdateChangePolicyRequest request,
        string traceId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Implements Tasks M06 (Approval Routes with ordered Role stages — NO stage_type, NO assigned_user_id)
/// and M07 (Change Types linked to Approval Routes + Change Policies) with reference protection and OP-01 auditing.
/// </summary>
public sealed class ApprovalRouteAndChangeConfigService(
    ApiGovernanceDbContext dbContext,
    IServerAuthorizationService serverAuthorization) : IApprovalRouteAndChangeConfigService
{
    private const string ArchivedState = "ARCHIVED";
    private const string InProgressStatus = "IN_PROGRESS";

    // ========================================================================
    // M06 • Approval Routes & Ordered Role-Based Stages (UI-05 / UI-05A)
    // ========================================================================
    public async Task<IReadOnlyList<ApprovalRouteRoleOptionDto>> GetAssignableRolesAsync(
        CancellationToken cancellationToken = default)
    {
        return await dbContext.Roles.AsNoTracking()
            .Where(r => r.IsActive)
            .OrderBy(r => r.Name)
            .Select(r => new ApprovalRouteRoleOptionDto(r.Id, r.Name, r.IsActive))
            .ToListAsync(cancellationToken);
    }

    public async Task<PagedResult<ApprovalRouteDto>> GetApprovalRoutesAsync(
        int? page,
        int? pageSize,
        string? search,
        bool? isActive,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.ApprovalRoutes.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(r => r.Name.ToLower().Contains(term));
        }
        if (isActive is not null)
        {
            query = query.Where(r => r.IsActive == isActive.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var (_, safePageSize, totalPages, normalizedPage) = PaginationHelper.Compute(page, pageSize, totalCount);

        var rows = await query
            .OrderBy(r => r.Name)
            .Skip((normalizedPage - 1) * safePageSize)
            .Take(safePageSize)
            .ToListAsync(cancellationToken);

        var dtos = await BuildApprovalRouteDtosAsync(rows, traceId, cancellationToken);
        return new PagedResult<ApprovalRouteDto>(dtos, normalizedPage, safePageSize, totalCount, totalPages);
    }

    public async Task<ApprovalRouteDto> CreateApprovalRouteAsync(
        ClaimsPrincipal actorPrincipal,
        CreateApprovalRouteRequest request,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var actor = await serverAuthorization.RequireActiveAuthenticatedUserAsync(actorPrincipal, cancellationToken);
        var trimmedName = request.Name.Trim();

        var duplicate = await dbContext.ApprovalRoutes.AsNoTracking()
            .AnyAsync(r => r.Name.ToLower() == trimmedName.ToLower(), cancellationToken);
        if (duplicate)
        {
            throw ApiContractException.Conflict(
                messageAr: "يوجد مسار اعتماد مسجّل بهذا الاسم مسبقًا.",
                messageEn: "An approval route with this name already exists.");
        }

        List<ValidatedStageSpec>? validatedStages = null;
        if (request.Stages is not null)
        {
            validatedStages = await ValidateStagesAsync(request.Stages, cancellationToken);
        }

        var route = new ApprovalRouteEntity
        {
            Id = Guid.NewGuid(),
            Name = trimmedName,
            // An empty route without stages starts inactive until valid ordered stages are attached (prevents invalid active routes).
            IsActive = validatedStages is { Count: > 0 }
        };

        await using var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        dbContext.ApprovalRoutes.Add(route);

        if (validatedStages is not null)
        {
            foreach (var stage in validatedStages)
            {
                dbContext.ApprovalRouteStages.Add(new ApprovalRouteStageEntity
                {
                    Id = Guid.NewGuid(),
                    RouteId = route.Id,
                    Position = stage.Position,
                    StageName = stage.StageName,
                    ResponsibleRoleId = stage.ResponsibleRoleId
                });
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        await serverAuthorization.WriteAuditEventAsync(
            actor.Id,
            "APPROVAL_ROUTE_CREATED",
            "APPROVAL_ROUTE",
            route.Id,
            "SUCCESS",
            new
            {
                route.Name,
                route.IsActive,
                stageCount = validatedStages?.Count ?? 0,
                stages = validatedStages?.Select(s => new { s.Position, s.StageName, s.ResponsibleRoleId })
            },
            cancellationToken);

        return (await BuildApprovalRouteDtosAsync([route], traceId, cancellationToken))[0];
    }

    public async Task<ApprovalRouteDto> UpdateApprovalRouteAsync(
        ClaimsPrincipal actorPrincipal,
        Guid routeId,
        UpdateApprovalRouteRequest request,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var actor = await serverAuthorization.RequireActiveAuthenticatedUserAsync(actorPrincipal, cancellationToken);

        var route = await dbContext.ApprovalRoutes
            .FirstOrDefaultAsync(r => r.Id == routeId, cancellationToken)
            ?? throw ApiContractException.NotFound(
                messageAr: "مسار الاعتماد غير موجود.",
                messageEn: "Approval route was not found.");

        if (request.Name is not null)
        {
            var trimmedName = request.Name.Trim();
            var duplicate = await dbContext.ApprovalRoutes.AsNoTracking()
                .AnyAsync(r => r.Id != routeId && r.Name.ToLower() == trimmedName.ToLower(), cancellationToken);
            if (duplicate)
            {
                throw ApiContractException.Conflict(
                    messageAr: "يوجد مسار اعتماد آخر مسجّل بهذا الاسم مسبقًا.",
                    messageEn: "Another approval route with this name already exists.");
            }
            route.Name = trimmedName;
        }

        if (request.IsActive == true)
        {
            var stageCount = await dbContext.ApprovalRouteStages.AsNoTracking()
                .CountAsync(s => s.RouteId == routeId, cancellationToken);
            if (stageCount == 0)
            {
                throw ApiContractException.UnprocessableEntity(
                    messageAr: "لا يمكن تفعيل مسار اعتماد بدون مرحلة واحدة على الأقل (مسار غير صالح).",
                    messageEn: "Cannot activate an approval route with zero stages.");
            }
        }
        else if (request.IsActive == false && route.IsActive)
        {
            var hasActiveChangeTypes = await dbContext.ChangeTypes.AsNoTracking()
                .AnyAsync(ct => ct.RouteId == routeId && ct.IsActive, cancellationToken);
            var hasActiveDocs = await dbContext.ApiDocuments.AsNoTracking()
                .AnyAsync(d => d.ApprovalRouteId == routeId && d.State != ArchivedState, cancellationToken);
            var hasActiveRuns = await dbContext.WorkflowRuns.AsNoTracking()
                .AnyAsync(wr => wr.RouteId == routeId && wr.Status == InProgressStatus, cancellationToken);

            if (hasActiveChangeTypes || hasActiveDocs || hasActiveRuns)
            {
                throw ApiContractException.UnprocessableEntity(
                    messageAr: "لا يمكن تعطيل مسار الاعتماد لارتباطه بأنواع تغيير أو وثائق أو معاملات اعتماد نشطة (حماية المراجع).",
                    messageEn: "Cannot deactivate an approval route referenced by active change types, documents, or workflow runs.");
            }
        }

        var wasActive = route.IsActive;
        if (request.IsActive is not null)
        {
            route.IsActive = request.IsActive.Value;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var op = wasActive && !route.IsActive
            ? "APPROVAL_ROUTE_DEACTIVATED"
            : (!wasActive && route.IsActive ? "APPROVAL_ROUTE_ACTIVATED" : "APPROVAL_ROUTE_UPDATED");

        await serverAuthorization.WriteAuditEventAsync(
            actor.Id,
            op,
            "APPROVAL_ROUTE",
            route.Id,
            "SUCCESS",
            new { route.Name, route.IsActive },
            cancellationToken);

        return (await BuildApprovalRouteDtosAsync([route], traceId, cancellationToken))[0];
    }

    public async Task<ApprovalRouteDto> ReplaceApprovalRouteStagesAsync(
        ClaimsPrincipal actorPrincipal,
        Guid routeId,
        ReplaceApprovalRouteStagesRequest request,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var actor = await serverAuthorization.RequireActiveAuthenticatedUserAsync(actorPrincipal, cancellationToken);

        var route = await dbContext.ApprovalRoutes
            .FirstOrDefaultAsync(r => r.Id == routeId, cancellationToken)
            ?? throw ApiContractException.NotFound(
                messageAr: "مسار الاعتماد غير موجود.",
                messageEn: "Approval route was not found.");

        var validatedStages = await ValidateStagesAsync(request.Stages, cancellationToken);

        await using var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var existingStages = await dbContext.ApprovalRouteStages
            .Where(s => s.RouteId == routeId)
            .ToListAsync(cancellationToken);

        var hadZeroStages = existingStages.Count == 0;
        if (existingStages.Count > 0)
        {
            dbContext.ApprovalRouteStages.RemoveRange(existingStages);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        foreach (var stage in validatedStages)
        {
            dbContext.ApprovalRouteStages.Add(new ApprovalRouteStageEntity
            {
                Id = Guid.NewGuid(),
                RouteId = routeId,
                Position = stage.Position,
                StageName = stage.StageName,
                ResponsibleRoleId = stage.ResponsibleRoleId
            });
        }

        // First stage definition activates a newly initialized draft route cleanly.
        if (hadZeroStages && !route.IsActive)
        {
            route.IsActive = true;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        await serverAuthorization.WriteAuditEventAsync(
            actor.Id,
            "APPROVAL_ROUTE_STAGES_REPLACED",
            "APPROVAL_ROUTE",
            route.Id,
            "SUCCESS",
            new
            {
                route.Name,
                route.IsActive,
                stages = validatedStages.Select(s => new { s.Position, s.StageName, s.ResponsibleRoleId })
            },
            cancellationToken);

        return (await BuildApprovalRouteDtosAsync([route], traceId, cancellationToken))[0];
    }

    // ========================================================================
    // M07 • Change Types & Change Policies (UI-06)
    // ========================================================================
    public async Task<IReadOnlyList<ApprovalRouteOptionDto>> GetAssignableRoutesForChangeTypesAsync(
        CancellationToken cancellationToken = default)
    {
        var activeRoutes = await dbContext.ApprovalRoutes.AsNoTracking()
            .Where(r => r.IsActive)
            .OrderBy(r => r.Name)
            .ToListAsync(cancellationToken);

        var ids = activeRoutes.Select(r => r.Id).ToList();
        var stageCounts = ids.Count == 0
            ? new Dictionary<Guid, int>()
            : await dbContext.ApprovalRouteStages.AsNoTracking()
                .Where(s => ids.Contains(s.RouteId))
                .GroupBy(s => s.RouteId)
                .Select(g => new { Id = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Id, x => x.Count, cancellationToken);

        return activeRoutes
            .Where(r => stageCounts.GetValueOrDefault(r.Id, 0) >= 1)
            .Select(r => new ApprovalRouteOptionDto(r.Id, r.Name, stageCounts[r.Id], r.IsActive))
            .ToList();
    }

    public async Task<PagedResult<ChangeTypeDto>> GetChangeTypesAsync(
        int? page,
        int? pageSize,
        string? search,
        bool? isActive,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.ChangeTypes.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(ct => ct.Name.ToLower().Contains(term));
        }
        if (isActive is not null)
        {
            query = query.Where(ct => ct.IsActive == isActive.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var (_, safePageSize, totalPages, normalizedPage) = PaginationHelper.Compute(page, pageSize, totalCount);

        var rows = await query
            .OrderBy(ct => ct.Name)
            .Skip((normalizedPage - 1) * safePageSize)
            .Take(safePageSize)
            .ToListAsync(cancellationToken);

        var dtos = await BuildChangeTypeDtosAsync(rows, traceId, cancellationToken);
        return new PagedResult<ChangeTypeDto>(dtos, normalizedPage, safePageSize, totalCount, totalPages);
    }

    public async Task<ChangeTypeDto> CreateChangeTypeAsync(
        ClaimsPrincipal actorPrincipal,
        CreateChangeTypeRequest request,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var actor = await serverAuthorization.RequireActiveAuthenticatedUserAsync(actorPrincipal, cancellationToken);
        var trimmedName = request.Name.Trim();

        var duplicate = await dbContext.ChangeTypes.AsNoTracking()
            .AnyAsync(ct => ct.Name.ToLower() == trimmedName.ToLower(), cancellationToken);
        if (duplicate)
        {
            throw ApiContractException.Conflict(
                messageAr: "يوجد نوع طلب تغيير مسجّل بهذا الاسم مسبقًا.",
                messageEn: "A change request type with this name already exists.");
        }

        await ValidateActiveNonEmptyRouteAsync(request.RouteId, cancellationToken);

        var entity = new ChangeTypeEntity
        {
            Id = Guid.NewGuid(),
            Name = trimmedName,
            RouteId = request.RouteId,
            IsActive = true
        };

        dbContext.ChangeTypes.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        await serverAuthorization.WriteAuditEventAsync(
            actor.Id,
            "CHANGE_TYPE_CREATED",
            "CHANGE_TYPE",
            entity.Id,
            "SUCCESS",
            new { entity.Name, entity.RouteId, entity.IsActive },
            cancellationToken);

        return (await BuildChangeTypeDtosAsync([entity], traceId, cancellationToken))[0];
    }

    public async Task<ChangeTypeDto> UpdateChangeTypeAsync(
        ClaimsPrincipal actorPrincipal,
        Guid typeId,
        UpdateChangeTypeRequest request,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var actor = await serverAuthorization.RequireActiveAuthenticatedUserAsync(actorPrincipal, cancellationToken);

        var entity = await dbContext.ChangeTypes
            .FirstOrDefaultAsync(ct => ct.Id == typeId, cancellationToken)
            ?? throw ApiContractException.NotFound(
                messageAr: "نوع طلب التغيير غير موجود.",
                messageEn: "Change request type was not found.");

        if (request.Name is not null)
        {
            var trimmedName = request.Name.Trim();
            var duplicate = await dbContext.ChangeTypes.AsNoTracking()
                .AnyAsync(ct => ct.Id != typeId && ct.Name.ToLower() == trimmedName.ToLower(), cancellationToken);
            if (duplicate)
            {
                throw ApiContractException.Conflict(
                    messageAr: "يوجد نوع طلب تغيير آخر مسجّل بهذا الاسم مسبقًا.",
                    messageEn: "Another change request type with this name already exists.");
            }
            entity.Name = trimmedName;
        }

        if (request.RouteId is not null)
        {
            await ValidateActiveNonEmptyRouteAsync(request.RouteId.Value, cancellationToken);
            entity.RouteId = request.RouteId.Value;
        }

        if (request.IsActive == false && entity.IsActive)
        {
            var hasActivePolicyRef = await (
                from cpt in dbContext.ChangePolicyTypes.AsNoTracking()
                join cp in dbContext.ChangePolicies.AsNoTracking() on cpt.PolicyId equals cp.Id
                where cpt.ChangeTypeId == typeId && cp.IsActive
                select cpt.Id)
                .AnyAsync(cancellationToken);

            if (hasActivePolicyRef)
            {
                throw ApiContractException.UnprocessableEntity(
                    messageAr: "لا يمكن تعطيل نوع طلب التغيير لارتباطه بسياسة تغيير نشطة (حماية المراجع).",
                    messageEn: "Cannot deactivate a change type referenced by an active change policy.");
            }
        }
        else if (request.IsActive == true && request.RouteId is null)
        {
            await ValidateActiveNonEmptyRouteAsync(entity.RouteId, cancellationToken);
        }

        var wasActive = entity.IsActive;
        if (request.IsActive is not null)
        {
            entity.IsActive = request.IsActive.Value;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var op = wasActive && !entity.IsActive
            ? "CHANGE_TYPE_DEACTIVATED"
            : (!wasActive && entity.IsActive ? "CHANGE_TYPE_ACTIVATED" : "CHANGE_TYPE_UPDATED");

        await serverAuthorization.WriteAuditEventAsync(
            actor.Id,
            op,
            "CHANGE_TYPE",
            entity.Id,
            "SUCCESS",
            new { entity.Name, entity.RouteId, entity.IsActive },
            cancellationToken);

        return (await BuildChangeTypeDtosAsync([entity], traceId, cancellationToken))[0];
    }

    public async Task<PagedResult<ChangePolicyDto>> GetChangePoliciesAsync(
        int? page,
        int? pageSize,
        string? search,
        bool? isActive,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.ChangePolicies.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(cp => cp.Name.ToLower().Contains(term));
        }
        if (isActive is not null)
        {
            query = query.Where(cp => cp.IsActive == isActive.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var (_, safePageSize, totalPages, normalizedPage) = PaginationHelper.Compute(page, pageSize, totalCount);

        var rows = await query
            .OrderBy(cp => cp.Name)
            .Skip((normalizedPage - 1) * safePageSize)
            .Take(safePageSize)
            .ToListAsync(cancellationToken);

        var dtos = await BuildChangePolicyDtosAsync(rows, traceId, cancellationToken);
        return new PagedResult<ChangePolicyDto>(dtos, normalizedPage, safePageSize, totalCount, totalPages);
    }

    public async Task<ChangePolicyDto> CreateChangePolicyAsync(
        ClaimsPrincipal actorPrincipal,
        CreateChangePolicyRequest request,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var actor = await serverAuthorization.RequireActiveAuthenticatedUserAsync(actorPrincipal, cancellationToken);
        var trimmedName = request.Name.Trim();

        var duplicate = await dbContext.ChangePolicies.AsNoTracking()
            .AnyAsync(cp => cp.Name.ToLower() == trimmedName.ToLower(), cancellationToken);
        if (duplicate)
        {
            throw ApiContractException.Conflict(
                messageAr: "توجد سياسة تغيير مسجّلة بهذا الاسم مسبقًا.",
                messageEn: "A change policy with this name already exists.");
        }

        var distinctTypeIds = await ValidateActiveChangeTypeIdsAsync(request.ChangeTypeIds, cancellationToken);

        var policy = new ChangePolicyEntity
        {
            Id = Guid.NewGuid(),
            Name = trimmedName,
            IsActive = true
        };

        await using var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        dbContext.ChangePolicies.Add(policy);
        foreach (var typeId in distinctTypeIds)
        {
            dbContext.ChangePolicyTypes.Add(new ChangePolicyTypeEntity
            {
                Id = Guid.NewGuid(),
                PolicyId = policy.Id,
                ChangeTypeId = typeId
            });
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        await serverAuthorization.WriteAuditEventAsync(
            actor.Id,
            "CHANGE_POLICY_CREATED",
            "CHANGE_POLICY",
            policy.Id,
            "SUCCESS",
            new { policy.Name, policy.IsActive, changeTypeIds = distinctTypeIds },
            cancellationToken);

        return (await BuildChangePolicyDtosAsync([policy], traceId, cancellationToken))[0];
    }

    public async Task<ChangePolicyDto> UpdateChangePolicyAsync(
        ClaimsPrincipal actorPrincipal,
        Guid policyId,
        UpdateChangePolicyRequest request,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var actor = await serverAuthorization.RequireActiveAuthenticatedUserAsync(actorPrincipal, cancellationToken);

        var policy = await dbContext.ChangePolicies
            .FirstOrDefaultAsync(cp => cp.Id == policyId, cancellationToken)
            ?? throw ApiContractException.NotFound(
                messageAr: "سياسة التغيير غير موجودة.",
                messageEn: "Change policy was not found.");

        if (request.Name is not null)
        {
            var trimmedName = request.Name.Trim();
            var duplicate = await dbContext.ChangePolicies.AsNoTracking()
                .AnyAsync(cp => cp.Id != policyId && cp.Name.ToLower() == trimmedName.ToLower(), cancellationToken);
            if (duplicate)
            {
                throw ApiContractException.Conflict(
                    messageAr: "توجد سياسة تغيير أخرى مسجّلة بهذا الاسم مسبقًا.",
                    messageEn: "Another change policy with this name already exists.");
            }
            policy.Name = trimmedName;
        }

        if (request.IsActive == false && policy.IsActive)
        {
            var hasActiveDocs = await dbContext.ApiDocuments.AsNoTracking()
                .AnyAsync(d => d.ChangePolicyId == policyId && d.State != ArchivedState, cancellationToken);
            if (hasActiveDocs)
            {
                throw ApiContractException.UnprocessableEntity(
                    messageAr: "لا يمكن تعطيل سياسة التغيير لارتباطها بوثائق API نشطة (حماية المراجع).",
                    messageEn: "Cannot deactivate a change policy referenced by active API documents.");
            }
        }

        List<Guid>? distinctTypeIds = null;
        if (request.ChangeTypeIds is not null)
        {
            distinctTypeIds = await ValidateActiveChangeTypeIdsAsync(request.ChangeTypeIds, cancellationToken);
        }

        var wasActive = policy.IsActive;
        if (request.IsActive is not null)
        {
            policy.IsActive = request.IsActive.Value;
        }

        await using var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        if (distinctTypeIds is not null)
        {
            var existingLinks = await dbContext.ChangePolicyTypes
                .Where(cpt => cpt.PolicyId == policyId)
                .ToListAsync(cancellationToken);
            dbContext.ChangePolicyTypes.RemoveRange(existingLinks);
            await dbContext.SaveChangesAsync(cancellationToken);

            foreach (var typeId in distinctTypeIds)
            {
                dbContext.ChangePolicyTypes.Add(new ChangePolicyTypeEntity
                {
                    Id = Guid.NewGuid(),
                    PolicyId = policyId,
                    ChangeTypeId = typeId
                });
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        var op = wasActive && !policy.IsActive
            ? "CHANGE_POLICY_DEACTIVATED"
            : (!wasActive && policy.IsActive ? "CHANGE_POLICY_ACTIVATED" : "CHANGE_POLICY_UPDATED");

        await serverAuthorization.WriteAuditEventAsync(
            actor.Id,
            op,
            "CHANGE_POLICY",
            policy.Id,
            "SUCCESS",
            new { policy.Name, policy.IsActive, changeTypeIds = distinctTypeIds },
            cancellationToken);

        return (await BuildChangePolicyDtosAsync([policy], traceId, cancellationToken))[0];
    }

    // ========================================================================
    // Private Validation & Projection Helpers
    // ========================================================================
    private sealed record ValidatedStageSpec(int Position, string StageName, Guid ResponsibleRoleId);

    private async Task<List<ValidatedStageSpec>> ValidateStagesAsync(
        IReadOnlyList<ApprovalRouteStageInput> inputs,
        CancellationToken cancellationToken)
    {
        if (inputs.Count == 0)
        {
            throw ApiContractException.UnprocessableEntity(
                messageAr: "يجب أن يحتوي مسار الاعتماد على مرحلة واحدة على الأقل (مسار غير صالح).",
                messageEn: "Approval route must contain at least one stage.");
        }

        var sorted = inputs.OrderBy(s => s.Position).ToList();
        var seenStageNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < sorted.Count; i++)
        {
            var expectedPosition = i + 1;
            if (sorted[i].Position != expectedPosition)
            {
                throw ApiContractException.UnprocessableEntity(
                    messageAr: $"ترتيب المراحل غير صالح؛ يجب أن يبدأ الترتيب من 1 بالتسلسل دون تكرار أو فجوات (المتوقع: {expectedPosition}).",
                    messageEn: $"Invalid stage ordering; positions must be contiguous 1..{sorted.Count} without gaps or duplicates.");
            }

            var stageName = sorted[i].StageName.Trim();
            if (!seenStageNames.Add(stageName))
            {
                throw ApiContractException.Conflict(
                    messageAr: $"لا يمكن تكرار اسم المرحلة ({stageName}) داخل نفس مسار الاعتماد.",
                    messageEn: $"Duplicate stage name '{stageName}' inside the same route is not allowed.");
            }
        }

        var roleIds = sorted.Select(s => s.ResponsibleRoleId).Distinct().ToList();
        var roles = await dbContext.Roles.AsNoTracking()
            .Where(r => roleIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, cancellationToken);

        foreach (var stage in sorted)
        {
            if (!roles.TryGetValue(stage.ResponsibleRoleId, out var role))
            {
                throw ApiContractException.UnprocessableEntity(
                    messageAr: "الدور المسؤول المحدد للمرحلة غير موجود.",
                    messageEn: "The responsible role specified for the stage was not found.");
            }

            if (!role.IsActive)
            {
                throw ApiContractException.UnprocessableEntity(
                    messageAr: $"لا يمكن إسناد المرحلة إلى الدور المعطّل ({role.Name}).",
                    messageEn: $"Inactive role '{role.Name}' cannot be assigned to an approval route stage.");
            }
        }

        return sorted.Select(s => new ValidatedStageSpec(s.Position, s.StageName.Trim(), s.ResponsibleRoleId)).ToList();
    }

    private async Task ValidateActiveNonEmptyRouteAsync(Guid routeId, CancellationToken cancellationToken)
    {
        var route = await dbContext.ApprovalRoutes.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == routeId, cancellationToken);
        if (route is null)
        {
            throw ApiContractException.UnprocessableEntity(
                messageAr: "مسار الاعتماد المحدد غير موجود.",
                messageEn: "The selected approval route does not exist.");
        }

        if (!route.IsActive)
        {
            throw ApiContractException.UnprocessableEntity(
                messageAr: $"لا يمكن الربط بمسار اعتماد معطّل ({route.Name}).",
                messageEn: $"Inactive approval route '{route.Name}' cannot be linked.");
        }

        var stageCount = await dbContext.ApprovalRouteStages.AsNoTracking()
            .CountAsync(s => s.RouteId == routeId, cancellationToken);
        if (stageCount == 0)
        {
            throw ApiContractException.UnprocessableEntity(
                messageAr: $"مسار الاعتماد ({route.Name}) لا يحتوي على أي مرحلة (مسار غير صالح للربط).",
                messageEn: $"Approval route '{route.Name}' has zero stages and cannot be linked.");
        }
    }

    private async Task<List<Guid>> ValidateActiveChangeTypeIdsAsync(
        IReadOnlyList<Guid> changeTypeIds,
        CancellationToken cancellationToken)
    {
        var distinct = changeTypeIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (distinct.Count == 0)
        {
            throw ApiContractException.UnprocessableEntity(
                messageAr: "يجب اختيار نوع طلب تغيير واحد صالح على الأقل.",
                messageEn: "At least one valid change request type is required.");
        }

        var types = await dbContext.ChangeTypes.AsNoTracking()
            .Where(ct => distinct.Contains(ct.Id))
            .ToDictionaryAsync(ct => ct.Id, cancellationToken);

        foreach (var id in distinct)
        {
            if (!types.TryGetValue(id, out var ct))
            {
                throw ApiContractException.UnprocessableEntity(
                    messageAr: "أحد أنواع طلبات التغيير المحددة غير موجود.",
                    messageEn: "One of the selected change types was not found.");
            }
            if (!ct.IsActive)
            {
                throw ApiContractException.UnprocessableEntity(
                    messageAr: $"لا يمكن تضمين نوع تغيير معطّل ({ct.Name}) داخل سياسة التغيير.",
                    messageEn: $"Inactive change type '{ct.Name}' cannot be included in a change policy.");
            }
        }

        return distinct;
    }

    private async Task<List<ApprovalRouteDto>> BuildApprovalRouteDtosAsync(
        IReadOnlyList<ApprovalRouteEntity> routes,
        string traceId,
        CancellationToken cancellationToken)
    {
        if (routes.Count == 0)
        {
            return [];
        }

        var routeIds = routes.Select(r => r.Id).ToList();

        var stageRows = await (
            from s in dbContext.ApprovalRouteStages.AsNoTracking()
            join role in dbContext.Roles.AsNoTracking() on s.ResponsibleRoleId equals role.Id
            where routeIds.Contains(s.RouteId)
            orderby s.Position
            select new { Stage = s, Role = role })
            .ToListAsync(cancellationToken);

        var stagesByRoute = stageRows
            .GroupBy(x => x.Stage.RouteId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<ApprovalRouteStageDto>)g.Select(x => new ApprovalRouteStageDto(
                    x.Stage.Id,
                    x.Stage.Position,
                    x.Stage.StageName,
                    x.Role.Id,
                    x.Role.Name,
                    x.Role.IsActive)).ToList());

        var ctCounts = await dbContext.ChangeTypes.AsNoTracking()
            .Where(ct => routeIds.Contains(ct.RouteId))
            .GroupBy(ct => ct.RouteId)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, cancellationToken);

        var docCounts = await dbContext.ApiDocuments.AsNoTracking()
            .Where(d => routeIds.Contains(d.ApprovalRouteId))
            .GroupBy(d => d.ApprovalRouteId)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, cancellationToken);

        var runCounts = await dbContext.WorkflowRuns.AsNoTracking()
            .Where(wr => routeIds.Contains(wr.RouteId) && wr.Status == InProgressStatus)
            .GroupBy(wr => wr.RouteId)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, cancellationToken);

        return routes.Select(r => new ApprovalRouteDto(
            r.Id,
            r.Name,
            r.IsActive,
            stagesByRoute.GetValueOrDefault(r.Id, []),
            ctCounts.GetValueOrDefault(r.Id, 0),
            docCounts.GetValueOrDefault(r.Id, 0),
            runCounts.GetValueOrDefault(r.Id, 0),
            traceId)).ToList();
    }

    private async Task<List<ChangeTypeDto>> BuildChangeTypeDtosAsync(
        IReadOnlyList<ChangeTypeEntity> changeTypes,
        string traceId,
        CancellationToken cancellationToken)
    {
        if (changeTypes.Count == 0)
        {
            return [];
        }

        var typeIds = changeTypes.Select(ct => ct.Id).ToList();
        var routeIds = changeTypes.Select(ct => ct.RouteId).Distinct().ToList();

        var routes = await dbContext.ApprovalRoutes.AsNoTracking()
            .Where(r => routeIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, cancellationToken);

        var stageCounts = await dbContext.ApprovalRouteStages.AsNoTracking()
            .Where(s => routeIds.Contains(s.RouteId))
            .GroupBy(s => s.RouteId)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, cancellationToken);

        var policyCounts = await dbContext.ChangePolicyTypes.AsNoTracking()
            .Where(cpt => typeIds.Contains(cpt.ChangeTypeId))
            .GroupBy(cpt => cpt.ChangeTypeId)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, cancellationToken);

        var crCounts = await dbContext.ChangeRequests.AsNoTracking()
            .Where(cr => typeIds.Contains(cr.ChangeTypeId))
            .GroupBy(cr => cr.ChangeTypeId)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, cancellationToken);

        return changeTypes.Select(ct =>
        {
            var route = routes.GetValueOrDefault(ct.RouteId);
            return new ChangeTypeDto(
                ct.Id,
                ct.Name,
                ct.RouteId,
                route?.Name ?? string.Empty,
                route?.IsActive ?? false,
                stageCounts.GetValueOrDefault(ct.RouteId, 0),
                ct.IsActive,
                policyCounts.GetValueOrDefault(ct.Id, 0),
                crCounts.GetValueOrDefault(ct.Id, 0),
                traceId);
        }).ToList();
    }

    private async Task<List<ChangePolicyDto>> BuildChangePolicyDtosAsync(
        IReadOnlyList<ChangePolicyEntity> policies,
        string traceId,
        CancellationToken cancellationToken)
    {
        if (policies.Count == 0)
        {
            return [];
        }

        var policyIds = policies.Select(p => p.Id).ToList();

        var typeLinks = await (
            from cpt in dbContext.ChangePolicyTypes.AsNoTracking()
            join ct in dbContext.ChangeTypes.AsNoTracking() on cpt.ChangeTypeId equals ct.Id
            join r in dbContext.ApprovalRoutes.AsNoTracking() on ct.RouteId equals r.Id
            where policyIds.Contains(cpt.PolicyId)
            orderby ct.Name
            select new { cpt.PolicyId, ChangeType = ct, Route = r })
            .ToListAsync(cancellationToken);

        var typesByPolicy = typeLinks
            .GroupBy(x => x.PolicyId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<ChangePolicyTypeSummaryDto>)g.Select(x => new ChangePolicyTypeSummaryDto(
                    x.ChangeType.Id,
                    x.ChangeType.Name,
                    x.Route.Id,
                    x.Route.Name,
                    x.ChangeType.IsActive)).ToList());

        var docCounts = await dbContext.ApiDocuments.AsNoTracking()
            .Where(d => policyIds.Contains(d.ChangePolicyId))
            .GroupBy(d => d.ChangePolicyId)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, cancellationToken);

        return policies.Select(p => new ChangePolicyDto(
            p.Id,
            p.Name,
            p.IsActive,
            typesByPolicy.GetValueOrDefault(p.Id, []),
            docCounts.GetValueOrDefault(p.Id, 0),
            traceId)).ToList();
    }
}
