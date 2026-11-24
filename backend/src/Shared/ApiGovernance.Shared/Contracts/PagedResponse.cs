namespace ApiGovernance.Shared.Contracts;

public static class PaginationHelper
{
    public static (int SafePage, int SafePageSize, int TotalPages, int NormalizedPage) Compute(
        int? page,
        int? pageSize,
        int totalCount,
        int defaultPageSize = 20,
        int maxPageSize = 100)
    {
        var safePage = Math.Max(1, page ?? 1);
        var safePageSize = Math.Clamp(pageSize ?? defaultPageSize, 1, maxPageSize);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)safePageSize));
        var normalizedPage = Math.Min(safePage, totalPages);
        return (safePage, safePageSize, totalPages, normalizedPage);
    }
}

