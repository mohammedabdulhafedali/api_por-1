using System.ComponentModel.DataAnnotations;

namespace ApiGovernance.Shared.Contracts;

/// <summary>
/// Standardized pagination request query parameters per Backend API Specification v0.1 Section 3.
/// </summary>
public sealed class PagedQueryRequest : IValidatableObject
{
    [Range(1, 10_000, ErrorMessage = "Page number must be between 1 and 10000.")]
    public int Page { get; init; } = 1;

    [Range(1, 100, ErrorMessage = "PageSize must be between 1 and 100.")]
    public int PageSize { get; init; } = 20;

    [MaxLength(64, ErrorMessage = "SortBy cannot exceed 64 characters.")]
    public string? SortBy { get; init; }

    [RegularExpression("^(asc|desc|ASC|DESC)$", ErrorMessage = "SortDirection must be 'asc' or 'desc'.")]
    public string SortDirection { get; init; } = "asc";

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Page < 1)
        {
            yield return new ValidationResult("Page must be greater than or equal to 1.", [nameof(Page)]);
        }

        if (PageSize is < 1 or > 100)
        {
            yield return new ValidationResult("PageSize must be between 1 and 100.", [nameof(PageSize)]);
        }
    }
}

/// <summary>
/// Standardized paginated response envelope.
/// </summary>
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

/// <summary>
/// Contract validation probe payload used ONLY in Development/Testing (F07/F06) to verify input validation
/// and deterministic Problem Details mapping (400/401/403/404/409/413/422/429/500) without business logic.
/// Note: 413 and 429 test the Problem Details error contract structure only; actual file size limits (V02)
/// and authentication rate-limiting policies (I01) are implemented in their designated tasks.
/// </summary>
public sealed class ContractValidationProbeRequest : IValidatableObject
{
    [Required(ErrorMessage = "Identifier code is required.")]
    [MinLength(3, ErrorMessage = "Identifier code must be at least 3 characters.")]
    [MaxLength(64, ErrorMessage = "Identifier code cannot exceed 64 characters.")]
    [RegularExpression(@"^[A-Za-z0-9\-_\.]+$", ErrorMessage = "Identifier code may only contain alphanumeric characters, hyphens, underscores, or dots.")]
    public string? Code { get; init; }

    [Required(ErrorMessage = "DisplayTitle is required.")]
    [MinLength(2, ErrorMessage = "DisplayTitle must be at least 2 characters.")]
    [MaxLength(150, ErrorMessage = "DisplayTitle cannot exceed 150 characters.")]
    public string? DisplayTitle { get; init; }

    [Range(1, 100, ErrorMessage = "PageSize must be between 1 and 100.")]
    public int PageSize { get; init; } = 20;

    /// <summary>
    /// Optional status simulation trigger for contract testing (none, conflict, unprocessable, forbidden, unauthorized, notfound, payloadtoolarge, toomanyrequests, error).
    /// </summary>
    [RegularExpression(@"^(none|conflict|unprocessable|forbidden|unauthorized|notfound|payloadtoolarge|toomanyrequests|error)$", ErrorMessage = "SimulateOutcome must be one of: none, conflict, unprocessable, forbidden, unauthorized, notfound, payloadtoolarge, toomanyrequests, error.")]
    public string SimulateOutcome { get; init; } = "none";

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!string.IsNullOrWhiteSpace(Code) &&
            string.Equals(Code.Trim(), "RESERVED", StringComparison.OrdinalIgnoreCase))
        {
            yield return new ValidationResult(
                "The code 'RESERVED' is reserved by platform contract rules.",
                [nameof(Code)]);
        }
    }
}

/// <summary>
/// Response DTO returned when a contract validation probe succeeds.
/// </summary>
public sealed record ContractValidationProbeResponse(
    string ApiVersion,
    string Status,
    string NormalizedCode,
    string DisplayTitle,
    int PageSize,
    string TraceId,
    DateTimeOffset TimestampUtc);

/// <summary>
/// Metadata summary describing the API contract standards enforced in F07.
/// </summary>
public sealed record ApiContractMetadataResponse(
    string ApiVersion,
    string BasePath,
    string ErrorFormat,
    string CorrelationHeader,
    int CatalogEndpointCount,
    int ModuleCount,
    IReadOnlyList<int> SupportedErrorStatusCodes,
    string TraceId);
