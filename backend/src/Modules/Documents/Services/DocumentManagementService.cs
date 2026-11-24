using System.Security.Claims;
using ApiGovernance.Modules.Documents.Contracts;
using ApiGovernance.Shared.Contracts;
using ApiGovernance.Shared.Persistence;
using ApiGovernance.Shared.Persistence.Entities;
using ApiGovernance.Shared.Security;
using Microsoft.EntityFrameworkCore;

namespace ApiGovernance.Modules.Documents.Services;

public interface IDocumentManagementService
{
    Task<DocumentLookupsResponse> GetLookupsAsync(
        string traceId,
        CancellationToken cancellationToken = default);

    Task<PagedResult<ApiDocumentDto>> GetDocumentsAsync(
        ClaimsPrincipal actorPrincipal,
        int? page,
        int? pageSize,
        string? search,
        string? state,
        bool? hasIntegration,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<ApiDocumentDto> GetDocumentAsync(
        ClaimsPrincipal actorPrincipal,
        Guid documentId,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<ApiDocumentDto> CreateDocumentAsync(
        ClaimsPrincipal actorPrincipal,
        CreateApiDocumentRequest request,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<ApiDocumentDto> UpdateDocumentAsync(
        ClaimsPrincipal actorPrincipal,
        Guid documentId,
        UpdateApiDocumentRequest request,
        string traceId,
        CancellationToken cancellationToken = default);

    Task DeleteDocumentAsync(
        ClaimsPrincipal actorPrincipal,
        Guid documentId,
        CancellationToken cancellationToken = default);

    Task<ApiDocumentDto> ReplaceDevelopersAsync(
        ClaimsPrincipal actorPrincipal,
        Guid documentId,
        ReplaceDocumentDevelopersRequest request,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<DocumentAccessMatrixResponse> GetDocumentAccessMatrixAsync(
        ClaimsPrincipal actorPrincipal,
        Guid documentId,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<DocumentAccessMatrixResponse> ReplaceDocumentAccessAsync(
        ClaimsPrincipal actorPrincipal,
        Guid documentId,
        ReplaceDocumentAccessRequest request,
        string traceId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Implements Sprint S06 Tasks D01, D02, D03, I07, and U02:
/// - Derived title (provider -> consumer), distinct internal parties, active configuration dependencies
/// - Explicit separation between document_developers assignment and document ACL grants
/// - Default-restricted new documents (creator default ACL grants only), anti-IDOR filtering &amp; 404 masking
/// - Conditional 1:1 integration link protection on delete and party edits
/// - Complete ADR-01 Document ACL matrix (Roles + Users across all 11 document actions)
/// </summary>
public sealed class DocumentManagementService(
    ApiGovernanceDbContext dbContext,
    IServerAuthorizationService serverAuthorization,
    IDocumentAuthorizationService documentAuthorization) : IDocumentManagementService
{
    public async Task<DocumentLookupsResponse> GetLookupsAsync(
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var internalProviders = await (
            from ip in dbContext.InternalProviderSystems.AsNoTracking()
            join cs in dbContext.CompanySystems.AsNoTracking() on ip.CompanySystemId equals cs.Id
            where ip.IsActive && cs.IsActive
            orderby cs.Name
            select new LookupOptionDto(cs.Id, cs.Name, cs.Description))
            .ToListAsync(cancellationToken);

        var externalProviders = await dbContext.ExternalProviderSystems.AsNoTracking()
            .Where(ep => ep.IsActive)
            .OrderBy(ep => ep.Name)
            .Select(ep => new LookupOptionDto(ep.Id, ep.Name, ep.Description))
            .ToListAsync(cancellationToken);

        var companyConsumers = await dbContext.CompanySystems.AsNoTracking()
            .Where(cs => cs.IsActive)
            .OrderBy(cs => cs.Name)
            .Select(cs => new LookupOptionDto(cs.Id, cs.Name, cs.Description))
            .ToListAsync(cancellationToken);

        var consumerEntities = await (
            from ce in dbContext.ConsumerEntities.AsNoTracking()
            join cc in dbContext.ConsumerCategories.AsNoTracking() on ce.CategoryId equals cc.Id
            where ce.IsActive && cc.IsActive
            orderby ce.Name
            select new LookupOptionDto(ce.Id, ce.Name, cc.Name))
            .ToListAsync(cancellationToken);

        var apiGroups = await dbContext.ApiGroups.AsNoTracking()
            .Where(ag => ag.IsActive)
            .OrderBy(ag => ag.Name)
            .Select(ag => new LookupOptionDto(ag.Id, ag.Name, ag.Description))
            .ToListAsync(cancellationToken);

        var templates = await dbContext.FileRequirementTemplates.AsNoTracking()
            .Where(rt => rt.IsActive)
            .OrderBy(rt => rt.Name)
            .Select(rt => new LookupOptionDto(rt.Id, rt.Name, null))
            .ToListAsync(cancellationToken);

        var routeIdsWithStages = await dbContext.ApprovalRouteStages.AsNoTracking()
            .Select(s => s.RouteId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var routes = await dbContext.ApprovalRoutes.AsNoTracking()
            .Where(r => r.IsActive && routeIdsWithStages.Contains(r.Id))
            .OrderBy(r => r.Name)
            .Select(r => new LookupOptionDto(r.Id, r.Name, null))
            .ToListAsync(cancellationToken);

        var policies = await dbContext.ChangePolicies.AsNoTracking()
            .Where(cp => cp.IsActive)
            .OrderBy(cp => cp.Name)
            .Select(cp => new LookupOptionDto(cp.Id, cp.Name, null))
            .ToListAsync(cancellationToken);

        var activeUsers = await dbContext.Users.AsNoTracking()
            .Where(u => u.IsActive)
            .OrderBy(u => u.FullName)
            .Select(u => new LookupOptionDto(u.Id, u.FullName, u.Email))
            .ToListAsync(cancellationToken);

        return new DocumentLookupsResponse(
            internalProviders,
            externalProviders,
            companyConsumers,
            consumerEntities,
            apiGroups,
            templates,
            routes,
            policies,
            activeUsers,
            traceId);
    }

    public async Task<PagedResult<ApiDocumentDto>> GetDocumentsAsync(
        ClaimsPrincipal actorPrincipal,
        int? page,
        int? pageSize,
        string? search,
        string? state,
        bool? hasIntegration,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var actor = await serverAuthorization.RequireActiveAuthenticatedUserAsync(actorPrincipal, cancellationToken);

        var query = dbContext.ApiDocuments.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(state))
        {
            var normalizedState = state.Trim().ToUpperInvariant();
            query = query.Where(d => d.State == normalizedState);
        }

        var candidates = await query
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync(cancellationToken);

        var docIds = candidates.Select(d => d.Id).ToList();
        var accessMap = await documentAuthorization.EvaluateDocumentPermissionsBatchAsync(actor.Id, docIds, cancellationToken);

        // Enforce strict Document-Level ACL visibility (anti-IDOR): only return documents where actor holds doc.view
        var visibleDocs = candidates
            .Where(d => accessMap.TryGetValue(d.Id, out var map) &&
                        map.TryGetValue(DocumentPermissionCatalog.View, out var res) &&
                        res.Allowed)
            .ToList();

        var dtos = await BuildDocumentDtosAsync(visibleDocs, accessMap, traceId, cancellationToken);

        if (hasIntegration is not null)
        {
            dtos = dtos.Where(d => d.HasIntegration == hasIntegration.Value).ToList();
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            dtos = dtos.Where(d =>
                d.DerivedTitle.ToLowerInvariant().Contains(term) ||
                d.ApiGroupName.ToLowerInvariant().Contains(term) ||
                d.ProviderName.ToLowerInvariant().Contains(term) ||
                d.ConsumerName.ToLowerInvariant().Contains(term) ||
                (d.IntegrationReferenceCode?.ToLowerInvariant().Contains(term) ?? false)).ToList();
        }

        var totalCount = dtos.Count;
        var (_, safePageSize, totalPages, normalizedPage) = PaginationHelper.Compute(page, pageSize, totalCount);
        var pagedItems = dtos.Skip((normalizedPage - 1) * safePageSize).Take(safePageSize).ToList();

        return new PagedResult<ApiDocumentDto>(pagedItems, normalizedPage, safePageSize, totalCount, totalPages);
    }

    public async Task<ApiDocumentDto> GetDocumentAsync(
        ClaimsPrincipal actorPrincipal,
        Guid documentId,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var access = await documentAuthorization.RequireDocumentPermissionAsync(
            actorPrincipal,
            documentId,
            DocumentPermissionCatalog.View,
            "API_DOCUMENT_READ_DENIED",
            cancellationToken);

        var list = await BuildDocumentDtosAsync(
            [access.Document],
            new Dictionary<Guid, IReadOnlyDictionary<string, DocumentPermissionResolution>>
            {
                [access.Document.Id] = access.EffectivePermissions
            },
            traceId,
            cancellationToken);

        return list[0];
    }

    public async Task<ApiDocumentDto> CreateDocumentAsync(
        ClaimsPrincipal actorPrincipal,
        CreateApiDocumentRequest request,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var actor = await serverAuthorization.RequireActiveAuthenticatedUserAsync(actorPrincipal, cancellationToken);

        await ValidateDocumentPartiesAndConfigAsync(
            request.ProviderInternalSystemId,
            request.ProviderExternalSystemId,
            request.ConsumerCompanySystemId,
            request.ConsumerEntityId,
            request.ApiGroupId,
            request.RequirementTemplateId,
            request.ApprovalRouteId,
            request.ChangePolicyId,
            excludeDocumentId: null,
            cancellationToken);

        var validatedDeveloperIds = await ValidateDeveloperUserIdsAsync(
            request.DeveloperUserIds ?? [],
            cancellationToken);

        var doc = new ApiDocumentEntity
        {
            Id = Guid.NewGuid(),
            ProviderInternalSystemId = request.ProviderInternalSystemId,
            ProviderExternalSystemId = request.ProviderExternalSystemId,
            ConsumerCompanySystemId = request.ConsumerCompanySystemId,
            ConsumerEntityId = request.ConsumerEntityId,
            ApiGroupId = request.ApiGroupId,
            RequirementTemplateId = request.RequirementTemplateId,
            ApprovalRouteId = request.ApprovalRouteId,
            ChangePolicyId = request.ChangePolicyId,
            PublicationOption = DocumentPublicationOptions.Normalize(request.PublicationOption),
            CreatedBy = actor.Id,
            State = DocumentStates.Normalize(request.State),
            CreatedAt = DateTimeOffset.UtcNow
        };

        await using var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        dbContext.ApiDocuments.Add(doc);
        foreach (var devId in validatedDeveloperIds)
        {
            dbContext.DocumentDevelopers.Add(new DocumentDeveloperEntity
            {
                Id = Guid.NewGuid(),
                DocumentId = doc.Id,
                UserId = devId
            });
        }
        await dbContext.SaveChangesAsync(cancellationToken);

        // Creator receives initial ACL grants; all other users (and roles) remain denied by default per ADR-01 & PRD-03.
        await documentAuthorization.GrantCreatorDefaultDocumentAclAsync(doc.Id, actor.Id, cancellationToken);

        await serverAuthorization.WriteAuditEventAsync(
            actor.Id,
            "API_DOCUMENT_CREATED",
            "API_DOCUMENT",
            doc.Id,
            "SUCCESS",
            new
            {
                doc.ProviderInternalSystemId,
                doc.ProviderExternalSystemId,
                doc.ConsumerCompanySystemId,
                doc.ConsumerEntityId,
                doc.ApiGroupId,
                doc.RequirementTemplateId,
                doc.ApprovalRouteId,
                doc.ChangePolicyId,
                doc.PublicationOption,
                doc.State,
                assignedDeveloperIds = validatedDeveloperIds
            },
            cancellationToken);

        await tx.CommitAsync(cancellationToken);

        return await GetDocumentAsync(actorPrincipal, doc.Id, traceId, cancellationToken);
    }

    public async Task<ApiDocumentDto> UpdateDocumentAsync(
        ClaimsPrincipal actorPrincipal,
        Guid documentId,
        UpdateApiDocumentRequest request,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var access = await documentAuthorization.RequireDocumentPermissionAsync(
            actorPrincipal,
            documentId,
            DocumentPermissionCatalog.Edit,
            "API_DOCUMENT_EDIT_DENIED",
            cancellationToken);

        var entity = await dbContext.ApiDocuments
            .FirstAsync(d => d.Id == documentId, cancellationToken);

        var hasIntegration = await dbContext.Integrations.AsNoTracking()
            .AnyAsync(i => i.DocumentId == documentId, cancellationToken);

        var changingParties =
            request.ProviderInternalSystemId is not null ||
            request.ProviderExternalSystemId is not null ||
            request.ConsumerCompanySystemId is not null ||
            request.ConsumerEntityId is not null ||
            request.ApiGroupId is not null;

        if (hasIntegration && changingParties)
        {
            var providerChanged =
                (request.ProviderInternalSystemId is not null || request.ProviderExternalSystemId is not null) &&
                (request.ProviderInternalSystemId != entity.ProviderInternalSystemId ||
                 request.ProviderExternalSystemId != entity.ProviderExternalSystemId);

            var consumerChanged =
                (request.ConsumerCompanySystemId is not null || request.ConsumerEntityId is not null) &&
                (request.ConsumerCompanySystemId != entity.ConsumerCompanySystemId ||
                 request.ConsumerEntityId != entity.ConsumerEntityId);

            var groupChanged = request.ApiGroupId is not null && request.ApiGroupId.Value != entity.ApiGroupId;

            if (providerChanged || consumerChanged || groupChanged)
            {
                throw ApiContractException.UnprocessableEntity(
                    messageAr: "لا يمكن تعديل المزوّد أو المستهلك أو مجموعة API بعد ربط الوثيقة بتكامل (يمكن تعديل الإعدادات فقط).",
                    messageEn: "Cannot modify provider, consumer, or API group once the document is linked to an integration.");
            }
        }

        var nextProvInt = (request.ProviderInternalSystemId is not null || request.ProviderExternalSystemId is not null)
            ? request.ProviderInternalSystemId
            : entity.ProviderInternalSystemId;
        var nextProvExt = (request.ProviderInternalSystemId is not null || request.ProviderExternalSystemId is not null)
            ? request.ProviderExternalSystemId
            : entity.ProviderExternalSystemId;
        var nextConsSys = (request.ConsumerCompanySystemId is not null || request.ConsumerEntityId is not null)
            ? request.ConsumerCompanySystemId
            : entity.ConsumerCompanySystemId;
        var nextConsEnt = (request.ConsumerCompanySystemId is not null || request.ConsumerEntityId is not null)
            ? request.ConsumerEntityId
            : entity.ConsumerEntityId;
        var nextApiGroup = request.ApiGroupId ?? entity.ApiGroupId;
        var nextTemplate = request.RequirementTemplateId ?? entity.RequirementTemplateId;
        var nextRoute = request.ApprovalRouteId ?? entity.ApprovalRouteId;
        var nextPolicy = request.ChangePolicyId ?? entity.ChangePolicyId;

        await ValidateDocumentPartiesAndConfigAsync(
            nextProvInt,
            nextProvExt,
            nextConsSys,
            nextConsEnt,
            nextApiGroup,
            nextTemplate,
            nextRoute,
            nextPolicy,
            excludeDocumentId: documentId,
            cancellationToken);

        entity.ProviderInternalSystemId = nextProvInt;
        entity.ProviderExternalSystemId = nextProvExt;
        entity.ConsumerCompanySystemId = nextConsSys;
        entity.ConsumerEntityId = nextConsEnt;
        entity.ApiGroupId = nextApiGroup;
        entity.RequirementTemplateId = nextTemplate;
        entity.ApprovalRouteId = nextRoute;
        entity.ChangePolicyId = nextPolicy;

        if (request.PublicationOption is not null)
        {
            entity.PublicationOption = DocumentPublicationOptions.Normalize(request.PublicationOption);
        }
        if (request.State is not null)
        {
            entity.State = DocumentStates.Normalize(request.State);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        await serverAuthorization.WriteAuditEventAsync(
            access.Actor.Id,
            "API_DOCUMENT_UPDATED",
            "API_DOCUMENT",
            entity.Id,
            "SUCCESS",
            new
            {
                entity.RequirementTemplateId,
                entity.ApprovalRouteId,
                entity.ChangePolicyId,
                entity.PublicationOption,
                entity.State
            },
            cancellationToken);

        return await GetDocumentAsync(actorPrincipal, documentId, traceId, cancellationToken);
    }

    public async Task DeleteDocumentAsync(
        ClaimsPrincipal actorPrincipal,
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        var access = await documentAuthorization.RequireDocumentPermissionAsync(
            actorPrincipal,
            documentId,
            DocumentPermissionCatalog.Delete,
            "API_DOCUMENT_DELETE_DENIED",
            cancellationToken);

        var hasIntegration = await dbContext.Integrations.AsNoTracking()
            .AnyAsync(i => i.DocumentId == documentId, cancellationToken);
        if (hasIntegration)
        {
            throw ApiContractException.UnprocessableEntity(
                messageAr: "لا يمكن حذف وثيقة API لارتباطها بتكامل فعّال (حماية الارتباط 1:1).",
                messageEn: "Cannot delete an API document linked to an integration.");
        }

        await using var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var devRows = await dbContext.DocumentDevelopers.Where(dd => dd.DocumentId == documentId).ToListAsync(cancellationToken);
        var roleAclRows = await dbContext.DocumentRolePermissions.Where(r => r.DocumentId == documentId).ToListAsync(cancellationToken);
        var userAclRows = await dbContext.DocumentUserPermissions.Where(u => u.DocumentId == documentId).ToListAsync(cancellationToken);

        dbContext.DocumentDevelopers.RemoveRange(devRows);
        dbContext.DocumentRolePermissions.RemoveRange(roleAclRows);
        dbContext.DocumentUserPermissions.RemoveRange(userAclRows);
        await dbContext.SaveChangesAsync(cancellationToken);

        var entity = await dbContext.ApiDocuments.FirstAsync(d => d.Id == documentId, cancellationToken);
        dbContext.ApiDocuments.Remove(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        await serverAuthorization.WriteAuditEventAsync(
            access.Actor.Id,
            "API_DOCUMENT_DELETED",
            "API_DOCUMENT",
            documentId,
            "SUCCESS",
            new { deletedDocumentId = documentId },
            cancellationToken);

        await tx.CommitAsync(cancellationToken);
    }

    public async Task<ApiDocumentDto> ReplaceDevelopersAsync(
        ClaimsPrincipal actorPrincipal,
        Guid documentId,
        ReplaceDocumentDevelopersRequest request,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var access = await documentAuthorization.RequireDocumentPermissionAsync(
            actorPrincipal,
            documentId,
            DocumentPermissionCatalog.Assign,
            "API_DOCUMENT_DEVELOPERS_DENIED",
            cancellationToken);

        var validatedDeveloperIds = await ValidateDeveloperUserIdsAsync(
            request.DeveloperUserIds ?? [],
            cancellationToken);

        await using var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var existing = await dbContext.DocumentDevelopers
            .Where(dd => dd.DocumentId == documentId)
            .ToListAsync(cancellationToken);

        dbContext.DocumentDevelopers.RemoveRange(existing);
        await dbContext.SaveChangesAsync(cancellationToken);

        foreach (var devId in validatedDeveloperIds)
        {
            dbContext.DocumentDevelopers.Add(new DocumentDeveloperEntity
            {
                Id = Guid.NewGuid(),
                DocumentId = documentId,
                UserId = devId
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        // Explicit audit trail — note that assigning developers NEVER mutates document_user_permissions.
        await serverAuthorization.WriteAuditEventAsync(
            access.Actor.Id,
            "API_DOCUMENT_DEVELOPERS_REPLACED",
            "API_DOCUMENT",
            documentId,
            "SUCCESS",
            new { developerUserIds = validatedDeveloperIds },
            cancellationToken);

        await tx.CommitAsync(cancellationToken);

        return await GetDocumentAsync(actorPrincipal, documentId, traceId, cancellationToken);
    }

    public async Task<DocumentAccessMatrixResponse> GetDocumentAccessMatrixAsync(
        ClaimsPrincipal actorPrincipal,
        Guid documentId,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var access = await documentAuthorization.RequireDocumentPermissionAsync(
            actorPrincipal,
            documentId,
            DocumentPermissionCatalog.AccessManage,
            "API_DOCUMENT_ACL_READ_DENIED",
            cancellationToken);

        return await BuildAccessMatrixResponseAsync(access.Document, traceId, cancellationToken);
    }

    public async Task<DocumentAccessMatrixResponse> ReplaceDocumentAccessAsync(
        ClaimsPrincipal actorPrincipal,
        Guid documentId,
        ReplaceDocumentAccessRequest request,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var access = await documentAuthorization.RequireDocumentPermissionAsync(
            actorPrincipal,
            documentId,
            DocumentPermissionCatalog.AccessManage,
            "API_DOCUMENT_ACL_UPDATE_DENIED",
            cancellationToken);

        var permDict = await documentAuthorization.EnsureDocumentPermissionDictionaryAsync(cancellationToken);

        var distinctRoleIds = request.RolePermissions.Select(r => r.RoleId).Distinct().ToList();
        var validRoleIds = distinctRoleIds.Count == 0
            ? new HashSet<Guid>()
            : await dbContext.Roles.AsNoTracking()
                .Where(r => distinctRoleIds.Contains(r.Id))
                .Select(r => r.Id)
                .ToHashSetAsync(cancellationToken);

        if (validRoleIds.Count != distinctRoleIds.Count)
        {
            throw ApiContractException.UnprocessableEntity(
                messageAr: "أحد الأدوار المحددة في صلاحيات الوثيقة غير موجود.",
                messageEn: "One of the specified roles was not found.");
        }

        var distinctUserIds = request.UserPermissions.Select(u => u.UserId).Distinct().ToList();
        var validUserIds = distinctUserIds.Count == 0
            ? new HashSet<Guid>()
            : await dbContext.Users.AsNoTracking()
                .Where(u => distinctUserIds.Contains(u.Id))
                .Select(u => u.Id)
                .ToHashSetAsync(cancellationToken);

        if (validUserIds.Count != distinctUserIds.Count)
        {
            throw ApiContractException.UnprocessableEntity(
                messageAr: "أحد المستخدمين المحددين في صلاحيات الوثيقة غير موجود.",
                messageEn: "One of the specified users was not found.");
        }

        var newRoleEntities = new List<DocumentRolePermissionEntity>();
        var seenRolePairs = new HashSet<(Guid RoleId, Guid PermId)>();
        foreach (var rp in request.RolePermissions)
        {
            if (!DocumentPermissionCatalog.IsDocumentPermissionCode(rp.PermissionCode))
            {
                throw ApiContractException.UnprocessableEntity(
                    messageAr: $"رمز صلاحية الوثيقة غير معروف: {rp.PermissionCode}",
                    messageEn: $"Unknown document permission code '{rp.PermissionCode}'.");
            }
            var perm = permDict[rp.PermissionCode.Trim()];
            if (!seenRolePairs.Add((rp.RoleId, perm.Id)))
            {
                throw ApiContractException.Conflict(
                    messageAr: "لا يمكن تكرار نفس الصلاحية لنفس الدور في الوثيقة.",
                    messageEn: "Duplicate role-permission entry inside document ACL payload.");
            }
            newRoleEntities.Add(new DocumentRolePermissionEntity
            {
                Id = Guid.NewGuid(),
                DocumentId = documentId,
                RoleId = rp.RoleId,
                PermissionId = perm.Id,
                Decision = rp.Decision.Trim().ToUpperInvariant()
            });
        }

        var newUserEntities = new List<DocumentUserPermissionEntity>();
        var seenUserPairs = new HashSet<(Guid UserId, Guid PermId)>();
        foreach (var up in request.UserPermissions)
        {
            if (!DocumentPermissionCatalog.IsDocumentPermissionCode(up.PermissionCode))
            {
                throw ApiContractException.UnprocessableEntity(
                    messageAr: $"رمز صلاحية الوثيقة غير معروف: {up.PermissionCode}",
                    messageEn: $"Unknown document permission code '{up.PermissionCode}'.");
            }
            var perm = permDict[up.PermissionCode.Trim()];
            if (!seenUserPairs.Add((up.UserId, perm.Id)))
            {
                throw ApiContractException.Conflict(
                    messageAr: "لا يمكن تكرار نفس الصلاحية لنفس المستخدم في الوثيقة.",
                    messageEn: "Duplicate user-permission entry inside document ACL payload.");
            }
            newUserEntities.Add(new DocumentUserPermissionEntity
            {
                Id = Guid.NewGuid(),
                DocumentId = documentId,
                UserId = up.UserId,
                PermissionId = perm.Id,
                Decision = up.Decision.Trim().ToUpperInvariant()
            });
        }

        // Prevent leaving a document completely orphaned without any active holder of doc.access.manage
        await EnsureAtLeastOneDocumentAccessManagerRemainsAsync(
            documentId,
            permDict[DocumentPermissionCatalog.AccessManage].Id,
            newRoleEntities,
            newUserEntities,
            cancellationToken);

        await using var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var existingRoleAcl = await dbContext.DocumentRolePermissions
            .Where(r => r.DocumentId == documentId)
            .ToListAsync(cancellationToken);
        var existingUserAcl = await dbContext.DocumentUserPermissions
            .Where(u => u.DocumentId == documentId)
            .ToListAsync(cancellationToken);

        dbContext.DocumentRolePermissions.RemoveRange(existingRoleAcl);
        dbContext.DocumentUserPermissions.RemoveRange(existingUserAcl);
        await dbContext.SaveChangesAsync(cancellationToken);

        dbContext.DocumentRolePermissions.AddRange(newRoleEntities);
        dbContext.DocumentUserPermissions.AddRange(newUserEntities);
        await dbContext.SaveChangesAsync(cancellationToken);

        await serverAuthorization.WriteAuditEventAsync(
            access.Actor.Id,
            "API_DOCUMENT_ACL_REPLACED",
            "API_DOCUMENT",
            documentId,
            "SUCCESS",
            new
            {
                roleRulesCount = newRoleEntities.Count,
                userRulesCount = newUserEntities.Count
            },
            cancellationToken);

        await tx.CommitAsync(cancellationToken);

        return await BuildAccessMatrixResponseAsync(access.Document, traceId, cancellationToken);
    }

    // ========================================================================
    // Validation, Anti-Lockout & Projection Helpers
    // ========================================================================
    private async Task EnsureAtLeastOneDocumentAccessManagerRemainsAsync(
        Guid documentId,
        Guid accessManagePermId,
        IReadOnlyList<DocumentRolePermissionEntity> proposedRoleAcl,
        IReadOnlyList<DocumentUserPermissionEntity> proposedUserAcl,
        CancellationToken cancellationToken)
    {
        var activeUsers = await dbContext.Users.AsNoTracking()
            .Where(u => u.IsActive && u.PasswordHash != null)
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);

        var activeUserRoles = await (
            from ur in dbContext.UserRoles.AsNoTracking()
            join r in dbContext.Roles.AsNoTracking() on ur.RoleId equals r.Id
            where activeUsers.Contains(ur.UserId) && r.IsActive
            select new { ur.UserId, ur.RoleId })
            .ToListAsync(cancellationToken);

        var rolesByUser = activeUserRoles
            .GroupBy(x => x.UserId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.RoleId).ToList());

        var roleDecisionMap = proposedRoleAcl
            .Where(x => x.PermissionId == accessManagePermId)
            .ToDictionary(x => x.RoleId, x => x.Decision);

        var userDecisionMap = proposedUserAcl
            .Where(x => x.PermissionId == accessManagePermId)
            .ToDictionary(x => x.UserId, x => x.Decision);

        var anyHolder = false;
        foreach (var uid in activeUsers)
        {
            userDecisionMap.TryGetValue(uid, out var userDecision);
            var roleDecisions = rolesByUser.TryGetValue(uid, out var rids)
                ? rids.Where(roleDecisionMap.ContainsKey).Select(rid => roleDecisionMap[rid]).ToList()
                : null;

            var res = DocumentAuthorizationService.ResolveSinglePermission(userDecision, roleDecisions);
            if (res.Allowed)
            {
                anyHolder = true;
                break;
            }
        }

        if (!anyHolder)
        {
            throw ApiContractException.UnprocessableEntity(
                messageAr: "لا يمكن حفظ صلاحيات الوثيقة لأنها ستفقد آخر مستخدم نشط يملك صلاحية إدارة الوصول للوثيقة (doc.access.manage).",
                messageEn: "Cannot save document ACL because no active user would retain doc.access.manage permission on this document.");
        }
    }

    private async Task ValidateDocumentPartiesAndConfigAsync(
        Guid? providerInternalSystemId,
        Guid? providerExternalSystemId,
        Guid? consumerCompanySystemId,
        Guid? consumerEntityId,
        Guid apiGroupId,
        Guid requirementTemplateId,
        Guid approvalRouteId,
        Guid changePolicyId,
        Guid? excludeDocumentId,
        CancellationToken cancellationToken)
    {
        var isInternalProv = providerInternalSystemId is not null && providerInternalSystemId != Guid.Empty;
        var isExternalProv = providerExternalSystemId is not null && providerExternalSystemId != Guid.Empty;
        if (isInternalProv == isExternalProv)
        {
            throw ApiContractException.UnprocessableEntity(
                messageAr: "يجب اختيار مزوّد واحد فقط (داخلي أو خارجي).",
                messageEn: "Exactly one provider (internal or external) must be selected.");
        }

        var isCompConsumer = consumerCompanySystemId is not null && consumerCompanySystemId != Guid.Empty;
        var isEntityConsumer = consumerEntityId is not null && consumerEntityId != Guid.Empty;
        if (isCompConsumer == isEntityConsumer)
        {
            throw ApiContractException.UnprocessableEntity(
                messageAr: "يجب اختيار مستهلك واحد فقط (نظام شركة أو جهة مستهلكة).",
                messageEn: "Exactly one consumer (company system or entity) must be selected.");
        }

        if (isInternalProv && isCompConsumer && providerInternalSystemId == consumerCompanySystemId)
        {
            throw ApiContractException.UnprocessableEntity(
                messageAr: "لا يمكن اختيار النظام الداخلي نفسه مزودًا ومستهلكًا في الوثيقة نفسها.",
                messageEn: "The same internal company system cannot serve as both provider and consumer.");
        }

        if (isInternalProv)
        {
            var validInternalProv = await (
                from ip in dbContext.InternalProviderSystems.AsNoTracking()
                join cs in dbContext.CompanySystems.AsNoTracking() on ip.CompanySystemId equals cs.Id
                where ip.CompanySystemId == providerInternalSystemId!.Value && ip.IsActive && cs.IsActive
                select ip.Id)
                .AnyAsync(cancellationToken);
            if (!validInternalProv)
            {
                throw ApiContractException.UnprocessableEntity(
                    messageAr: "المزوّد الداخلي المحدد غير موجود أو معطّل.",
                    messageEn: "Selected internal provider system was not found or is inactive.");
            }
        }
        else
        {
            var validExtProv = await dbContext.ExternalProviderSystems.AsNoTracking()
                .AnyAsync(e => e.Id == providerExternalSystemId!.Value && e.IsActive, cancellationToken);
            if (!validExtProv)
            {
                throw ApiContractException.UnprocessableEntity(
                    messageAr: "المزوّد الخارجي المحدد غير موجود أو معطّل.",
                    messageEn: "Selected external provider was not found or is inactive.");
            }
        }

        if (isCompConsumer)
        {
            var validCompCons = await dbContext.CompanySystems.AsNoTracking()
                .AnyAsync(cs => cs.Id == consumerCompanySystemId!.Value && cs.IsActive, cancellationToken);
            if (!validCompCons)
            {
                throw ApiContractException.UnprocessableEntity(
                    messageAr: "نظام الشركة المستهلك المحدد غير موجود أو معطّل.",
                    messageEn: "Selected consumer company system was not found or is inactive.");
            }
        }
        else
        {
            var validEntCons = await (
                from ce in dbContext.ConsumerEntities.AsNoTracking()
                join cc in dbContext.ConsumerCategories.AsNoTracking() on ce.CategoryId equals cc.Id
                where ce.Id == consumerEntityId!.Value && ce.IsActive && cc.IsActive
                select ce.Id)
                .AnyAsync(cancellationToken);
            if (!validEntCons)
            {
                throw ApiContractException.UnprocessableEntity(
                    messageAr: "الجهة المستهلكة المحددة غير موجودة أو معطّلة.",
                    messageEn: "Selected consumer entity was not found or is inactive.");
            }
        }

        var groupOk = await dbContext.ApiGroups.AsNoTracking()
            .AnyAsync(g => g.Id == apiGroupId && g.IsActive, cancellationToken);
        if (!groupOk)
        {
            throw ApiContractException.UnprocessableEntity(
                messageAr: "مجموعة API المحددة غير موجودة أو معطّلة.",
                messageEn: "Selected API group was not found or is inactive.");
        }

        var tplOk = await dbContext.FileRequirementTemplates.AsNoTracking()
            .AnyAsync(t => t.Id == requirementTemplateId && t.IsActive, cancellationToken);
        if (!tplOk)
        {
            throw ApiContractException.UnprocessableEntity(
                messageAr: "قالب متطلبات الملفات المحدد غير موجود أو معطّل.",
                messageEn: "Selected file requirement template was not found or is inactive.");
        }

        var routeActive = await dbContext.ApprovalRoutes.AsNoTracking()
            .AnyAsync(r => r.Id == approvalRouteId && r.IsActive, cancellationToken);
        var routeHasStages = await dbContext.ApprovalRouteStages.AsNoTracking()
            .AnyAsync(s => s.RouteId == approvalRouteId, cancellationToken);
        if (!routeActive || !routeHasStages)
        {
            throw ApiContractException.UnprocessableEntity(
                messageAr: "مسار الاعتماد المحدد غير نشط أو لا يحتوي على مراحل.",
                messageEn: "Selected approval route is inactive or has zero stages.");
        }

        var policyOk = await dbContext.ChangePolicies.AsNoTracking()
            .AnyAsync(cp => cp.Id == changePolicyId && cp.IsActive, cancellationToken);
        if (!policyOk)
        {
            throw ApiContractException.UnprocessableEntity(
                messageAr: "سياسة التغيير المحددة غير موجودة أو معطّلة.",
                messageEn: "Selected change policy was not found or is inactive.");
        }

        // OP-05 Uniqueness check across (Provider, Consumer, ApiGroup)
        var dupQuery = dbContext.ApiDocuments.AsNoTracking()
            .Where(d => d.ApiGroupId == apiGroupId
                        && d.ProviderInternalSystemId == (isInternalProv ? providerInternalSystemId : null)
                        && d.ProviderExternalSystemId == (isExternalProv ? providerExternalSystemId : null)
                        && d.ConsumerCompanySystemId == (isCompConsumer ? consumerCompanySystemId : null)
                        && d.ConsumerEntityId == (isEntityConsumer ? consumerEntityId : null));

        if (excludeDocumentId.HasValue)
        {
            dupQuery = dupQuery.Where(d => d.Id != excludeDocumentId.Value);
        }

        if (await dupQuery.AnyAsync(cancellationToken))
        {
            throw ApiContractException.Conflict(
                messageAr: "توجد وثيقة API مسجّلة مسبقًا لنفس المزوّد والمستهلك ومجموعة API (OP-05).",
                messageEn: "An API document already exists for this provider, consumer, and API group.");
        }
    }

    private async Task<List<Guid>> ValidateDeveloperUserIdsAsync(
        IReadOnlyList<Guid> requestedIds,
        CancellationToken cancellationToken)
    {
        var distinct = requestedIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (distinct.Count == 0)
        {
            return [];
        }

        var users = await dbContext.Users.AsNoTracking()
            .Where(u => distinct.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, cancellationToken);

        foreach (var uid in distinct)
        {
            if (!users.TryGetValue(uid, out var user))
            {
                throw ApiContractException.UnprocessableEntity(
                    messageAr: "أحد المطورين المحددين غير موجود.",
                    messageEn: "One of the selected developer users was not found.");
            }
            if (!user.IsActive)
            {
                throw ApiContractException.UnprocessableEntity(
                    messageAr: $"لا يمكن إسناد المستخدم المعطّل ({user.FullName}) كمطور للوثيقة.",
                    messageEn: $"Inactive user '{user.FullName}' cannot be assigned as a developer.");
            }
        }

        return distinct;
    }

    private async Task<List<ApiDocumentDto>> BuildDocumentDtosAsync(
        IReadOnlyList<ApiDocumentEntity> documents,
        IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, DocumentPermissionResolution>> accessMap,
        string traceId,
        CancellationToken cancellationToken)
    {
        if (documents.Count == 0)
        {
            return [];
        }

        var docIds = documents.Select(d => d.Id).ToList();
        var compIds = documents.SelectMany(d => new[] { d.ProviderInternalSystemId, d.ConsumerCompanySystemId })
            .Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();
        var extProvIds = documents.Where(d => d.ProviderExternalSystemId.HasValue).Select(d => d.ProviderExternalSystemId!.Value).Distinct().ToList();
        var consEntIds = documents.Where(d => d.ConsumerEntityId.HasValue).Select(d => d.ConsumerEntityId!.Value).Distinct().ToList();
        var groupIds = documents.Select(d => d.ApiGroupId).Distinct().ToList();
        var tplIds = documents.Select(d => d.RequirementTemplateId).Distinct().ToList();
        var routeIds = documents.Select(d => d.ApprovalRouteId).Distinct().ToList();
        var policyIds = documents.Select(d => d.ChangePolicyId).Distinct().ToList();
        var creatorIds = documents.Select(d => d.CreatedBy).Distinct().ToList();

        var compMap = compIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await dbContext.CompanySystems.AsNoTracking().Where(c => compIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);
        var extMap = extProvIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await dbContext.ExternalProviderSystems.AsNoTracking().Where(e => extProvIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.Name, cancellationToken);
        var consMap = consEntIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await dbContext.ConsumerEntities.AsNoTracking().Where(c => consEntIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);

        var groupMap = await dbContext.ApiGroups.AsNoTracking().Where(g => groupIds.Contains(g.Id)).ToDictionaryAsync(g => g.Id, g => g.Name, cancellationToken);
        var tplMap = await dbContext.FileRequirementTemplates.AsNoTracking().Where(t => tplIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Name, cancellationToken);
        var routeMap = await dbContext.ApprovalRoutes.AsNoTracking().Where(r => routeIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Name, cancellationToken);
        var policyMap = await dbContext.ChangePolicies.AsNoTracking().Where(p => policyIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name, cancellationToken);

        var integrationMap = await dbContext.Integrations.AsNoTracking()
            .Where(i => docIds.Contains(i.DocumentId))
            .ToDictionaryAsync(i => i.DocumentId, cancellationToken);

        var devRows = await (
            from dd in dbContext.DocumentDevelopers.AsNoTracking()
            join u in dbContext.Users.AsNoTracking() on dd.UserId equals u.Id
            where docIds.Contains(dd.DocumentId)
            orderby u.FullName
            select new { dd.DocumentId, Dev = new AssignedDeveloperDto(u.Id, u.FullName, u.Email, u.IsActive) })
            .ToListAsync(cancellationToken);

        var devsByDoc = devRows
            .GroupBy(x => x.DocumentId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<AssignedDeveloperDto>)g.Select(x => x.Dev).ToList());

        var creatorMap = await dbContext.Users.AsNoTracking()
            .Where(u => creatorIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, cancellationToken);

        return documents.Select(d =>
        {
            var providerName = d.ProviderInternalSystemId.HasValue
                ? compMap.GetValueOrDefault(d.ProviderInternalSystemId.Value, string.Empty)
                : extMap.GetValueOrDefault(d.ProviderExternalSystemId!.Value, string.Empty);

            var consumerName = d.ConsumerCompanySystemId.HasValue
                ? compMap.GetValueOrDefault(d.ConsumerCompanySystemId.Value, string.Empty)
                : consMap.GetValueOrDefault(d.ConsumerEntityId!.Value, string.Empty);

            // Derived document title per 01_PRD_v1.2.md (strictly server-computed from provider & consumer)
            var derivedTitle = $"{providerName} ← {consumerName}";
            var integration = integrationMap.GetValueOrDefault(d.Id);

            IReadOnlyList<string> myPerms = accessMap.TryGetValue(d.Id, out var permsMap)
                ? permsMap.Where(kv => kv.Value.Allowed).Select(kv => kv.Key).OrderBy(k => k, StringComparer.Ordinal).ToList()
                : [];

            return new ApiDocumentDto(
                d.Id,
                derivedTitle,
                d.ProviderInternalSystemId.HasValue ? "INTERNAL" : "EXTERNAL",
                d.ProviderInternalSystemId,
                d.ProviderExternalSystemId,
                providerName,
                d.ConsumerCompanySystemId.HasValue ? "COMPANY_SYSTEM" : "CONSUMER_ENTITY",
                d.ConsumerCompanySystemId,
                d.ConsumerEntityId,
                consumerName,
                d.ApiGroupId,
                groupMap.GetValueOrDefault(d.ApiGroupId, string.Empty),
                d.RequirementTemplateId,
                tplMap.GetValueOrDefault(d.RequirementTemplateId, string.Empty),
                d.ApprovalRouteId,
                routeMap.GetValueOrDefault(d.ApprovalRouteId, string.Empty),
                d.ChangePolicyId,
                policyMap.GetValueOrDefault(d.ChangePolicyId, string.Empty),
                d.PublicationOption,
                d.State,
                d.CreatedBy,
                creatorMap.GetValueOrDefault(d.CreatedBy, string.Empty),
                d.CreatedAt,
                HasIntegration: integration is not null,
                IntegrationId: integration?.Id,
                IntegrationReferenceCode: integration?.ReferenceCode,
                AssignedDevelopers: devsByDoc.GetValueOrDefault(d.Id, []),
                MyDocumentPermissions: myPerms,
                TraceId: traceId);
        }).ToList();
    }

    private async Task<DocumentAccessMatrixResponse> BuildAccessMatrixResponseAsync(
        ApiDocumentEntity document,
        string traceId,
        CancellationToken cancellationToken)
    {
        var permDict = await documentAuthorization.EnsureDocumentPermissionDictionaryAsync(cancellationToken);
        var permIdToCode = permDict.Values.ToDictionary(p => p.Id, p => p.Code);

        var summaryDto = (await BuildDocumentDtosAsync(
            [document],
            new Dictionary<Guid, IReadOnlyDictionary<string, DocumentPermissionResolution>>(),
            traceId,
            cancellationToken))[0];

        var columns = DocumentPermissionCatalog.All.Select(meta =>
            new DocumentPermissionColumnDto(
                permDict[meta.Code].Id,
                meta.Code,
                meta.ActionKey,
                meta.LabelAr,
                meta.LabelEn)).ToList();

        var roles = await dbContext.Roles.AsNoTracking()
            .OrderBy(r => r.Name)
            .ToListAsync(cancellationToken);

        var roleAclRows = await dbContext.DocumentRolePermissions.AsNoTracking()
            .Where(drp => drp.DocumentId == document.Id)
            .ToListAsync(cancellationToken);

        var roleAclLookup = roleAclRows
            .GroupBy(x => x.RoleId)
            .ToDictionary(
                g => g.Key,
                g => g.Where(x => permIdToCode.ContainsKey(x.PermissionId))
                      .ToDictionary(x => permIdToCode[x.PermissionId], x => x.Decision, StringComparer.OrdinalIgnoreCase));

        var roleDtos = roles.Select(r =>
        {
            roleAclLookup.TryGetValue(r.Id, out var rMap);
            var decisions = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var col in columns)
            {
                decisions[col.Code] = rMap is not null && rMap.TryGetValue(col.Code, out var d) ? d : null;
            }
            return new DocumentRoleAccessRowDto(r.Id, r.Name, r.IsActive, decisions);
        }).ToList();

        var users = await dbContext.Users.AsNoTracking()
            .OrderBy(u => u.FullName)
            .ToListAsync(cancellationToken);

        var assignedDevSet = await dbContext.DocumentDevelopers.AsNoTracking()
            .Where(dd => dd.DocumentId == document.Id)
            .Select(dd => dd.UserId)
            .ToHashSetAsync(cancellationToken);

        var userRolesRows = await (
            from ur in dbContext.UserRoles.AsNoTracking()
            join r in dbContext.Roles.AsNoTracking() on ur.RoleId equals r.Id
            where r.IsActive
            orderby r.Name
            select new { ur.UserId, RoleId = r.Id, RoleName = r.Name })
            .ToListAsync(cancellationToken);

        var userRolesByUser = userRolesRows
            .GroupBy(x => x.UserId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var userAclRows = await dbContext.DocumentUserPermissions.AsNoTracking()
            .Where(dup => dup.DocumentId == document.Id)
            .ToListAsync(cancellationToken);

        var userAclLookup = userAclRows
            .GroupBy(x => x.UserId)
            .ToDictionary(
                g => g.Key,
                g => g.Where(x => permIdToCode.ContainsKey(x.PermissionId))
                      .ToDictionary(x => permIdToCode[x.PermissionId], x => x.Decision, StringComparer.OrdinalIgnoreCase));

        var userDtos = users.Select(u =>
        {
            var activeRoles = userRolesByUser.GetValueOrDefault(u.Id) ?? [];
            userAclLookup.TryGetValue(u.Id, out var explicitMap);

            var cells = new Dictionary<string, DocumentUserEffectiveCellDto>(StringComparer.OrdinalIgnoreCase);
            foreach (var col in columns)
            {
                string? explicitDecision = null;
                explicitMap?.TryGetValue(col.Code, out explicitDecision);

                var roleDecisionsForCell = new List<string>();
                foreach (var ar in activeRoles)
                {
                    if (roleAclLookup.TryGetValue(ar.RoleId, out var rMap) &&
                        rMap.TryGetValue(col.Code, out var rDec))
                    {
                        roleDecisionsForCell.Add(rDec);
                    }
                }

                var resolution = DocumentAuthorizationService.ResolveSinglePermission(
                    explicitDecision,
                    roleDecisionsForCell);

                cells[col.Code] = new DocumentUserEffectiveCellDto(
                    resolution.Allowed,
                    resolution.ResolutionSource,
                    explicitDecision);
            }

            return new DocumentUserAccessRowDto(
                u.Id,
                u.FullName,
                u.Email,
                u.IsActive,
                activeRoles.Select(r => r.RoleName).ToList(),
                assignedDevSet.Contains(u.Id),
                cells);
        }).ToList();

        return new DocumentAccessMatrixResponse(
            document.Id,
            summaryDto.DerivedTitle,
            summaryDto.ApiGroupName,
            document.CreatedBy,
            summaryDto.CreatedByName,
            columns,
            roleDtos,
            userDtos,
            traceId);
    }
}

