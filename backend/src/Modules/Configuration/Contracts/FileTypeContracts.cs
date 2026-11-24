using System.ComponentModel.DataAnnotations;

namespace ApiGovernance.Modules.Configuration.Contracts;

public sealed record FileTypeCatalogEntryDto(
    string CatalogCode,
    string DefaultNameAr,
    string DefaultNameEn,
    IReadOnlyList<string> AllowedExtensions,
    long DefaultMaxSizeBytes,
    bool SupportsWordTemplate);

public sealed record FileTypeDto(
    Guid Id,
    string CatalogCode,
    string DisplayName,
    long MaxSizeBytes,
    Guid? WordTemplateObjectId,
    bool IsActive,
    bool SupportsWordTemplate,
    IReadOnlyList<string> Extensions,
    int RequirementRulesCount,
    string TraceId);

public sealed record CreateFileTypeRequest : IValidatableObject
{
    [Required(ErrorMessage = "رمز النوع القياسي مطلوب / CatalogCode is required.")]
    [MaxLength(80)]
    public string CatalogCode { get; init; } = string.Empty;

    [Required(ErrorMessage = "الاسم الظاهر لنوع الملف مطلوب / DisplayName is required.")]
    [MinLength(2, ErrorMessage = "يجب ألا يقل الاسم عن حرفين / DisplayName must be at least 2 characters.")]
    [MaxLength(140)]
    public string DisplayName { get; init; } = string.Empty;

    [Range(1, 104_857_600, ErrorMessage = "الحد الأقصى للحجم يجب أن يكون بين 1 بايت و100 ميجابايت / MaxSizeBytes must be between 1 and 104857600.")]
    public long MaxSizeBytes { get; init; }

    public Guid? WordTemplateObjectId { get; init; }

    /// <summary>
    /// Optional subset of extensions; if omitted, all catalog extensions for CatalogCode are populated automatically.
    /// Any extension outside the approved catalog for CatalogCode is rejected.
    /// </summary>
    public IReadOnlyList<string>? Extensions { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(CatalogCode))
        {
            yield return new ValidationResult(
                "رمز النوع القياسي مطلوب / CatalogCode is required.",
                [nameof(CatalogCode)]);
        }

        if (string.IsNullOrWhiteSpace(DisplayName) || DisplayName.Trim().Length < 2)
        {
            yield return new ValidationResult(
                "الاسم الظاهر مطلوب (حرفان على الأقل) / DisplayName must be at least 2 characters.",
                [nameof(DisplayName)]);
        }
    }
}

public sealed record UpdateFileTypeRequest : IValidatableObject
{
    [MaxLength(140)]
    public string? DisplayName { get; init; }

    [Range(1, 104_857_600, ErrorMessage = "الحد الأقصى للحجم يجب أن يكون بين 1 بايت و100 ميجابايت / MaxSizeBytes must be between 1 and 104857600.")]
    public long? MaxSizeBytes { get; init; }

    public Guid? WordTemplateObjectId { get; init; }

    public bool ClearWordTemplate { get; init; }

    public bool? IsActive { get; init; }

    public IReadOnlyList<string>? Extensions { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (DisplayName is not null && DisplayName.Trim().Length < 2)
        {
            yield return new ValidationResult(
                "يجب ألا يقل الاسم عن حرفين / DisplayName must be at least 2 characters.",
                [nameof(DisplayName)]);
        }

        if (DisplayName is null && MaxSizeBytes is null && WordTemplateObjectId is null && !ClearWordTemplate && IsActive is null && Extensions is null)
        {
            yield return new ValidationResult(
                "يجب تحديد حقل واحد على الأقل للتحديث / At least one field is required to update.",
                [nameof(DisplayName)]);
        }
    }
}

/// <summary>
/// Curated common file types dictionary per 01_PRD_v1.2.md &amp; 02_UX_UI_v1.2.md (UI-04 / M04).
/// Extensions are auto-populated from this catalog and strictly verified against executable/unapproved extensions.
/// </summary>
public static class CommonFileTypeCatalog
{
    public const string WordCatalogCode = "WORD_DOC";

    public static readonly IReadOnlyList<FileTypeCatalogEntryDto> Entries =
    [
        new("OPENAPI_SPEC", "مواصفة OpenAPI / Swagger", "OpenAPI / Swagger Specification", [".yaml", ".yml", ".json"], 10 * 1024 * 1024L, false),
        new(WordCatalogCode, "مستند Word التوثيقي", "Microsoft Word Document (.docx)", [".docx"], 20 * 1024 * 1024L, true),
        new("PDF_DOC", "مستند PDF", "PDF Document", [".pdf"], 25 * 1024 * 1024L, false),
        new("JSON_SCHEMA", "مخطط JSON", "JSON Schema", [".json"], 5 * 1024 * 1024L, false),
        new("POSTMAN_COLLECTION", "مجموعة Postman", "Postman Collection", [".json"], 10 * 1024 * 1024L, false),
        new("MARKDOWN_DOC", "ملف توثيق Markdown", "Markdown Documentation", [".md", ".markdown"], 5 * 1024 * 1024L, false),
        new("EXCEL_SHEET", "جدول بيانات Excel", "Excel Workbook (.xlsx)", [".xlsx"], 15 * 1024 * 1024L, false),
        new("ARCHIVE_ZIP", "حزمة ملفات مضغوطة (ZIP)", "Compressed ZIP Archive", [".zip"], 50 * 1024 * 1024L, false)
    ];

    private static readonly Dictionary<string, FileTypeCatalogEntryDto> ByCode =
        Entries.ToDictionary(e => e.CatalogCode, StringComparer.OrdinalIgnoreCase);

    public static bool TryGet(string catalogCode, out FileTypeCatalogEntryDto entry)
        => ByCode.TryGetValue(catalogCode.Trim(), out entry!);
}

