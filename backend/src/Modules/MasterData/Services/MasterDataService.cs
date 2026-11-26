using System.Security.Claims;
using ApiGovernance.Modules.MasterData.Contracts;
using ApiGovernance.Shared.Contracts;
using ApiGovernance.Shared.Persistence;
using ApiGovernance.Shared.Persistence.Entities;
using ApiGovernance.Shared.Security;
using Microsoft.EntityFrameworkCore;

namespace ApiGovernance.Modules.MasterData.Services;

public interface IMasterDataService
{
    Task<PagedResult<CompanySystemDto>> GetCompanySystemsAsync(
        int? page,
        int? pageSize,
        string? search,
        bool? isActive,
        string? usageType,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<CompanySystemDto> CreateCompanySystemAsync(
        ClaimsPrincipal actorPrincipal,
        CreateCompanySystemRequest request,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<CompanySystemDto> UpdateCompanySystemAsync(
        ClaimsPrincipal actorPrincipal,
        Guid systemId,
        UpdateCompanySystemRequest request,
        string traceId,
        CancellationToken cancellationToken = default);

    Task DeleteCompanySystemAsync(
        ClaimsPrincipal actorPrincipal,
        Guid systemId,
        CancellationToken cancellationToken = default);

    Task<PagedResult<InternalProviderDto>> GetInternalProvidersAsync(
        int? page,
        int? pageSize,
        string? search,
        bool? isActive,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<InternalProviderDto> CreateInternalProviderAsync(
        ClaimsPrincipal actorPrincipal,
        CreateInternalProviderRequest request,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<InternalProviderDto> UpdateInternalProviderAsync(
        ClaimsPrincipal actorPrincipal,
        Guid providerId,
        UpdateInternalProviderRequest request,
        string traceId,
        CancellationToken cancellationToken = default);

    Task DeleteInternalProviderAsync(
        ClaimsPrincipal actorPrincipal,
        Guid providerId,
        CancellationToken cancellationToken = default);

    Task<PagedResult<ExternalProviderDto>> GetExternalProvidersAsync(
        int? page,
        int? pageSize,
        string? search,
        bool? isActive,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<ExternalProviderDto> CreateExternalProviderAsync(
        ClaimsPrincipal actorPrincipal,
        CreateExternalProviderRequest request,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<ExternalProviderDto> UpdateExternalProviderAsync(
        ClaimsPrincipal actorPrincipal,
        Guid providerId,
        UpdateExternalProviderRequest request,
        string traceId,
        CancellationToken cancellationToken = default);

    Task DeleteExternalProviderAsync(
        ClaimsPrincipal actorPrincipal,
        Guid providerId,
        CancellationToken cancellationToken = default);

    Task<PagedResult<ConsumerCategoryDto>> GetConsumerCategoriesAsync(
        int? page,
        int? pageSize,
        string? search,
        bool? isActive,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<ConsumerCategoryDto> CreateConsumerCategoryAsync(
        ClaimsPrincipal actorPrincipal,
        CreateConsumerCategoryRequest request,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<ConsumerCategoryDto> UpdateConsumerCategoryAsync(
        ClaimsPrincipal actorPrincipal,
        Guid categoryId,
        UpdateConsumerCategoryRequest request,
        string traceId,
        CancellationToken cancellationToken = default);

    Task DeleteConsumerCategoryAsync(
        ClaimsPrincipal actorPrincipal,
        Guid categoryId,
        CancellationToken cancellationToken = default);

    Task<PagedResult<ConsumerDto>> GetConsumersAsync(
        int? page,
        int? pageSize,
        string? search,
        bool? isActive,
        Guid? categoryId,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<ConsumerDto> CreateConsumerAsync(
        ClaimsPrincipal actorPrincipal,
        CreateConsumerRequest request,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<ConsumerDto> UpdateConsumerAsync(
        ClaimsPrincipal actorPrincipal,
        Guid consumerId,
        UpdateConsumerRequest request,
        string traceId,
        CancellationToken cancellationToken = default);

    Task DeleteConsumerAsync(
        ClaimsPrincipal actorPrincipal,
        Guid consumerId,
        CancellationToken cancellationToken = default);

    Task<PagedResult<ApiGroupDto>> GetApiGroupsAsync(
        int? page,
        int? pageSize,
        string? search,
        bool? isActive,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<ApiGroupDto> CreateApiGroupAsync(
        ClaimsPrincipal actorPrincipal,
        CreateApiGroupRequest request,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<ApiGroupDto> UpdateApiGroupAsync(
        ClaimsPrincipal actorPrincipal,
        Guid groupId,
        UpdateApiGroupRequest request,
        string traceId,
        CancellationToken cancellationToken = default);

    Task DeleteApiGroupAsync(
        ClaimsPrincipal actorPrincipal,
        Guid groupId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Implements Sprint S04 Master Data (M01 Company Systems with derived usage types,
/// M02 non-duplicated Internal Providers and independent External Providers,
/// M03 flat Consumer Categories and Consumer Entities, and M04 API Groups).
/// </summary>
public sealed class MasterDataService(
    ApiGovernanceDbContext dbContext,
    IServerAuthorizationService serverAuthorization) : IMasterDataService
{
    private const string ArchivedState = "ARCHIVED";

    // ========================================================================
    // M01 • Company Systems & Computed Usage Types
    // ========================================================================
    public async Task<PagedResult<CompanySystemDto>> GetCompanySystemsAsync(
        int? page,
        int? pageSize,
        string? search,
        bool? isActive,
        string? usageType,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.CompanySystems.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(s =>
                s.Name.ToLower().Contains(term) ||
                (s.Description != null && s.Description.ToLower().Contains(term)));
        }
        if (isActive is not null)
        {
            query = query.Where(s => s.IsActive == isActive.Value);
        }

        var systems = await query.OrderBy(s => s.Name).ToListAsync(cancellationToken);
        var dtos = await BuildCompanySystemDtosAsync(systems, traceId, cancellationToken);

        if (!string.IsNullOrWhiteSpace(usageType))
        {
            var normalizedUsage = usageType.Trim().ToUpperInvariant();
            dtos = dtos.Where(d => string.Equals(d.ComputedUsageType, normalizedUsage, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        var totalCount = dtos.Count;
        var (_, safePageSize, totalPages, normalizedPage) = PaginationHelper.Compute(page, pageSize, totalCount);
        var pagedItems = dtos.Skip((normalizedPage - 1) * safePageSize).Take(safePageSize).ToList();

        return new PagedResult<CompanySystemDto>(pagedItems, normalizedPage, safePageSize, totalCount, totalPages);
    }

    public async Task<CompanySystemDto> CreateCompanySystemAsync(
        ClaimsPrincipal actorPrincipal,
        CreateCompanySystemRequest request,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var actor = await serverAuthorization.RequireActiveAuthenticatedUserAsync(actorPrincipal, cancellationToken);
        var trimmedName = request.Name.Trim();
        var upperName = trimmedName.ToUpperInvariant();

        if (await dbContext.CompanySystems.AsNoTracking().AnyAsync(s => s.Name.ToUpper() == upperName, cancellationToken))
        {
            throw ApiContractException.Conflict(
                messageAr: "يوجد نظام شركة آخر مسجل بالاسم نفسه.",
                messageEn: "A company system with this name already exists.");
        }

        var entity = new CompanySystemEntity
        {
            Id = Guid.NewGuid(),
            Name = trimmedName,
            Description = NormalizeOptionalText(request.Description),
            IsActive = true
        };

        dbContext.CompanySystems.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        await serverAuthorization.WriteAuditEventAsync(
            actor.Id,
            "COMPANY_SYSTEM_CREATED",
            "COMPANY_SYSTEM",
            entity.Id,
            "SUCCESS",
            new { entity.Name, entity.IsActive },
            cancellationToken);

        return (await BuildCompanySystemDtosAsync([entity], traceId, cancellationToken)).Single();
    }

    public async Task<CompanySystemDto> UpdateCompanySystemAsync(
        ClaimsPrincipal actorPrincipal,
        Guid systemId,
        UpdateCompanySystemRequest request,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var actor = await serverAuthorization.RequireActiveAuthenticatedUserAsync(actorPrincipal, cancellationToken);
        var entity = await dbContext.CompanySystems.FirstOrDefaultAsync(s => s.Id == systemId, cancellationToken);
        if (entity is null)
        {
            throw ApiContractException.NotFound(
                messageAr: "نظام الشركة المطلوب غير موجود.",
                messageEn: "The requested company system was not found.");
        }

        if (request.Name is not null)
        {
            var trimmedName = request.Name.Trim();
            var upperName = trimmedName.ToUpperInvariant();
            if (await dbContext.CompanySystems.AsNoTracking().AnyAsync(s => s.Id != systemId && s.Name.ToUpper() == upperName, cancellationToken))
            {
                throw ApiContractException.Conflict(
                    messageAr: "اسم النظام مستخدم بالفعل لنظام شركة آخر.",
                    messageEn: "Another company system already uses this name.");
            }
            entity.Name = trimmedName;
        }

        if (request.ClearDescription)
        {
            entity.Description = null;
        }
        else if (request.Description is not null)
        {
            entity.Description = NormalizeOptionalText(request.Description);
        }

        var wasActive = entity.IsActive;
        if (request.IsActive is not null && entity.IsActive && !request.IsActive.Value)
        {
            var hasActiveProvider = await dbContext.InternalProviderSystems.AsNoTracking()
                .AnyAsync(p => p.CompanySystemId == systemId && p.IsActive, cancellationToken);
            var hasActiveDocuments = await dbContext.ApiDocuments.AsNoTracking()
                .AnyAsync(
                    d => d.State != ArchivedState &&
                         (d.ConsumerCompanySystemId == systemId || d.ProviderInternalSystemId == systemId),
                    cancellationToken);

            if (hasActiveProvider || hasActiveDocuments)
            {
                throw ApiContractException.UnprocessableEntity(
                    messageAr: "لا يمكن تعطيل نظام شركة مرتبط بمزوّد داخلي فعّال أو وثائق API نشطة (حماية المراجع).",
                    messageEn: "Cannot deactivate a company system referenced by an active internal provider or API document.");
            }
        }

        if (request.IsActive is not null)
        {
            entity.IsActive = request.IsActive.Value;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var op = wasActive && !entity.IsActive
            ? "COMPANY_SYSTEM_DEACTIVATED"
            : (!wasActive && entity.IsActive ? "COMPANY_SYSTEM_ACTIVATED" : "COMPANY_SYSTEM_UPDATED");

        await serverAuthorization.WriteAuditEventAsync(
            actor.Id,
            op,
            "COMPANY_SYSTEM",
            entity.Id,
            "SUCCESS",
            new { entity.Name, entity.IsActive },
            cancellationToken);

        return (await BuildCompanySystemDtosAsync([entity], traceId, cancellationToken)).Single();
    }

    // ========================================================================
    // M02 • Internal & External Provider Systems
    // ========================================================================
    public async Task<PagedResult<InternalProviderDto>> GetInternalProvidersAsync(
        int? page,
        int? pageSize,
        string? search,
        bool? isActive,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var query =
            from ip in dbContext.InternalProviderSystems.AsNoTracking()
            join cs in dbContext.CompanySystems.AsNoTracking() on ip.CompanySystemId equals cs.Id
            select new { Provider = ip, CompanySystem = cs };

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(x =>
                x.CompanySystem.Name.ToLower().Contains(term) ||
                (x.CompanySystem.Description != null && x.CompanySystem.Description.ToLower().Contains(term)));
        }
        if (isActive is not null)
        {
            query = query.Where(x => x.Provider.IsActive == isActive.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var (_, safePageSize, totalPages, normalizedPage) = PaginationHelper.Compute(page, pageSize, totalCount);

        var rows = await query
            .OrderBy(x => x.CompanySystem.Name)
            .Skip((normalizedPage - 1) * safePageSize)
            .Take(safePageSize)
            .ToListAsync(cancellationToken);

        var companySystemIds = rows.Select(x => x.CompanySystem.Id).ToList();
        var docCounts = companySystemIds.Count == 0
            ? new Dictionary<Guid, int>()
            : await dbContext.ApiDocuments.AsNoTracking()
                .Where(d => d.ProviderInternalSystemId != null && companySystemIds.Contains(d.ProviderInternalSystemId.Value))
                .GroupBy(d => d.ProviderInternalSystemId!.Value)
                .Select(g => new { Id = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Id, x => x.Count, cancellationToken);

        var items = rows.Select(x => new InternalProviderDto(
            x.Provider.Id,
            x.CompanySystem.Id,
            x.CompanySystem.Name,
            x.CompanySystem.Description,
            x.CompanySystem.IsActive,
            x.Provider.IsActive,
            docCounts.GetValueOrDefault(x.CompanySystem.Id, 0),
            traceId)).ToList();

        return new PagedResult<InternalProviderDto>(items, normalizedPage, safePageSize, totalCount, totalPages);
    }

    public async Task<InternalProviderDto> CreateInternalProviderAsync(
        ClaimsPrincipal actorPrincipal,
        CreateInternalProviderRequest request,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var actor = await serverAuthorization.RequireActiveAuthenticatedUserAsync(actorPrincipal, cancellationToken);

        var companySystem = await dbContext.CompanySystems.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == request.CompanySystemId, cancellationToken);
        if (companySystem is null || !companySystem.IsActive)
        {
            throw ApiContractException.UnprocessableEntity(
                messageAr: "نظام الشركة المحدد غير موجود أو معطّل.",
                messageEn: "The selected company system does not exist or is inactive.");
        }

        if (await dbContext.InternalProviderSystems.AsNoTracking().AnyAsync(ip => ip.CompanySystemId == request.CompanySystemId, cancellationToken))
        {
            throw ApiContractException.Conflict(
                messageAr: "نظام الشركة مفعّل بالفعل كمزوّد داخلي (يُمنع التكرار).",
                messageEn: "This company system is already registered as an internal provider.");
        }

        var entity = new InternalProviderSystemEntity
        {
            Id = Guid.NewGuid(),
            CompanySystemId = request.CompanySystemId,
            IsActive = true
        };

        dbContext.InternalProviderSystems.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        await serverAuthorization.WriteAuditEventAsync(
            actor.Id,
            "INTERNAL_PROVIDER_CREATED",
            "INTERNAL_PROVIDER",
            entity.Id,
            "SUCCESS",
            new { entity.CompanySystemId, companySystem.Name },
            cancellationToken);

        return new InternalProviderDto(
            entity.Id,
            companySystem.Id,
            companySystem.Name,
            companySystem.Description,
            companySystem.IsActive,
            entity.IsActive,
            0,
            traceId);
    }

    public async Task<InternalProviderDto> UpdateInternalProviderAsync(
        ClaimsPrincipal actorPrincipal,
        Guid providerId,
        UpdateInternalProviderRequest request,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var actor = await serverAuthorization.RequireActiveAuthenticatedUserAsync(actorPrincipal, cancellationToken);

        var provider = await dbContext.InternalProviderSystems.FirstOrDefaultAsync(p => p.Id == providerId, cancellationToken);
        if (provider is null)
        {
            throw ApiContractException.NotFound(
                messageAr: "المزوّد الداخلي المطلوب غير موجود.",
                messageEn: "The requested internal provider was not found.");
        }

        var companySystem = await dbContext.CompanySystems.AsNoTracking()
            .FirstAsync(s => s.Id == provider.CompanySystemId, cancellationToken);

        if (request.IsActive && !companySystem.IsActive)
        {
            throw ApiContractException.UnprocessableEntity(
                messageAr: "لا يمكن إعادة تفعيل مزوّد داخلي ما دام نظام الشركة الأصلي معطّلًا.",
                messageEn: "Cannot activate an internal provider while its underlying company system is inactive.");
        }

        if (provider.IsActive && !request.IsActive)
        {
            var hasActiveDocuments = await dbContext.ApiDocuments.AsNoTracking()
                .AnyAsync(d => d.ProviderInternalSystemId == provider.CompanySystemId && d.State != ArchivedState, cancellationToken);
            if (hasActiveDocuments)
            {
                throw ApiContractException.UnprocessableEntity(
                    messageAr: "لا يمكن تعطيل المزوّد الداخلي لارتباطه بوثائق API نشطة (حماية المراجع).",
                    messageEn: "Cannot deactivate an internal provider referenced by active API documents.");
            }
        }

        var wasActive = provider.IsActive;
        provider.IsActive = request.IsActive;
        await dbContext.SaveChangesAsync(cancellationToken);

        var docCount = await dbContext.ApiDocuments.AsNoTracking()
            .CountAsync(d => d.ProviderInternalSystemId == provider.CompanySystemId, cancellationToken);

        await serverAuthorization.WriteAuditEventAsync(
            actor.Id,
            wasActive && !provider.IsActive ? "INTERNAL_PROVIDER_DEACTIVATED" : "INTERNAL_PROVIDER_ACTIVATED",
            "INTERNAL_PROVIDER",
            provider.Id,
            "SUCCESS",
            new { provider.CompanySystemId, provider.IsActive },
            cancellationToken);

        return new InternalProviderDto(
            provider.Id,
            companySystem.Id,
            companySystem.Name,
            companySystem.Description,
            companySystem.IsActive,
            provider.IsActive,
            docCount,
            traceId);
    }

    public async Task<PagedResult<ExternalProviderDto>> GetExternalProvidersAsync(
        int? page,
        int? pageSize,
        string? search,
        bool? isActive,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.ExternalProviderSystems.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(e =>
                e.Name.ToLower().Contains(term) ||
                (e.Description != null && e.Description.ToLower().Contains(term)));
        }
        if (isActive is not null)
        {
            query = query.Where(e => e.IsActive == isActive.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var (_, safePageSize, totalPages, normalizedPage) = PaginationHelper.Compute(page, pageSize, totalCount);

        var rows = await query
            .OrderBy(e => e.Name)
            .Skip((normalizedPage - 1) * safePageSize)
            .Take(safePageSize)
            .ToListAsync(cancellationToken);

        var ids = rows.Select(r => r.Id).ToList();
        var docCounts = ids.Count == 0
            ? new Dictionary<Guid, int>()
            : await dbContext.ApiDocuments.AsNoTracking()
                .Where(d => d.ProviderExternalSystemId != null && ids.Contains(d.ProviderExternalSystemId.Value))
                .GroupBy(d => d.ProviderExternalSystemId!.Value)
                .Select(g => new { Id = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Id, x => x.Count, cancellationToken);

        var items = rows.Select(e => new ExternalProviderDto(
            e.Id,
            e.Name,
            e.Description,
            e.IsActive,
            docCounts.GetValueOrDefault(e.Id, 0),
            traceId)).ToList();

        return new PagedResult<ExternalProviderDto>(items, normalizedPage, safePageSize, totalCount, totalPages);
    }

    public async Task<ExternalProviderDto> CreateExternalProviderAsync(
        ClaimsPrincipal actorPrincipal,
        CreateExternalProviderRequest request,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var actor = await serverAuthorization.RequireActiveAuthenticatedUserAsync(actorPrincipal, cancellationToken);
        var trimmedName = request.Name.Trim();
        var upperName = trimmedName.ToUpperInvariant();

        if (await dbContext.ExternalProviderSystems.AsNoTracking().AnyAsync(e => e.Name.ToUpper() == upperName, cancellationToken))
        {
            throw ApiContractException.Conflict(
                messageAr: "يوجد مزوّد خارجي آخر مسجل بالاسم نفسه.",
                messageEn: "An external provider with this name already exists.");
        }

        var entity = new ExternalProviderSystemEntity
        {
            Id = Guid.NewGuid(),
            Name = trimmedName,
            Description = NormalizeOptionalText(request.Description),
            IsActive = true
        };

        dbContext.ExternalProviderSystems.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        await serverAuthorization.WriteAuditEventAsync(
            actor.Id,
            "EXTERNAL_PROVIDER_CREATED",
            "EXTERNAL_PROVIDER",
            entity.Id,
            "SUCCESS",
            new { entity.Name, entity.IsActive },
            cancellationToken);

        return new ExternalProviderDto(entity.Id, entity.Name, entity.Description, entity.IsActive, 0, traceId);
    }

    public async Task<ExternalProviderDto> UpdateExternalProviderAsync(
        ClaimsPrincipal actorPrincipal,
        Guid providerId,
        UpdateExternalProviderRequest request,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var actor = await serverAuthorization.RequireActiveAuthenticatedUserAsync(actorPrincipal, cancellationToken);
        var entity = await dbContext.ExternalProviderSystems.FirstOrDefaultAsync(e => e.Id == providerId, cancellationToken);
        if (entity is null)
        {
            throw ApiContractException.NotFound(
                messageAr: "المزوّد الخارجي المطلوب غير موجود.",
                messageEn: "The requested external provider was not found.");
        }

        if (request.Name is not null)
        {
            var trimmedName = request.Name.Trim();
            var upperName = trimmedName.ToUpperInvariant();
            if (await dbContext.ExternalProviderSystems.AsNoTracking().AnyAsync(e => e.Id != providerId && e.Name.ToUpper() == upperName, cancellationToken))
            {
                throw ApiContractException.Conflict(
                    messageAr: "اسم المزوّد الخارجي مستخدم بالفعل لمزوّد آخر.",
                    messageEn: "Another external provider already uses this name.");
            }
            entity.Name = trimmedName;
        }

        if (request.ClearDescription)
        {
            entity.Description = null;
        }
        else if (request.Description is not null)
        {
            entity.Description = NormalizeOptionalText(request.Description);
        }

        var wasActive = entity.IsActive;
        if (request.IsActive is not null && entity.IsActive && !request.IsActive.Value)
        {
            var hasActiveDocs = await dbContext.ApiDocuments.AsNoTracking()
                .AnyAsync(d => d.ProviderExternalSystemId == providerId && d.State != ArchivedState, cancellationToken);
            if (hasActiveDocs)
            {
                throw ApiContractException.UnprocessableEntity(
                    messageAr: "لا يمكن تعطيل المزوّد الخارجي لارتباطه بوثائق API نشطة (حماية المراجع).",
                    messageEn: "Cannot deactivate an external provider referenced by active API documents.");
            }
        }

        if (request.IsActive is not null)
        {
            entity.IsActive = request.IsActive.Value;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var docCount = await dbContext.ApiDocuments.AsNoTracking()
            .CountAsync(d => d.ProviderExternalSystemId == providerId, cancellationToken);

        var op = wasActive && !entity.IsActive
            ? "EXTERNAL_PROVIDER_DEACTIVATED"
            : (!wasActive && entity.IsActive ? "EXTERNAL_PROVIDER_ACTIVATED" : "EXTERNAL_PROVIDER_UPDATED");

        await serverAuthorization.WriteAuditEventAsync(
            actor.Id,
            op,
            "EXTERNAL_PROVIDER",
            entity.Id,
            "SUCCESS",
            new { entity.Name, entity.IsActive },
            cancellationToken);

        return new ExternalProviderDto(entity.Id, entity.Name, entity.Description, entity.IsActive, docCount, traceId);
    }

    // ========================================================================
    // M03 • Consumer Categories & Consumer Entities
    // ========================================================================
    public async Task<PagedResult<ConsumerCategoryDto>> GetConsumerCategoriesAsync(
        int? page,
        int? pageSize,
        string? search,
        bool? isActive,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.ConsumerCategories.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(c => c.Name.ToLower().Contains(term));
        }
        if (isActive is not null)
        {
            query = query.Where(c => c.IsActive == isActive.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var (_, safePageSize, totalPages, normalizedPage) = PaginationHelper.Compute(page, pageSize, totalCount);

        var rows = await query
            .OrderBy(c => c.Name)
            .Skip((normalizedPage - 1) * safePageSize)
            .Take(safePageSize)
            .ToListAsync(cancellationToken);

        var catIds = rows.Select(r => r.Id).ToList();
        var consumerCounts = catIds.Count == 0
            ? new Dictionary<Guid, int>()
            : await dbContext.ConsumerEntities.AsNoTracking()
                .Where(c => catIds.Contains(c.CategoryId))
                .GroupBy(c => c.CategoryId)
                .Select(g => new { Id = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Id, x => x.Count, cancellationToken);

        var items = rows.Select(c => new ConsumerCategoryDto(
            c.Id,
            c.Name,
            c.IsActive,
            consumerCounts.GetValueOrDefault(c.Id, 0),
            traceId)).ToList();

        return new PagedResult<ConsumerCategoryDto>(items, normalizedPage, safePageSize, totalCount, totalPages);
    }

    public async Task<ConsumerCategoryDto> CreateConsumerCategoryAsync(
        ClaimsPrincipal actorPrincipal,
        CreateConsumerCategoryRequest request,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var actor = await serverAuthorization.RequireActiveAuthenticatedUserAsync(actorPrincipal, cancellationToken);
        var trimmedName = request.Name.Trim();
        var upperName = trimmedName.ToUpperInvariant();

        if (await dbContext.ConsumerCategories.AsNoTracking().AnyAsync(c => c.Name.ToUpper() == upperName, cancellationToken))
        {
            throw ApiContractException.Conflict(
                messageAr: "يوجد تصنيف جهات مستهلكة آخر بالاسم نفسه.",
                messageEn: "A consumer category with this name already exists.");
        }

        var entity = new ConsumerCategoryEntity
        {
            Id = Guid.NewGuid(),
            Name = trimmedName,
            IsActive = true
        };

        dbContext.ConsumerCategories.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        await serverAuthorization.WriteAuditEventAsync(
            actor.Id,
            "CONSUMER_CATEGORY_CREATED",
            "CONSUMER_CATEGORY",
            entity.Id,
            "SUCCESS",
            new { entity.Name, entity.IsActive },
            cancellationToken);

        return new ConsumerCategoryDto(entity.Id, entity.Name, entity.IsActive, 0, traceId);
    }

    public async Task<ConsumerCategoryDto> UpdateConsumerCategoryAsync(
        ClaimsPrincipal actorPrincipal,
        Guid categoryId,
        UpdateConsumerCategoryRequest request,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var actor = await serverAuthorization.RequireActiveAuthenticatedUserAsync(actorPrincipal, cancellationToken);
        var entity = await dbContext.ConsumerCategories.FirstOrDefaultAsync(c => c.Id == categoryId, cancellationToken);
        if (entity is null)
        {
            throw ApiContractException.NotFound(
                messageAr: "تصنيف الجهات المطلوب غير موجود.",
                messageEn: "The requested consumer category was not found.");
        }

        if (request.Name is not null)
        {
            var trimmedName = request.Name.Trim();
            var upperName = trimmedName.ToUpperInvariant();
            if (await dbContext.ConsumerCategories.AsNoTracking().AnyAsync(c => c.Id != categoryId && c.Name.ToUpper() == upperName, cancellationToken))
            {
                throw ApiContractException.Conflict(
                    messageAr: "اسم التصنيف مستخدم بالفعل لتصنيف آخر.",
                    messageEn: "Another consumer category already uses this name.");
            }
            entity.Name = trimmedName;
        }

        var wasActive = entity.IsActive;
        if (request.IsActive is not null && entity.IsActive && !request.IsActive.Value)
        {
            var hasActiveConsumers = await dbContext.ConsumerEntities.AsNoTracking()
                .AnyAsync(ce => ce.CategoryId == categoryId && ce.IsActive, cancellationToken);
            if (hasActiveConsumers)
            {
                throw ApiContractException.UnprocessableEntity(
                    messageAr: "لا يمكن تعطيل تصنيف يضم جهات مستهلكة فعّالة (حماية المراجع).",
                    messageEn: "Cannot deactivate a consumer category containing active consumer entities.");
            }
        }

        if (request.IsActive is not null)
        {
            entity.IsActive = request.IsActive.Value;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var count = await dbContext.ConsumerEntities.AsNoTracking()
            .CountAsync(ce => ce.CategoryId == categoryId, cancellationToken);

        var op = wasActive && !entity.IsActive
            ? "CONSUMER_CATEGORY_DEACTIVATED"
            : (!wasActive && entity.IsActive ? "CONSUMER_CATEGORY_ACTIVATED" : "CONSUMER_CATEGORY_UPDATED");

        await serverAuthorization.WriteAuditEventAsync(
            actor.Id,
            op,
            "CONSUMER_CATEGORY",
            entity.Id,
            "SUCCESS",
            new { entity.Name, entity.IsActive },
            cancellationToken);

        return new ConsumerCategoryDto(entity.Id, entity.Name, entity.IsActive, count, traceId);
    }

    public async Task<PagedResult<ConsumerDto>> GetConsumersAsync(
        int? page,
        int? pageSize,
        string? search,
        bool? isActive,
        Guid? categoryId,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var query =
            from ce in dbContext.ConsumerEntities.AsNoTracking()
            join cat in dbContext.ConsumerCategories.AsNoTracking() on ce.CategoryId equals cat.Id
            select new { Consumer = ce, Category = cat };

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(x =>
                x.Consumer.Name.ToLower().Contains(term) ||
                x.Category.Name.ToLower().Contains(term));
        }
        if (isActive is not null)
        {
            query = query.Where(x => x.Consumer.IsActive == isActive.Value);
        }
        if (categoryId is not null)
        {
            query = query.Where(x => x.Consumer.CategoryId == categoryId.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var (_, safePageSize, totalPages, normalizedPage) = PaginationHelper.Compute(page, pageSize, totalCount);

        var rows = await query
            .OrderBy(x => x.Category.Name)
            .ThenBy(x => x.Consumer.Name)
            .Skip((normalizedPage - 1) * safePageSize)
            .Take(safePageSize)
            .ToListAsync(cancellationToken);

        var ids = rows.Select(x => x.Consumer.Id).ToList();
        var docCounts = ids.Count == 0
            ? new Dictionary<Guid, int>()
            : await dbContext.ApiDocuments.AsNoTracking()
                .Where(d => d.ConsumerEntityId != null && ids.Contains(d.ConsumerEntityId.Value))
                .GroupBy(d => d.ConsumerEntityId!.Value)
                .Select(g => new { Id = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Id, x => x.Count, cancellationToken);

        var userCounts = ids.Count == 0
            ? new Dictionary<Guid, int>()
            : await dbContext.Users.AsNoTracking()
                .Where(u => u.ConsumerEntityId != null && ids.Contains(u.ConsumerEntityId.Value))
                .GroupBy(u => u.ConsumerEntityId!.Value)
                .Select(g => new { Id = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Id, x => x.Count, cancellationToken);

        var items = rows.Select(x => new ConsumerDto(
            x.Consumer.Id,
            x.Category.Id,
            x.Category.Name,
            x.Category.IsActive,
            x.Consumer.Name,
            x.Consumer.IsActive,
            docCounts.GetValueOrDefault(x.Consumer.Id, 0),
            userCounts.GetValueOrDefault(x.Consumer.Id, 0),
            traceId)).ToList();

        return new PagedResult<ConsumerDto>(items, normalizedPage, safePageSize, totalCount, totalPages);
    }

    public async Task<ConsumerDto> CreateConsumerAsync(
        ClaimsPrincipal actorPrincipal,
        CreateConsumerRequest request,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var actor = await serverAuthorization.RequireActiveAuthenticatedUserAsync(actorPrincipal, cancellationToken);

        var category = await dbContext.ConsumerCategories.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == request.CategoryId, cancellationToken);
        if (category is null || !category.IsActive)
        {
            throw ApiContractException.UnprocessableEntity(
                messageAr: "تصنيف الجهة المحدد غير موجود أو معطّل.",
                messageEn: "The selected consumer category does not exist or is inactive.");
        }

        var trimmedName = request.Name.Trim();
        var upperName = trimmedName.ToUpperInvariant();
        if (await dbContext.ConsumerEntities.AsNoTracking().AnyAsync(c => c.Name.ToUpper() == upperName, cancellationToken))
        {
            throw ApiContractException.Conflict(
                messageAr: "توجد جهة مستهلكة أخرى مسجلة بالاسم نفسه.",
                messageEn: "A consumer entity with this name already exists.");
        }

        var entity = new ConsumerEntityRecord
        {
            Id = Guid.NewGuid(),
            CategoryId = category.Id,
            Name = trimmedName,
            IsActive = true
        };

        dbContext.ConsumerEntities.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        await serverAuthorization.WriteAuditEventAsync(
            actor.Id,
            "CONSUMER_CREATED",
            "CONSUMER_ENTITY",
            entity.Id,
            "SUCCESS",
            new { entity.Name, entity.CategoryId },
            cancellationToken);

        return new ConsumerDto(
            entity.Id,
            category.Id,
            category.Name,
            category.IsActive,
            entity.Name,
            entity.IsActive,
            0,
            0,
            traceId);
    }

    public async Task<ConsumerDto> UpdateConsumerAsync(
        ClaimsPrincipal actorPrincipal,
        Guid consumerId,
        UpdateConsumerRequest request,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var actor = await serverAuthorization.RequireActiveAuthenticatedUserAsync(actorPrincipal, cancellationToken);
        var entity = await dbContext.ConsumerEntities.FirstOrDefaultAsync(c => c.Id == consumerId, cancellationToken);
        if (entity is null)
        {
            throw ApiContractException.NotFound(
                messageAr: "الجهة المستهلكة المطلوبة غير موجودة.",
                messageEn: "The requested consumer entity was not found.");
        }

        if (request.CategoryId is not null && request.CategoryId.Value != entity.CategoryId)
        {
            var nextCat = await dbContext.ConsumerCategories.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == request.CategoryId.Value, cancellationToken);
            if (nextCat is null || !nextCat.IsActive)
            {
                throw ApiContractException.UnprocessableEntity(
                    messageAr: "التصنيف المحدد غير موجود أو معطّل.",
                    messageEn: "The target consumer category does not exist or is inactive.");
            }
            entity.CategoryId = nextCat.Id;
        }

        if (request.Name is not null)
        {
            var trimmedName = request.Name.Trim();
            var upperName = trimmedName.ToUpperInvariant();
            if (await dbContext.ConsumerEntities.AsNoTracking().AnyAsync(c => c.Id != consumerId && c.Name.ToUpper() == upperName, cancellationToken))
            {
                throw ApiContractException.Conflict(
                    messageAr: "اسم الجهة المستهلكة مستخدم بالفعل لجهة أخرى.",
                    messageEn: "Another consumer entity already uses this name.");
            }
            entity.Name = trimmedName;
        }

        var category = await dbContext.ConsumerCategories.AsNoTracking()
            .FirstAsync(c => c.Id == entity.CategoryId, cancellationToken);

        if (request.IsActive == true && !category.IsActive)
        {
            throw ApiContractException.UnprocessableEntity(
                messageAr: "لا يمكن تفعيل الجهة المستهلكة ما دام تصنيفها معطّلًا.",
                messageEn: "Cannot activate a consumer entity while its category is inactive.");
        }

        var wasActive = entity.IsActive;
        if (request.IsActive is not null && entity.IsActive && !request.IsActive.Value)
        {
            var hasActiveUsers = await dbContext.Users.AsNoTracking()
                .AnyAsync(u => u.ConsumerEntityId == consumerId && u.IsActive, cancellationToken);
            var hasActiveDocs = await dbContext.ApiDocuments.AsNoTracking()
                .AnyAsync(d => d.ConsumerEntityId == consumerId && d.State != ArchivedState, cancellationToken);

            if (hasActiveUsers || hasActiveDocs)
            {
                throw ApiContractException.UnprocessableEntity(
                    messageAr: "لا يمكن تعطيل الجهة المستهلكة لارتباطها بمستخدمين فعّالين أو وثائق API نشطة (حماية المراجع).",
                    messageEn: "Cannot deactivate a consumer entity referenced by active users or active API documents.");
            }
        }

        if (request.IsActive is not null)
        {
            entity.IsActive = request.IsActive.Value;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var docCount = await dbContext.ApiDocuments.AsNoTracking().CountAsync(d => d.ConsumerEntityId == consumerId, cancellationToken);
        var userCount = await dbContext.Users.AsNoTracking().CountAsync(u => u.ConsumerEntityId == consumerId, cancellationToken);

        var op = wasActive && !entity.IsActive
            ? "CONSUMER_DEACTIVATED"
            : (!wasActive && entity.IsActive ? "CONSUMER_ACTIVATED" : "CONSUMER_UPDATED");

        await serverAuthorization.WriteAuditEventAsync(
            actor.Id,
            op,
            "CONSUMER_ENTITY",
            entity.Id,
            "SUCCESS",
            new { entity.Name, entity.CategoryId, entity.IsActive },
            cancellationToken);

        return new ConsumerDto(
            entity.Id,
            category.Id,
            category.Name,
            category.IsActive,
            entity.Name,
            entity.IsActive,
            docCount,
            userCount,
            traceId);
    }

    // ========================================================================
    // M04 • API Groups
    // ========================================================================
    public async Task<PagedResult<ApiGroupDto>> GetApiGroupsAsync(
        int? page,
        int? pageSize,
        string? search,
        bool? isActive,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.ApiGroups.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(g =>
                g.Name.ToLower().Contains(term) ||
                (g.Description != null && g.Description.ToLower().Contains(term)));
        }
        if (isActive is not null)
        {
            query = query.Where(g => g.IsActive == isActive.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var (_, safePageSize, totalPages, normalizedPage) = PaginationHelper.Compute(page, pageSize, totalCount);

        var rows = await query
            .OrderBy(g => g.Name)
            .Skip((normalizedPage - 1) * safePageSize)
            .Take(safePageSize)
            .ToListAsync(cancellationToken);

        var ids = rows.Select(r => r.Id).ToList();
        var docCounts = ids.Count == 0
            ? new Dictionary<Guid, int>()
            : await dbContext.ApiDocuments.AsNoTracking()
                .Where(d => ids.Contains(d.ApiGroupId))
                .GroupBy(d => d.ApiGroupId)
                .Select(g => new { Id = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Id, x => x.Count, cancellationToken);

        var items = rows.Select(g => new ApiGroupDto(
            g.Id,
            g.Name,
            g.Description,
            g.IsActive,
            docCounts.GetValueOrDefault(g.Id, 0),
            traceId)).ToList();

        return new PagedResult<ApiGroupDto>(items, normalizedPage, safePageSize, totalCount, totalPages);
    }

    public async Task<ApiGroupDto> CreateApiGroupAsync(
        ClaimsPrincipal actorPrincipal,
        CreateApiGroupRequest request,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var actor = await serverAuthorization.RequireActiveAuthenticatedUserAsync(actorPrincipal, cancellationToken);
        var trimmedName = request.Name.Trim();
        var upperName = trimmedName.ToUpperInvariant();

        if (await dbContext.ApiGroups.AsNoTracking().AnyAsync(g => g.Name.ToUpper() == upperName, cancellationToken))
        {
            throw ApiContractException.Conflict(
                messageAr: "توجد مجموعة API أخرى مسجلة بالاسم نفسه.",
                messageEn: "An API group with this name already exists.");
        }

        var entity = new ApiGroupEntity
        {
            Id = Guid.NewGuid(),
            Name = trimmedName,
            Description = NormalizeOptionalText(request.Description),
            IsActive = true
        };

        dbContext.ApiGroups.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        await serverAuthorization.WriteAuditEventAsync(
            actor.Id,
            "API_GROUP_CREATED",
            "API_GROUP",
            entity.Id,
            "SUCCESS",
            new { entity.Name, entity.IsActive },
            cancellationToken);

        return new ApiGroupDto(entity.Id, entity.Name, entity.Description, entity.IsActive, 0, traceId);
    }

    public async Task<ApiGroupDto> UpdateApiGroupAsync(
        ClaimsPrincipal actorPrincipal,
        Guid groupId,
        UpdateApiGroupRequest request,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var actor = await serverAuthorization.RequireActiveAuthenticatedUserAsync(actorPrincipal, cancellationToken);
        var entity = await dbContext.ApiGroups.FirstOrDefaultAsync(g => g.Id == groupId, cancellationToken);
        if (entity is null)
        {
            throw ApiContractException.NotFound(
                messageAr: "مجموعة API المطلوبة غير موجودة.",
                messageEn: "The requested API group was not found.");
        }

        if (request.Name is not null)
        {
            var trimmedName = request.Name.Trim();
            var upperName = trimmedName.ToUpperInvariant();
            if (await dbContext.ApiGroups.AsNoTracking().AnyAsync(g => g.Id != groupId && g.Name.ToUpper() == upperName, cancellationToken))
            {
                throw ApiContractException.Conflict(
                    messageAr: "اسم مجموعة API مستخدم بالفعل لمجموعة أخرى.",
                    messageEn: "Another API group already uses this name.");
            }
            entity.Name = trimmedName;
        }

        if (request.ClearDescription)
        {
            entity.Description = null;
        }
        else if (request.Description is not null)
        {
            entity.Description = NormalizeOptionalText(request.Description);
        }

        var wasActive = entity.IsActive;
        if (request.IsActive is not null && entity.IsActive && !request.IsActive.Value)
        {
            var hasActiveDocs = await dbContext.ApiDocuments.AsNoTracking()
                .AnyAsync(d => d.ApiGroupId == groupId && d.State != ArchivedState, cancellationToken);
            if (hasActiveDocs)
            {
                throw ApiContractException.UnprocessableEntity(
                    messageAr: "لا يمكن تعطيل مجموعة API مرتبطة بوثائق API نشطة (حماية المراجع).",
                    messageEn: "Cannot deactivate an API group referenced by active API documents.");
            }
        }

        if (request.IsActive is not null)
        {
            entity.IsActive = request.IsActive.Value;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var docCount = await dbContext.ApiDocuments.AsNoTracking()
            .CountAsync(d => d.ApiGroupId == groupId, cancellationToken);

        var op = wasActive && !entity.IsActive
            ? "API_GROUP_DEACTIVATED"
            : (!wasActive && entity.IsActive ? "API_GROUP_ACTIVATED" : "API_GROUP_UPDATED");

        await serverAuthorization.WriteAuditEventAsync(
            actor.Id,
            op,
            "API_GROUP",
            entity.Id,
            "SUCCESS",
            new { entity.Name, entity.IsActive },
            cancellationToken);

        return new ApiGroupDto(entity.Id, entity.Name, entity.Description, entity.IsActive, docCount, traceId);
    }

    public async Task DeleteCompanySystemAsync(
        ClaimsPrincipal actorPrincipal,
        Guid systemId,
        CancellationToken cancellationToken = default)
    {
        var actor = await serverAuthorization.RequireActiveAuthenticatedUserAsync(actorPrincipal, cancellationToken);
        var entity = await dbContext.CompanySystems.FirstOrDefaultAsync(s => s.Id == systemId, cancellationToken);
        if (entity is null)
        {
            throw ApiContractException.NotFound(
                messageAr: "نظام الشركة المطلوب غير موجود.",
                messageEn: "The requested company system was not found.");
        }

        var hasDocuments = await dbContext.ApiDocuments.AsNoTracking()
            .AnyAsync(
                d => d.ConsumerCompanySystemId == systemId || d.ProviderInternalSystemId == systemId,
                cancellationToken);

        if (hasDocuments)
        {
            throw ApiContractException.UnprocessableEntity(
                messageAr: "لا يمكن حذف نظام شركة مرتبط بوثائق API قائمة.",
                messageEn: "Cannot delete a company system linked to existing API documents.");
        }

        var internalProviders = await dbContext.InternalProviderSystems
            .Where(p => p.CompanySystemId == systemId)
            .ToListAsync(cancellationToken);
        if (internalProviders.Count > 0)
        {
            dbContext.InternalProviderSystems.RemoveRange(internalProviders);
        }

        dbContext.CompanySystems.Remove(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        await serverAuthorization.WriteAuditEventAsync(
            actor.Id,
            "COMPANY_SYSTEM_DELETED",
            "COMPANY_SYSTEM",
            systemId,
            "SUCCESS",
            new { entity.Name },
            cancellationToken);
    }

    public async Task DeleteApiGroupAsync(
        ClaimsPrincipal actorPrincipal,
        Guid groupId,
        CancellationToken cancellationToken = default)
    {
        var actor = await serverAuthorization.RequireActiveAuthenticatedUserAsync(actorPrincipal, cancellationToken);
        var entity = await dbContext.ApiGroups.FirstOrDefaultAsync(g => g.Id == groupId, cancellationToken);
        if (entity is null)
        {
            throw ApiContractException.NotFound(
                messageAr: "مجموعة واجهات API المطلوبة غير موجودة.",
                messageEn: "The requested API group was not found.");
        }

        var hasDocuments = await dbContext.ApiDocuments.AsNoTracking()
            .AnyAsync(d => d.ApiGroupId == groupId, cancellationToken);

        if (hasDocuments)
        {
            throw ApiContractException.UnprocessableEntity(
                messageAr: "لا يمكن حذف مجموعة واجهات API مرتبطة بوثائق API قائمة.",
                messageEn: "Cannot delete an API group linked to existing API documents.");
        }

        dbContext.ApiGroups.Remove(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        await serverAuthorization.WriteAuditEventAsync(
            actor.Id,
            "API_GROUP_DELETED",
            "API_GROUP",
            groupId,
            "SUCCESS",
            new { entity.Name },
            cancellationToken);
    }

    public async Task DeleteConsumerCategoryAsync(
        ClaimsPrincipal actorPrincipal,
        Guid categoryId,
        CancellationToken cancellationToken = default)
    {
        var actor = await serverAuthorization.RequireActiveAuthenticatedUserAsync(actorPrincipal, cancellationToken);
        var entity = await dbContext.ConsumerCategories.FirstOrDefaultAsync(c => c.Id == categoryId, cancellationToken);
        if (entity is null)
        {
            throw ApiContractException.NotFound(
                messageAr: "تصنيف الجهات المطلوب غير موجود.",
                messageEn: "The requested consumer category was not found.");
        }

        var hasConsumers = await dbContext.ConsumerEntities.AsNoTracking()
            .AnyAsync(ce => ce.CategoryId == categoryId, cancellationToken);
        if (hasConsumers)
        {
            throw ApiContractException.UnprocessableEntity(
                messageAr: "لا يمكن حذف تصنيف يضم جهات مستهلكة مرتبطة به.",
                messageEn: "Cannot delete a consumer category containing linked consumer entities.");
        }

        dbContext.ConsumerCategories.Remove(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        await serverAuthorization.WriteAuditEventAsync(
            actor.Id,
            "CONSUMER_CATEGORY_DELETED",
            "CONSUMER_CATEGORY",
            categoryId,
            "SUCCESS",
            new { entity.Name },
            cancellationToken);
    }

    public async Task DeleteConsumerAsync(
        ClaimsPrincipal actorPrincipal,
        Guid consumerId,
        CancellationToken cancellationToken = default)
    {
        var actor = await serverAuthorization.RequireActiveAuthenticatedUserAsync(actorPrincipal, cancellationToken);
        var entity = await dbContext.ConsumerEntities.FirstOrDefaultAsync(c => c.Id == consumerId, cancellationToken);
        if (entity is null)
        {
            throw ApiContractException.NotFound(
                messageAr: "الجهة المستهلكة المطلوبة غير موجودة.",
                messageEn: "The requested consumer entity was not found.");
        }

        var hasDocuments = await dbContext.ApiDocuments.AsNoTracking()
            .AnyAsync(d => d.ConsumerEntityId == consumerId, cancellationToken);
        var hasUsers = await dbContext.Users.AsNoTracking()
            .AnyAsync(u => u.ConsumerEntityId == consumerId, cancellationToken);

        if (hasDocuments || hasUsers)
        {
            throw ApiContractException.UnprocessableEntity(
                messageAr: "لا يمكن حذف جهة مستهلكة مرتبطة بوثائق API أو مستخدمين.",
                messageEn: "Cannot delete a consumer entity linked to API documents or user accounts.");
        }

        dbContext.ConsumerEntities.Remove(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        await serverAuthorization.WriteAuditEventAsync(
            actor.Id,
            "CONSUMER_DELETED",
            "CONSUMER_ENTITY",
            consumerId,
            "SUCCESS",
            new { entity.Name },
            cancellationToken);
    }

    public async Task DeleteInternalProviderAsync(
        ClaimsPrincipal actorPrincipal,
        Guid providerId,
        CancellationToken cancellationToken = default)
    {
        var actor = await serverAuthorization.RequireActiveAuthenticatedUserAsync(actorPrincipal, cancellationToken);
        var entity = await dbContext.InternalProviderSystems.FirstOrDefaultAsync(p => p.Id == providerId, cancellationToken);
        if (entity is null)
        {
            throw ApiContractException.NotFound(
                messageAr: "سجل المزوّد الداخلي غير موجود.",
                messageEn: "The requested internal provider record was not found.");
        }

        var hasDocuments = await dbContext.ApiDocuments.AsNoTracking()
            .AnyAsync(d => d.ProviderInternalSystemId == entity.CompanySystemId, cancellationToken);
        if (hasDocuments)
        {
            throw ApiContractException.UnprocessableEntity(
                messageAr: "لا يمكن حذف مزوّد داخلي مرتبط بوثائق API قائمة.",
                messageEn: "Cannot delete an internal provider linked to existing API documents.");
        }

        dbContext.InternalProviderSystems.Remove(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        await serverAuthorization.WriteAuditEventAsync(
            actor.Id,
            "INTERNAL_PROVIDER_DELETED",
            "INTERNAL_PROVIDER",
            providerId,
            "SUCCESS",
            new { entity.CompanySystemId },
            cancellationToken);
    }

    public async Task DeleteExternalProviderAsync(
        ClaimsPrincipal actorPrincipal,
        Guid providerId,
        CancellationToken cancellationToken = default)
    {
        var actor = await serverAuthorization.RequireActiveAuthenticatedUserAsync(actorPrincipal, cancellationToken);
        var entity = await dbContext.ExternalProviderSystems.FirstOrDefaultAsync(p => p.Id == providerId, cancellationToken);
        if (entity is null)
        {
            throw ApiContractException.NotFound(
                messageAr: "سجل المزوّد الخارجي غير موجود.",
                messageEn: "The requested external provider record was not found.");
        }

        var hasDocuments = await dbContext.ApiDocuments.AsNoTracking()
            .AnyAsync(d => d.ProviderExternalSystemId == providerId, cancellationToken);
        if (hasDocuments)
        {
            throw ApiContractException.UnprocessableEntity(
                messageAr: "لا يمكن حذف مزوّد خارجي مرتبط بوثائق API قائمة.",
                messageEn: "Cannot delete an external provider linked to existing API documents.");
        }

        dbContext.ExternalProviderSystems.Remove(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        await serverAuthorization.WriteAuditEventAsync(
            actor.Id,
            "EXTERNAL_PROVIDER_DELETED",
            "EXTERNAL_PROVIDER",
            providerId,
            "SUCCESS",
            new { entity.Name },
            cancellationToken);
    }

    // ========================================================================
    // Helpers
    // ========================================================================
    private async Task<List<CompanySystemDto>> BuildCompanySystemDtosAsync(
        IReadOnlyList<CompanySystemEntity> systems,
        string traceId,
        CancellationToken cancellationToken)
    {
        if (systems.Count == 0)
        {
            return [];
        }

        var systemIds = systems.Select(s => s.Id).ToList();

        var internalProviderSystemIds = await dbContext.InternalProviderSystems.AsNoTracking()
            .Where(ip => systemIds.Contains(ip.CompanySystemId))
            .Select(ip => ip.CompanySystemId)
            .ToHashSetAsync(cancellationToken);

        var providerDocCountsBySystem = await dbContext.ApiDocuments.AsNoTracking()
            .Where(d => d.ProviderInternalSystemId != null && systemIds.Contains(d.ProviderInternalSystemId.Value))
            .GroupBy(d => d.ProviderInternalSystemId!.Value)
            .Select(g => new { SystemId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.SystemId, x => x.Count, cancellationToken);

        var consumerDocCountsBySystem = await dbContext.ApiDocuments.AsNoTracking()
            .Where(d => d.ConsumerCompanySystemId != null && systemIds.Contains(d.ConsumerCompanySystemId.Value))
            .GroupBy(d => d.ConsumerCompanySystemId!.Value)
            .Select(g => new { SystemId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.SystemId, x => x.Count, cancellationToken);

        return systems.Select(sys =>
        {
            var provDocs = providerDocCountsBySystem.GetValueOrDefault(sys.Id, 0);
            var consDocs = consumerDocCountsBySystem.GetValueOrDefault(sys.Id, 0);
            var isRegisteredProvider = internalProviderSystemIds.Contains(sys.Id);

            var computedUsage = (provDocs > 0, consDocs > 0) switch
            {
                (true, true) => CompanySystemUsageTypes.Both,
                (true, false) => CompanySystemUsageTypes.Provider,
                (false, true) => CompanySystemUsageTypes.Consumer,
                _ => CompanySystemUsageTypes.None
            };

            return new CompanySystemDto(
                sys.Id,
                sys.Name,
                sys.Description,
                sys.IsActive,
                computedUsage,
                isRegisteredProvider,
                provDocs,
                consDocs,
                traceId);
        }).ToList();
    }

    private static string? NormalizeOptionalText(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
