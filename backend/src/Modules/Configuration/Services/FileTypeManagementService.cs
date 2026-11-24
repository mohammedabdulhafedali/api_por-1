using System.Security.Claims;
using ApiGovernance.Modules.Configuration.Contracts;
using ApiGovernance.Shared.Contracts;
using ApiGovernance.Shared.Persistence;
using ApiGovernance.Shared.Persistence.Entities;
using ApiGovernance.Shared.Security;
using Microsoft.EntityFrameworkCore;

namespace ApiGovernance.Modules.Configuration.Services;

public interface IFileTypeManagementService
{
    IReadOnlyList<FileTypeCatalogEntryDto> GetCommonCatalog();

    Task<PagedResult<FileTypeDto>> GetFileTypesAsync(
        int? page,
        int? pageSize,
        string? search,
        bool? isActive,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<FileTypeDto> CreateFileTypeAsync(
        ClaimsPrincipal actorPrincipal,
        CreateFileTypeRequest request,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<FileTypeDto> UpdateFileTypeAsync(
        ClaimsPrincipal actorPrincipal,
        Guid fileTypeId,
        UpdateFileTypeRequest request,
        string traceId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Implements Task M04 File Types dictionary management (UI-04):
/// - Restricted to curated CommonFileTypeCatalog options
/// - Auto-populates and strictly validates extensions against catalog (blocks arbitrary/unsafe extensions)
/// - Supports Word template reference ONLY for WORD_DOC
/// - Protects active file requirement template references on deactivation
/// </summary>
public sealed class FileTypeManagementService(
    ApiGovernanceDbContext dbContext,
    IServerAuthorizationService serverAuthorization) : IFileTypeManagementService
{
    public IReadOnlyList<FileTypeCatalogEntryDto> GetCommonCatalog()
        => CommonFileTypeCatalog.Entries;

    public async Task<PagedResult<FileTypeDto>> GetFileTypesAsync(
        int? page,
        int? pageSize,
        string? search,
        bool? isActive,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.FileTypes.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(ft =>
                ft.DisplayName.ToLower().Contains(term) ||
                ft.CatalogCode.ToLower().Contains(term));
        }
        if (isActive is not null)
        {
            query = query.Where(ft => ft.IsActive == isActive.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var (_, safePageSize, totalPages, normalizedPage) = PaginationHelper.Compute(page, pageSize, totalCount);

        var rows = await query
            .OrderBy(ft => ft.DisplayName)
            .Skip((normalizedPage - 1) * safePageSize)
            .Take(safePageSize)
            .ToListAsync(cancellationToken);

        var ids = rows.Select(r => r.Id).ToList();
        var extRows = ids.Count == 0
            ? []
            : await dbContext.FileTypeExtensions.AsNoTracking()
                .Where(e => ids.Contains(e.FileTypeId))
                .OrderBy(e => e.Extension)
                .ToListAsync(cancellationToken);

        var extByFileType = extRows
            .GroupBy(e => e.FileTypeId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(x => x.Extension).ToList());

        var ruleCounts = ids.Count == 0
            ? new Dictionary<Guid, int>()
            : await dbContext.FileRequirementRules.AsNoTracking()
                .Where(r => ids.Contains(r.FileTypeId))
                .GroupBy(r => r.FileTypeId)
                .Select(g => new { Id = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Id, x => x.Count, cancellationToken);

        var items = rows.Select(ft =>
        {
            var supportsWord = string.Equals(ft.CatalogCode, CommonFileTypeCatalog.WordCatalogCode, StringComparison.OrdinalIgnoreCase);
            return new FileTypeDto(
                ft.Id,
                ft.CatalogCode,
                ft.DisplayName,
                ft.MaxSizeBytes,
                ft.WordTemplateObjectId,
                ft.IsActive,
                supportsWord,
                extByFileType.GetValueOrDefault(ft.Id, []),
                ruleCounts.GetValueOrDefault(ft.Id, 0),
                traceId);
        }).ToList();

        return new PagedResult<FileTypeDto>(items, normalizedPage, safePageSize, totalCount, totalPages);
    }

    public async Task<FileTypeDto> CreateFileTypeAsync(
        ClaimsPrincipal actorPrincipal,
        CreateFileTypeRequest request,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var actor = await serverAuthorization.RequireActiveAuthenticatedUserAsync(actorPrincipal, cancellationToken);

        if (!CommonFileTypeCatalog.TryGet(request.CatalogCode, out var catalogEntry))
        {
            throw ApiContractException.UnprocessableEntity(
                messageAr: "نوع الملف المحدد غير مدرج ضمن قاموس الأنواع الشائعة المعتمد.",
                messageEn: "The selected CatalogCode is not part of the approved common file types catalog.");
        }

        if (await dbContext.FileTypes.AsNoTracking()
                .AnyAsync(ft => ft.CatalogCode.ToUpper() == catalogEntry.CatalogCode.ToUpper(), cancellationToken))
        {
            throw ApiContractException.Conflict(
                messageAr: "نوع الملف المحدد معرّف مسبقًا في قاموس المنصة.",
                messageEn: "A file type for this catalog code already exists.");
        }

        await ValidateWordTemplateReferenceAsync(catalogEntry, request.WordTemplateObjectId, cancellationToken);
        var resolvedExtensions = ResolveAndVerifyCatalogExtensions(catalogEntry, request.Extensions);

        var entity = new FileTypeEntity
        {
            Id = Guid.NewGuid(),
            CatalogCode = catalogEntry.CatalogCode,
            DisplayName = request.DisplayName.Trim(),
            MaxSizeBytes = request.MaxSizeBytes,
            WordTemplateObjectId = catalogEntry.SupportsWordTemplate ? request.WordTemplateObjectId : null,
            IsActive = true
        };

        await using var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        dbContext.FileTypes.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        foreach (var ext in resolvedExtensions)
        {
            dbContext.FileTypeExtensions.Add(new FileTypeExtensionEntity
            {
                Id = Guid.NewGuid(),
                FileTypeId = entity.Id,
                Extension = ext
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        await serverAuthorization.WriteAuditEventAsync(
            actor.Id,
            "FILE_TYPE_CREATED",
            "FILE_TYPE",
            entity.Id,
            "SUCCESS",
            new { entity.CatalogCode, entity.DisplayName, entity.MaxSizeBytes, extensions = resolvedExtensions },
            cancellationToken);

        return new FileTypeDto(
            entity.Id,
            entity.CatalogCode,
            entity.DisplayName,
            entity.MaxSizeBytes,
            entity.WordTemplateObjectId,
            entity.IsActive,
            catalogEntry.SupportsWordTemplate,
            resolvedExtensions,
            0,
            traceId);
    }

    public async Task<FileTypeDto> UpdateFileTypeAsync(
        ClaimsPrincipal actorPrincipal,
        Guid fileTypeId,
        UpdateFileTypeRequest request,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var actor = await serverAuthorization.RequireActiveAuthenticatedUserAsync(actorPrincipal, cancellationToken);
        var entity = await dbContext.FileTypes.FirstOrDefaultAsync(ft => ft.Id == fileTypeId, cancellationToken);
        if (entity is null)
        {
            throw ApiContractException.NotFound(
                messageAr: "نوع الملف المطلوب غير موجود.",
                messageEn: "The requested file type was not found.");
        }

        if (!CommonFileTypeCatalog.TryGet(entity.CatalogCode, out var catalogEntry))
        {
            throw ApiContractException.UnprocessableEntity(
                messageAr: "رمز نوع الملف المخزن غير مطابق للقاموس المعتمد.",
                messageEn: "Stored file type catalog code does not match the approved catalog.");
        }

        if (request.DisplayName is not null)
        {
            entity.DisplayName = request.DisplayName.Trim();
        }

        if (request.MaxSizeBytes is not null)
        {
            entity.MaxSizeBytes = request.MaxSizeBytes.Value;
        }

        if (request.ClearWordTemplate)
        {
            entity.WordTemplateObjectId = null;
        }
        else if (request.WordTemplateObjectId is not null)
        {
            await ValidateWordTemplateReferenceAsync(catalogEntry, request.WordTemplateObjectId, cancellationToken);
            entity.WordTemplateObjectId = request.WordTemplateObjectId;
        }

        var wasActive = entity.IsActive;
        if (request.IsActive is not null && entity.IsActive && !request.IsActive.Value)
        {
            var hasActiveTemplateRule = await (
                from r in dbContext.FileRequirementRules.AsNoTracking()
                join t in dbContext.FileRequirementTemplates.AsNoTracking() on r.TemplateId equals t.Id
                where r.FileTypeId == fileTypeId && t.IsActive
                select r.Id).AnyAsync(cancellationToken);

            if (hasActiveTemplateRule)
            {
                throw ApiContractException.UnprocessableEntity(
                    messageAr: "لا يمكن تعطيل نوع الملف لارتباطه بقالب متطلبات ملفات فعّال (حماية المراجع).",
                    messageEn: "Cannot deactivate a file type referenced by an active file requirement template.");
            }
        }

        if (request.IsActive is not null)
        {
            entity.IsActive = request.IsActive.Value;
        }

        IReadOnlyList<string> finalExtensions;
        await using var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        if (request.Extensions is not null)
        {
            finalExtensions = ResolveAndVerifyCatalogExtensions(catalogEntry, request.Extensions);
            var existingExts = await dbContext.FileTypeExtensions
                .Where(e => e.FileTypeId == fileTypeId)
                .ToListAsync(cancellationToken);

            dbContext.FileTypeExtensions.RemoveRange(existingExts);
            await dbContext.SaveChangesAsync(cancellationToken);

            foreach (var ext in finalExtensions)
            {
                dbContext.FileTypeExtensions.Add(new FileTypeExtensionEntity
                {
                    Id = Guid.NewGuid(),
                    FileTypeId = fileTypeId,
                    Extension = ext
                });
            }
        }
        else
        {
            finalExtensions = await dbContext.FileTypeExtensions.AsNoTracking()
                .Where(e => e.FileTypeId == fileTypeId)
                .OrderBy(e => e.Extension)
                .Select(e => e.Extension)
                .ToListAsync(cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        var ruleCount = await dbContext.FileRequirementRules.AsNoTracking()
            .CountAsync(r => r.FileTypeId == fileTypeId, cancellationToken);

        var op = wasActive && !entity.IsActive
            ? "FILE_TYPE_DEACTIVATED"
            : (!wasActive && entity.IsActive ? "FILE_TYPE_ACTIVATED" : "FILE_TYPE_UPDATED");

        await serverAuthorization.WriteAuditEventAsync(
            actor.Id,
            op,
            "FILE_TYPE",
            entity.Id,
            "SUCCESS",
            new { entity.CatalogCode, entity.DisplayName, entity.MaxSizeBytes, entity.IsActive, extensions = finalExtensions },
            cancellationToken);

        return new FileTypeDto(
            entity.Id,
            entity.CatalogCode,
            entity.DisplayName,
            entity.MaxSizeBytes,
            entity.WordTemplateObjectId,
            entity.IsActive,
            catalogEntry.SupportsWordTemplate,
            finalExtensions,
            ruleCount,
            traceId);
    }

    private async Task ValidateWordTemplateReferenceAsync(
        FileTypeCatalogEntryDto catalogEntry,
        Guid? wordTemplateObjectId,
        CancellationToken cancellationToken)
    {
        if (wordTemplateObjectId is null)
        {
            return;
        }

        if (!catalogEntry.SupportsWordTemplate)
        {
            throw ApiContractException.UnprocessableEntity(
                messageAr: "حقل قالب Word مسموح فقط لنوع ملف Word التوثيقي.",
                messageEn: "WordTemplateObjectId is permitted only for WORD_DOC file types.");
        }

        var exists = await dbContext.FileObjects.AsNoTracking()
            .AnyAsync(f => f.Id == wordTemplateObjectId.Value, cancellationToken);
        if (!exists)
        {
            throw ApiContractException.UnprocessableEntity(
                messageAr: "كائن قالب Word المحدد غير موجود في مخزن الملفات.",
                messageEn: "The specified WordTemplateObjectId does not exist.");
        }
    }

    private static IReadOnlyList<string> ResolveAndVerifyCatalogExtensions(
        FileTypeCatalogEntryDto catalogEntry,
        IReadOnlyList<string>? requestedExtensions)
    {
        if (requestedExtensions is null || requestedExtensions.Count == 0)
        {
            return catalogEntry.AllowedExtensions.OrderBy(e => e, StringComparer.Ordinal).ToList();
        }

        var allowedSet = new HashSet<string>(catalogEntry.AllowedExtensions, StringComparer.OrdinalIgnoreCase);
        var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in requestedExtensions)
        {
            var trimmed = (raw ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }

            if (!trimmed.StartsWith('.'))
            {
                trimmed = "." + trimmed;
            }

            if (!allowedSet.Contains(trimmed))
            {
                throw ApiContractException.UnprocessableEntity(
                    messageAr: $"الامتداد ({trimmed}) غير مسموح به لنوع الملف ({catalogEntry.CatalogCode}). الامتدادات المعتمدة: {string.Join(", ", catalogEntry.AllowedExtensions)}.",
                    messageEn: $"Extension '{trimmed}' is not permitted for catalog code '{catalogEntry.CatalogCode}'. Allowed: {string.Join(", ", catalogEntry.AllowedExtensions)}.");
            }

            normalized.Add(trimmed);
        }

        if (normalized.Count == 0)
        {
            throw ApiContractException.UnprocessableEntity(
                messageAr: "يجب اختيار امتداد واحد على الأقل من الامتدادات المعتمدة للنوع.",
                messageEn: "At least one approved extension is required.");
        }

        return normalized.OrderBy(e => e, StringComparer.Ordinal).ToList();
    }
}

