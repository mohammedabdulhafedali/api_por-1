using ApiGovernance.Modules.IdentityAccess.Contracts;
using ApiGovernance.Shared.Persistence;
using ApiGovernance.Shared.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace ApiGovernance.Modules.IdentityAccess.Services;

internal static class CanonicalPermissionSyncHelper
{
    /// <summary>
    /// Seeds any missing canonical permission codes into the permanent permissions dictionary and
    /// one-time backfills newly introduced split permissions onto roles that already hold the
    /// corresponding legacy parent permission.
    /// </summary>
    public static async Task EnsureSeededAndBackfilledAsync(
        ApiGovernanceDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var existingRows = await dbContext.Permissions.AsNoTracking()
            .Select(p => new { p.Id, p.Code })
            .ToListAsync(cancellationToken);

        var idByCode = existingRows.ToDictionary(
            x => x.Code,
            x => x.Id,
            StringComparer.OrdinalIgnoreCase);

        var newlyAddedEntities = new List<PermissionEntity>();

        foreach (var entry in PlatformPermissionCatalog.CanonicalEntries)
        {
            if (!idByCode.ContainsKey(entry.Code))
            {
                var entity = new PermissionEntity
                {
                    Id = Guid.NewGuid(),
                    Code = entry.Code,
                    Resource = entry.Resource,
                    Action = entry.Action
                };
                dbContext.Permissions.Add(entity);
                idByCode[entry.Code] = entity.Id;
                newlyAddedEntities.Add(entity);
            }
        }

        if (newlyAddedEntities.Count == 0)
        {
            return;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        // One-time backfill for newly created split permissions onto existing roles holding the legacy parent permission
        var parentCodesNeeded = newlyAddedEntities
            .Select(e => PlatformPermissionCatalog.SplitPermissionToLegacyParent.GetValueOrDefault(e.Code))
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (parentCodesNeeded.Count == 0)
        {
            return;
        }

        var parentIds = parentCodesNeeded
            .Where(c => idByCode.ContainsKey(c!))
            .Select(c => idByCode[c!])
            .Distinct()
            .ToList();

        if (parentIds.Count == 0)
        {
            return;
        }

        var roleParents = await dbContext.RolePermissions.AsNoTracking()
            .Where(rp => parentIds.Contains(rp.PermissionId))
            .Select(rp => new { rp.RoleId, rp.PermissionId })
            .ToListAsync(cancellationToken);

        if (roleParents.Count == 0)
        {
            return;
        }

        var rolesByParentId = roleParents
            .GroupBy(x => x.PermissionId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.RoleId).Distinct().ToList());

        var addedAnyGrant = false;
        foreach (var added in newlyAddedEntities)
        {
            if (!PlatformPermissionCatalog.SplitPermissionToLegacyParent.TryGetValue(added.Code, out var parentCode) ||
                !idByCode.TryGetValue(parentCode, out var parentId) ||
                !rolesByParentId.TryGetValue(parentId, out var roleIds))
            {
                continue;
            }

            foreach (var roleId in roleIds)
            {
                dbContext.RolePermissions.Add(new RolePermissionEntity
                {
                    Id = Guid.NewGuid(),
                    RoleId = roleId,
                    PermissionId = added.Id
                });
                addedAnyGrant = true;
            }
        }

        if (addedAnyGrant)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
