using System.Security.Claims;
using System.Text.Json;
using ApiGovernance.Modules.Configuration.Contracts;
using ApiGovernance.Shared.Contracts;
using ApiGovernance.Shared.Persistence;
using ApiGovernance.Shared.Persistence.Entities;
using ApiGovernance.Shared.Security;
using Microsoft.EntityFrameworkCore;

namespace ApiGovernance.Modules.Configuration.Services;

public interface IRequirementTemplateService
{
    Task<IReadOnlyList<RequirementFileTypeOptionDto>> GetActiveFileTypeOptionsAsync(
        CancellationToken cancellationToken = default);

    Task<PagedResult<FileRequirementTemplateDto>> GetRequirementTemplatesAsync(
        int? page,
        int? pageSize,
        string? search,
        bool? isActive,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<FileRequirementTemplateDto> GetRequirementTemplateAsync(
        Guid templateId,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RequirementTemplateSnapshotDto>> GetRequirementTemplateSnapshotsAsync(
        Guid templateId,
        CancellationToken cancellationToken = default);

    Task<FileRequirementTemplateDto> CreateRequirementTemplateAsync(
        ClaimsPrincipal actorPrincipal,
        CreateRequirementTemplateRequest request,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<FileRequirementTemplateDto> UpdateRequirementTemplateAsync(
        ClaimsPrincipal actorPrincipal,
        Guid templateId,
        UpdateRequirementTemplateRequest request,
        string traceId,
        CancellationToken cancellationToken = default);

    Task DeleteRequirementTemplateAsync(
        ClaimsPrincipal actorPrincipal,
        Guid templateId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Implements Task M05 File Requirement Templates (UI-23) with immutable append-only historical snapshots
/// recorded in audit_events (OP-01) and pre-use structural protection for active API document references.
/// </summary>
public sealed class RequirementTemplateService(
    ApiGovernanceDbContext dbContext,
    IServerAuthorizationService serverAuthorization) : IRequirementTemplateService
{
    private const string SnapshotResourceKind = "FILE_REQUIREMENT_TEMPLATE";
    private const string ArchivedState = "ARCHIVED";

    private sealed record TemplateSnapshotPayload(
        int VersionNumber,
        Guid TemplateId,
        string TemplateName,
        bool IsActive,
        DateTimeOffset CapturedAtUtc,
        List<FileRequirementRuleDto> Rules);

    public async Task<IReadOnlyList<RequirementFileTypeOptionDto>> GetActiveFileTypeOptionsAsync(
        CancellationToken cancellationToken = default)
    {
        var fileTypes = await dbContext.FileTypes.AsNoTracking()
            .Where(ft => ft.IsActive)
            .OrderBy(ft => ft.DisplayName)
            .ToListAsync(cancellationToken);

        var ids = fileTypes.Select(f => f.Id).ToList();
        var exts = ids.Count == 0
            ? []
            : await dbContext.FileTypeExtensions.AsNoTracking()
                .Where(e => ids.Contains(e.FileTypeId))
                .OrderBy(e => e.Extension)
                .ToListAsync(cancellationToken);

        var extMap = exts.GroupBy(e => e.FileTypeId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(x => x.Extension).ToList());

        return fileTypes.Select(ft => new RequirementFileTypeOptionDto(
            ft.Id,
            ft.CatalogCode,
            ft.DisplayName,
            extMap.GetValueOrDefault(ft.Id, []),
            ft.MaxSizeBytes,
            ft.IsActive)).ToList();
    }

    public async Task<PagedResult<FileRequirementTemplateDto>> GetRequirementTemplatesAsync(
        int? page,
        int? pageSize,
        string? search,
        bool? isActive,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.FileRequirementTemplates.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(t => t.Name.ToLower().Contains(term));
        }
        if (isActive is not null)
        {
            query = query.Where(t => t.IsActive == isActive.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var (_, safePageSize, totalPages, normalizedPage) = PaginationHelper.Compute(page, pageSize, totalCount);

        var rows = await query
            .OrderBy(t => t.Name)
            .Skip((normalizedPage - 1) * safePageSize)
            .Take(safePageSize)
            .ToListAsync(cancellationToken);

        var dtos = await BuildTemplateDtosAsync(rows, traceId, cancellationToken);
        return new PagedResult<FileRequirementTemplateDto>(dtos, normalizedPage, safePageSize, totalCount, totalPages);
    }

    public async Task<FileRequirementTemplateDto> GetRequirementTemplateAsync(
        Guid templateId,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var template = await dbContext.FileRequirementTemplates.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == templateId, cancellationToken)
            ?? throw ApiContractException.NotFound(
                messageAr: "قالب متطلبات الملفات غير موجود.",
                messageEn: "File requirement template was not found.");

        var list = await BuildTemplateDtosAsync([template], traceId, cancellationToken);
        return list[0];
    }

    public async Task<IReadOnlyList<RequirementTemplateSnapshotDto>> GetRequirementTemplateSnapshotsAsync(
        Guid templateId,
        CancellationToken cancellationToken = default)
    {
        var exists = await dbContext.FileRequirementTemplates.AsNoTracking()
            .AnyAsync(t => t.Id == templateId, cancellationToken);
        if (!exists)
        {
            throw ApiContractException.NotFound(
                messageAr: "قالب متطلبات الملفات غير موجود.",
                messageEn: "File requirement template was not found.");
        }

        var auditRows = await dbContext.AuditEvents.AsNoTracking()
            .Where(a => a.ResourceKind == SnapshotResourceKind
                        && a.ResourceId == templateId
                        && a.Result == "SUCCESS")
            .OrderByDescending(a => a.RecordedAt)
            .ToListAsync(cancellationToken);

        var actorIds = auditRows.Where(a => a.ActorUserId.HasValue)
            .Select(a => a.ActorUserId!.Value)
            .Distinct()
            .ToList();

        var actorNames = actorIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await dbContext.Users.AsNoTracking()
                .Where(u => actorIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.FullName, cancellationToken);

        var result = new List<RequirementTemplateSnapshotDto>(auditRows.Count);
        var fallbackVersion = auditRows.Count;

        foreach (var row in auditRows)
        {
            TemplateSnapshotPayload? payload = null;
            if (!string.IsNullOrWhiteSpace(row.ChangeSummaryJson))
            {
                try
                {
                    payload = JsonSerializer.Deserialize<TemplateSnapshotPayload>(row.ChangeSummaryJson);
                }
                catch (JsonException)
                {
                    payload = null;
                }
            }

            var actorName = row.ActorUserId.HasValue
                ? actorNames.GetValueOrDefault(row.ActorUserId.Value, "System")
                : "System";

            result.Add(new RequirementTemplateSnapshotDto(
                row.Id,
                payload?.VersionNumber > 0 ? payload.VersionNumber : fallbackVersion,
                row.Operation,
                payload?.CapturedAtUtc ?? row.RecordedAt,
                row.ActorUserId,
                actorName,
                payload?.TemplateName ?? string.Empty,
                payload?.IsActive ?? true,
                payload?.Rules ?? []));

            fallbackVersion--;
        }

        return result;
    }

    public async Task<FileRequirementTemplateDto> CreateRequirementTemplateAsync(
        ClaimsPrincipal actorPrincipal,
        CreateRequirementTemplateRequest request,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var actor = await serverAuthorization.RequireActiveAuthenticatedUserAsync(actorPrincipal, cancellationToken);
        var trimmedName = request.Name.Trim();

        var duplicate = await dbContext.FileRequirementTemplates.AsNoTracking()
            .AnyAsync(t => t.Name.ToLower() == trimmedName.ToLower(), cancellationToken);
        if (duplicate)
        {
            throw ApiContractException.Conflict(
                messageAr: "يوجد قالب متطلبات ملفات مسجّل بهذا الاسم مسبقًا.",
                messageEn: "A file requirement template with this name already exists.");
        }

        var validatedRules = await ValidateRuleInputsAsync(request.Rules, cancellationToken);

        var template = new FileRequirementTemplateEntity
        {
            Id = Guid.NewGuid(),
            Name = trimmedName,
            IsActive = true
        };

        await using var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        dbContext.FileRequirementTemplates.Add(template);
        foreach (var rule in validatedRules)
        {
            dbContext.FileRequirementRules.Add(new FileRequirementRuleEntity
            {
                Id = Guid.NewGuid(),
                TemplateId = template.Id,
                FileTypeId = rule.FileTypeId,
                RequirementMode = rule.Mode,
                MinCount = rule.MinCount,
                MaxCount = rule.MaxCount,
                ConditionSpecJson = rule.ConditionSpecJson
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var builtDto = (await BuildTemplateDtosWithoutSnapshotsAsync([template], traceId, cancellationToken))[0];
        var snapshotPayload = new TemplateSnapshotPayload(
            VersionNumber: 1,
            TemplateId: template.Id,
            TemplateName: template.Name,
            IsActive: template.IsActive,
            CapturedAtUtc: DateTimeOffset.UtcNow,
            Rules: builtDto.Rules.ToList());

        await serverAuthorization.WriteAuditEventAsync(
            actor.Id,
            "REQUIREMENT_TEMPLATE_CREATED",
            SnapshotResourceKind,
            template.Id,
            "SUCCESS",
            snapshotPayload,
            cancellationToken);

        await tx.CommitAsync(cancellationToken);

        return await GetRequirementTemplateAsync(template.Id, traceId, cancellationToken);
    }

    public async Task<FileRequirementTemplateDto> UpdateRequirementTemplateAsync(
        ClaimsPrincipal actorPrincipal,
        Guid templateId,
        UpdateRequirementTemplateRequest request,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var actor = await serverAuthorization.RequireActiveAuthenticatedUserAsync(actorPrincipal, cancellationToken);

        var template = await dbContext.FileRequirementTemplates
            .FirstOrDefaultAsync(t => t.Id == templateId, cancellationToken)
            ?? throw ApiContractException.NotFound(
                messageAr: "قالب متطلبات الملفات غير موجود.",
                messageEn: "File requirement template was not found.");

        var hasActiveDocuments = await dbContext.ApiDocuments.AsNoTracking()
            .AnyAsync(d => d.RequirementTemplateId == templateId && d.State != ArchivedState, cancellationToken);

        if (request.IsActive == false && template.IsActive && hasActiveDocuments)
        {
            throw ApiContractException.UnprocessableEntity(
                messageAr: "لا يمكن تعطيل قالب المتطلبات لارتباطه بوثائق API نشطة (حماية المراجع).",
                messageEn: "Cannot deactivate a requirement template referenced by active API documents.");
        }

        if (hasActiveDocuments && (request.Name is not null || request.Rules is not null))
        {
            throw ApiContractException.UnprocessableEntity(
                messageAr: "لا يمكن تعديل اسم أو قواعد قالب متطلبات مستخدم حاليًا في وثائق API نشطة؛ يمكنك إنشاء قالب جديد أو مراجعة اللقطات التاريخية.",
                messageEn: "Cannot modify name or rules of a requirement template referenced by active API documents.");
        }

        if (request.Name is not null)
        {
            var trimmedName = request.Name.Trim();
            var duplicate = await dbContext.FileRequirementTemplates.AsNoTracking()
                .AnyAsync(t => t.Id != templateId && t.Name.ToLower() == trimmedName.ToLower(), cancellationToken);
            if (duplicate)
            {
                throw ApiContractException.Conflict(
                    messageAr: "يوجد قالب متطلبات ملفات آخر مسجّل بهذا الاسم مسبقًا.",
                    messageEn: "Another file requirement template with this name already exists.");
            }
            template.Name = trimmedName;
        }

        List<ValidatedRuleSpec>? validatedRules = null;
        if (request.Rules is not null)
        {
            validatedRules = await ValidateRuleInputsAsync(request.Rules, cancellationToken);
        }

        var wasActive = template.IsActive;
        if (request.IsActive is not null)
        {
            template.IsActive = request.IsActive.Value;
        }

        await using var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        if (validatedRules is not null)
        {
            var existingRules = await dbContext.FileRequirementRules
                .Where(r => r.TemplateId == templateId)
                .ToListAsync(cancellationToken);

            dbContext.FileRequirementRules.RemoveRange(existingRules);
            await dbContext.SaveChangesAsync(cancellationToken);

            foreach (var rule in validatedRules)
            {
                dbContext.FileRequirementRules.Add(new FileRequirementRuleEntity
                {
                    Id = Guid.NewGuid(),
                    TemplateId = template.Id,
                    FileTypeId = rule.FileTypeId,
                    RequirementMode = rule.Mode,
                    MinCount = rule.MinCount,
                    MaxCount = rule.MaxCount,
                    ConditionSpecJson = rule.ConditionSpecJson
                });
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var existingSnapshotCount = await dbContext.AuditEvents.AsNoTracking()
            .CountAsync(a => a.ResourceKind == SnapshotResourceKind
                             && a.ResourceId == template.Id
                             && a.Result == "SUCCESS", cancellationToken);

        var builtDto = (await BuildTemplateDtosWithoutSnapshotsAsync([template], traceId, cancellationToken))[0];
        var snapshotPayload = new TemplateSnapshotPayload(
            VersionNumber: existingSnapshotCount + 1,
            TemplateId: template.Id,
            TemplateName: template.Name,
            IsActive: template.IsActive,
            CapturedAtUtc: DateTimeOffset.UtcNow,
            Rules: builtDto.Rules.ToList());

        var op = wasActive && !template.IsActive
            ? "REQUIREMENT_TEMPLATE_DEACTIVATED"
            : (!wasActive && template.IsActive ? "REQUIREMENT_TEMPLATE_ACTIVATED" : "REQUIREMENT_TEMPLATE_UPDATED");

        await serverAuthorization.WriteAuditEventAsync(
            actor.Id,
            op,
            SnapshotResourceKind,
            template.Id,
            "SUCCESS",
            snapshotPayload,
            cancellationToken);

        await tx.CommitAsync(cancellationToken);

        return await GetRequirementTemplateAsync(template.Id, traceId, cancellationToken);
    }

    public async Task DeleteRequirementTemplateAsync(
        ClaimsPrincipal actorPrincipal,
        Guid templateId,
        CancellationToken cancellationToken = default)
    {
        var actor = await serverAuthorization.RequireActiveAuthenticatedUserAsync(actorPrincipal, cancellationToken);

        var template = await dbContext.FileRequirementTemplates
            .FirstOrDefaultAsync(t => t.Id == templateId, cancellationToken)
            ?? throw ApiContractException.NotFound(
                messageAr: "قالب متطلبات الملفات غير موجود.",
                messageEn: "File requirement template was not found.");

        var hasDocuments = await dbContext.ApiDocuments.AsNoTracking()
            .AnyAsync(d => d.RequirementTemplateId == templateId, cancellationToken);
        if (hasDocuments)
        {
            throw ApiContractException.UnprocessableEntity(
                messageAr: "لا يمكن حذف قالب متطلبات مرتبط بوثائق API قائمة.",
                messageEn: "Cannot delete a requirement template referenced by existing API documents.");
        }

        await using var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var rules = await dbContext.FileRequirementRules
            .Where(r => r.TemplateId == templateId)
            .ToListAsync(cancellationToken);
        if (rules.Count > 0)
        {
            dbContext.FileRequirementRules.RemoveRange(rules);
        }

        dbContext.FileRequirementTemplates.Remove(template);
        await dbContext.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        await serverAuthorization.WriteAuditEventAsync(
            actor.Id,
            "REQUIREMENT_TEMPLATE_DELETED",
            SnapshotResourceKind,
            template.Id,
            "SUCCESS",
            new { template.Name },
            cancellationToken);
    }

    private sealed record ValidatedRuleSpec(
        Guid FileTypeId,
        string Mode,
        int MinCount,
        int MaxCount,
        string? ConditionSpecJson);

    private async Task<List<ValidatedRuleSpec>> ValidateRuleInputsAsync(
        IReadOnlyList<RequirementRuleInput> inputs,
        CancellationToken cancellationToken)
    {
        if (inputs.Count == 0)
        {
            throw ApiContractException.UnprocessableEntity(
                messageAr: "يجب تعريف قاعدة ملف واحدة على الأقل داخل القالب.",
                messageEn: "At least one file requirement rule is required.");
        }

        var seenFileTypes = new HashSet<Guid>();
        foreach (var item in inputs)
        {
            if (!seenFileTypes.Add(item.FileTypeId))
            {
                throw ApiContractException.Conflict(
                    messageAr: "لا يمكن تكرار نوع الملف نفسه داخل القالب الواحد.",
                    messageEn: "Duplicate file type rules inside the same template are not allowed.");
            }
        }

        var fileTypeIds = seenFileTypes.ToList();
        var fileTypes = await dbContext.FileTypes.AsNoTracking()
            .Where(ft => fileTypeIds.Contains(ft.Id))
            .ToDictionaryAsync(ft => ft.Id, cancellationToken);

        var result = new List<ValidatedRuleSpec>(inputs.Count);
        foreach (var item in inputs)
        {
            if (!fileTypes.TryGetValue(item.FileTypeId, out var ft))
            {
                throw ApiContractException.UnprocessableEntity(
                    messageAr: "نوع الملف المحدد غير موجود.",
                    messageEn: "The selected file type does not exist.");
            }

            if (!ft.IsActive)
            {
                throw ApiContractException.UnprocessableEntity(
                    messageAr: $"لا يمكن استخدام نوع الملف المعطّل ({ft.DisplayName}) داخل قالب جديد أو محدّث.",
                    messageEn: $"Inactive file type '{ft.DisplayName}' cannot be assigned to a requirement template.");
            }

            var normalizedMode = RequirementRuleModes.Normalize(item.RequirementMode);
            string? normalizedJson = null;
            if (!string.IsNullOrWhiteSpace(item.ConditionSpecJson))
            {
                using var doc = JsonDocument.Parse(item.ConditionSpecJson);
                normalizedJson = JsonSerializer.Serialize(doc.RootElement);
            }

            result.Add(new ValidatedRuleSpec(
                item.FileTypeId,
                normalizedMode,
                item.MinCount,
                item.MaxCount,
                normalizedJson));
        }

        return result;
    }

    private async Task<List<FileRequirementTemplateDto>> BuildTemplateDtosAsync(
        IReadOnlyList<FileRequirementTemplateEntity> templates,
        string traceId,
        CancellationToken cancellationToken)
    {
        if (templates.Count == 0)
        {
            return [];
        }

        var baseList = await BuildTemplateDtosWithoutSnapshotsAsync(templates, traceId, cancellationToken);
        var templateIds = templates.Select(t => t.Id).ToList();

        var snapshotStats = await dbContext.AuditEvents.AsNoTracking()
            .Where(a => a.ResourceKind == SnapshotResourceKind
                        && a.ResourceId != null
                        && templateIds.Contains(a.ResourceId.Value)
                        && a.Result == "SUCCESS")
            .GroupBy(a => a.ResourceId!.Value)
            .Select(g => new
            {
                TemplateId = g.Key,
                Count = g.Count(),
                LatestAt = g.Max(x => x.RecordedAt)
            })
            .ToDictionaryAsync(x => x.TemplateId, cancellationToken);

        return baseList.Select(dto =>
        {
            var stats = snapshotStats.GetValueOrDefault(dto.Id);
            return dto with
            {
                SnapshotCount = stats?.Count ?? 0,
                LatestSnapshotAt = stats?.LatestAt
            };
        }).ToList();
    }

    private async Task<List<FileRequirementTemplateDto>> BuildTemplateDtosWithoutSnapshotsAsync(
        IReadOnlyList<FileRequirementTemplateEntity> templates,
        string traceId,
        CancellationToken cancellationToken)
    {
        var templateIds = templates.Select(t => t.Id).ToList();

        var ruleRows = await (
            from r in dbContext.FileRequirementRules.AsNoTracking()
            join ft in dbContext.FileTypes.AsNoTracking() on r.FileTypeId equals ft.Id
            where templateIds.Contains(r.TemplateId)
            orderby ft.DisplayName
            select new { Rule = r, FileType = ft })
            .ToListAsync(cancellationToken);

        var fileTypeIds = ruleRows.Select(x => x.FileType.Id).Distinct().ToList();
        var exts = fileTypeIds.Count == 0
            ? []
            : await dbContext.FileTypeExtensions.AsNoTracking()
                .Where(e => fileTypeIds.Contains(e.FileTypeId))
                .OrderBy(e => e.Extension)
                .ToListAsync(cancellationToken);

        var extMap = exts.GroupBy(e => e.FileTypeId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(x => x.Extension).ToList());

        var rulesByTemplate = ruleRows
            .GroupBy(x => x.Rule.TemplateId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<FileRequirementRuleDto>)g.Select(x => new FileRequirementRuleDto(
                    x.Rule.Id,
                    x.FileType.Id,
                    x.FileType.DisplayName,
                    x.FileType.CatalogCode,
                    extMap.GetValueOrDefault(x.FileType.Id, []),
                    x.FileType.IsActive,
                    x.Rule.RequirementMode,
                    x.Rule.MinCount,
                    x.Rule.MaxCount,
                    x.Rule.ConditionSpecJson)).ToList());

        var docCounts = await dbContext.ApiDocuments.AsNoTracking()
            .Where(d => templateIds.Contains(d.RequirementTemplateId))
            .GroupBy(d => d.RequirementTemplateId)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, cancellationToken);

        return templates.Select(t => new FileRequirementTemplateDto(
            t.Id,
            t.Name,
            t.IsActive,
            rulesByTemplate.GetValueOrDefault(t.Id, []),
            docCounts.GetValueOrDefault(t.Id, 0),
            SnapshotCount: 0,
            LatestSnapshotAt: null,
            TraceId: traceId)).ToList();
    }
}

