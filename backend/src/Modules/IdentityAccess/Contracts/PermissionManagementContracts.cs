using System.ComponentModel.DataAnnotations;

namespace ApiGovernance.Modules.IdentityAccess.Contracts;

public sealed record MatrixActionColumnDto(
    string Id,
    string LabelAr,
    string LabelEn);

public sealed record MatrixScreenRowDto(
    string Id,
    string GroupId,
    string GroupLabelAr,
    string GroupLabelEn,
    string TitleAr,
    string TitleEn,
    string TechnicalPath,
    IReadOnlyList<string> ApplicableColumnIds);

public sealed record PermissionCatalogMetadataEntry(
    string Code,
    string Resource,
    string Action,
    string ScreenId,
    string GroupId,
    string ColumnId);

public sealed record PermissionDictionaryItemDto(
    Guid Id,
    string Code,
    string Resource,
    string Action,
    string ScreenId,
    string GroupId,
    string ColumnId);

public sealed record PermissionsCatalogResponse(
    IReadOnlyList<PermissionDictionaryItemDto> Permissions,
    IReadOnlyList<MatrixActionColumnDto> Columns,
    IReadOnlyList<MatrixScreenRowDto> Screens,
    string TraceId);

public sealed record RolePermissionsResponse(
    Guid RoleId,
    string RoleName,
    bool IsActive,
    bool IsPinned,
    IReadOnlyList<Guid> PermissionIds,
    IReadOnlyList<string> PermissionCodes,
    string TraceId);

public sealed record ReplaceRolePermissionsRequest
{
    [Required(ErrorMessage = "قائمة معرفات الصلاحيات مطلوبة / PermissionIds collection is required.")]
    public IReadOnlyList<Guid> PermissionIds { get; init; } = [];
}

public sealed record UserPermissionEntryDto(
    Guid PermissionId,
    string Code,
    string Resource,
    string Action,
    string ScreenId,
    string GroupId,
    string ColumnId,
    bool IsInherited,
    IReadOnlyList<RoleSummaryDto> InheritedFromRoles,
    string? OverrideDecision,
    bool EffectiveAllowed,
    string ResolutionSource);

public sealed record UserPermissionsMatrixResponse(
    Guid UserId,
    string FullName,
    string Email,
    bool IsActive,
    IReadOnlyList<RoleSummaryDto> ActiveRoles,
    IReadOnlyList<UserPermissionEntryDto> Permissions,
    IReadOnlyList<MatrixActionColumnDto> Columns,
    IReadOnlyList<MatrixScreenRowDto> Screens,
    int EffectiveAllowedCount,
    string TraceId);

public sealed record UserPermissionOverrideItemInput
{
    [Required]
    public Guid PermissionId { get; init; }

    [Required(ErrorMessage = "نوع القرار الفردي مطلوب (GRANT أو REVOKE) / Override decision is required.")]
    public string Decision { get; init; } = string.Empty;
}

public sealed record ReplaceUserPermissionOverridesRequest
{
    [Required(ErrorMessage = "قائمة الاستثناءات الفردية مطلوبة / Overrides collection is required.")]
    public IReadOnlyList<UserPermissionOverrideItemInput> Overrides { get; init; } = [];
}

/// <summary>
/// Canonical platform permission dictionary &amp; UI-25/UI-15A screen-action matrix catalog
/// derived strictly from docs/baseline/05_API_catalog_v1.2.csv &amp; 02_UX_UI_v1.2.md.
/// Permissions are permanent dictionary items and are never deleted.
/// </summary>
public static class PlatformPermissionCatalog
{
    public static readonly IReadOnlyList<MatrixActionColumnDto> ActionColumns =
    [
        new("view", "عرض", "View"),
        new("create", "إضافة / رفع", "Create / Upload"),
        new("edit", "تعديل", "Edit"),
        new("delete", "حذف", "Delete"),
        new("manage", "إدارة / تهيئة", "Manage"),
        new("assign", "إسناد / تعيين", "Assign"),
        new("reset_password", "تفعيل / كلمة المرور", "Activation / Reset"),
        new("submit", "إرسال", "Submit"),
        new("decide", "اعتماد / قرار", "Decide"),
        new("withdraw", "إيقاف نشر", "Withdraw"),
        new("download", "تنزيل", "Download")
    ];

    public static readonly IReadOnlyList<MatrixScreenRowDto> ScreenRows =
    [
        // 1. Navigation & Monitoring
        new("UI-17", "navigation", "التنقل العام والمراقبة", "Global Navigation & Monitoring", "لوحة التحكم", "Dashboard", "/api/v1/dashboard", ["view"]),
        new("UI-18", "navigation", "التنقل العام والمراقبة", "Global Navigation & Monitoring", "البحث والاستعراض", "Search & Browse", "/api/v1/search", ["view"]),

        // 2. Administration & Identity
        new("UI-15", "administration", "الإدارة والهوية", "Administration & Identity", "المستخدمون", "Users", "/api/v1/users", ["view", "create", "edit", "assign", "reset_password"]),
        new("UI-25-ROLES", "administration", "الإدارة والهوية", "Administration & Identity", "الأدوار التنظيمية", "Roles Management", "/api/v1/roles", ["view", "create", "edit", "delete"]),
        new("UI-25-PERMS", "administration", "الإدارة والهوية", "Administration & Identity", "مصفوفة الصلاحيات والاستثناءات", "Permissions & Overrides", "/api/v1/permissions", ["view", "manage"]),
        new("UI-16", "administration", "الإدارة والهوية", "Administration & Identity", "سجل التدقيق", "Audit Log", "/api/v1/audit-events", ["view"]),

        // 3. Systems & Entities
        new("UI-01", "systems", "الأنظمة والجهات", "Systems & Entities", "أنظمة الشركة", "Company Systems", "/api/v1/company-systems", ["view", "create", "edit"]),
        new("UI-03", "systems", "الأنظمة والجهات", "Systems & Entities", "الأنظمة المزودة (الداخلية والخارجية)", "Provider Systems", "/api/v1/internal-providers", ["view", "create", "edit"]),
        new("UI-02", "systems", "الأنظمة والجهات", "Systems & Entities", "الجهات المستهلكة وتصنيفاتها", "Consumer Entities", "/api/v1/consumers", ["view", "manage"]),
        new("UI-GROUPS", "systems", "الأنظمة والجهات", "Systems & Entities", "مجموعات API", "API Groups", "/api/v1/api-groups", ["view", "manage"]),

        // 4. Settings & Governance Config
        new("UI-04", "settings", "الإعدادات والتهيئة", "Settings & Configuration", "أنواع الملفات", "File Types", "/api/v1/file-types", ["view", "manage"]),
        new("UI-23", "settings", "الإعدادات والتهيئة", "Settings & Configuration", "قوالب متطلبات الملفات", "Requirement Templates", "/api/v1/requirement-templates", ["view", "manage"]),
        new("UI-05", "settings", "الإعدادات والتهيئة", "Settings & Configuration", "مسارات الاعتماد ومراحلها", "Approval Routes", "/api/v1/approval-routes", ["view", "manage"]),
        new("UI-06", "settings", "الإعدادات والتهيئة", "Settings & Configuration", "أنواع التغيير وسياساتها", "Change Types & Policies", "/api/v1/change-types", ["view", "manage"]),

        // 5. API Documents & Document ACL
        new("UI-21", "documents", "وثائق API والتفويض", "API Documents & ACL", "وثائق API", "API Documents", "/api/v1/api-documents", ["view", "create", "edit", "delete", "assign"]),
        new("UI-21A", "documents", "وثائق API والتفويض", "API Documents & ACL", "صلاحيات كل وثيقة", "Document Access Control", "/api/v1/api-documents/{documentId}/access", ["view", "manage"]),

        // 6. Integrations, Versions, Files, Workflows & Publishing
        new("UI-22", "integrations", "التكاملات ودورة الاعتماد والنشر", "Integrations, Workflows & Publishing", "التكاملات", "Integrations", "/api/v1/integrations", ["view", "create", "assign"]),
        new("UI-12", "integrations", "التكاملات ودورة الاعتماد والنشر", "Integrations, Workflows & Publishing", "إصدارات API", "API Versions", "/api/v1/integrations/{integrationId}/versions", ["view", "create"]),
        new("UI-12-USAGE", "integrations", "التكاملات ودورة الاعتماد والنشر", "Integrations, Workflows & Publishing", "تعيين الإصدار المستخدم", "Consumer Version Usage", "/api/v1/integrations/{integrationId}/consumer-version", ["view", "manage"]),
        new("UI-10-REV", "integrations", "التكاملات ودورة الاعتماد والنشر", "Integrations, Workflows & Publishing", "مراجعات حزمة التوثيق", "Document Revisions", "/api/v1/versions/{versionId}/revisions", ["view", "create", "submit"]),
        new("UI-10-FILES", "integrations", "التكاملات ودورة الاعتماد والنشر", "Integrations, Workflows & Publishing", "ملفات المراجعة", "Revision Files", "/api/v1/revisions/{revisionId}/files", ["view", "create", "delete", "download"]),
        new("UI-13", "integrations", "التكاملات ودورة الاعتماد والنشر", "Integrations, Workflows & Publishing", "طلبات التغيير", "Change Requests", "/api/v1/change-requests", ["view", "create", "edit", "submit"]),
        new("UI-11", "integrations", "التكاملات ودورة الاعتماد والنشر", "Integrations, Workflows & Publishing", "المراجعات والاعتمادات", "Approval Workflows", "/api/v1/workflows", ["view", "decide"]),
        new("UI-14", "integrations", "التكاملات ودورة الاعتماد والنشر", "Integrations, Workflows & Publishing", "النشر وإيقاف النشر", "Publications", "/api/v1/publications", ["view", "create", "withdraw"])
    ];

    public static readonly IReadOnlyList<PermissionCatalogMetadataEntry> CanonicalEntries =
    [
        // Navigation & Monitoring
        new("dashboard.view", "dashboard", "view", "UI-17", "navigation", "view"),
        new("search.view", "search", "view", "UI-18", "navigation", "view"),

        // Administration & Identity
        new(UserPermissionCodes.UsersView, "users", "view", "UI-15", "administration", "view"),
        new(UserPermissionCodes.UsersCreate, "users", "create", "UI-15", "administration", "create"),
        new(UserPermissionCodes.UsersEdit, "users", "edit", "UI-15", "administration", "edit"),
        new(UserPermissionCodes.UsersAssignRoles, "users", "assign_roles", "UI-15", "administration", "assign"),
        new(UserPermissionCodes.UsersPasswordReset, "users", "password.reset", "UI-15", "administration", "reset_password"),

        new(UserPermissionCodes.RolesView, "roles", "view", "UI-25-ROLES", "administration", "view"),
        new(UserPermissionCodes.RolesCreate, "roles", "create", "UI-25-ROLES", "administration", "create"),
        new(UserPermissionCodes.RolesEdit, "roles", "edit", "UI-25-ROLES", "administration", "edit"),
        new(UserPermissionCodes.RolesDelete, "roles", "delete", "UI-25-ROLES", "administration", "delete"),

        new(UserPermissionCodes.PermissionsView, "permissions", "view", "UI-25-PERMS", "administration", "view"),
        new(UserPermissionCodes.PermissionsManage, "permissions", "manage", "UI-25-PERMS", "administration", "manage"),

        new("audit.view", "audit", "view", "UI-16", "administration", "view"),

        // Systems & Entities
        new("company_systems.view", "company_systems", "view", "UI-01", "systems", "view"),
        new("company_systems.create", "company_systems", "create", "UI-01", "systems", "create"),
        new("company_systems.edit", "company_systems", "edit", "UI-01", "systems", "edit"),

        new("providers.view", "providers", "view", "UI-03", "systems", "view"),
        new("providers.create", "providers", "create", "UI-03", "systems", "create"),
        new("providers.edit", "providers", "edit", "UI-03", "systems", "edit"),

        new("consumers.view", "consumers", "view", "UI-02", "systems", "view"),
        new("consumers.manage", "consumers", "manage", "UI-02", "systems", "manage"),

        new("api_groups.view", "api_groups", "view", "UI-GROUPS", "systems", "view"),
        new("api_groups.manage", "api_groups", "manage", "UI-GROUPS", "systems", "manage"),

        // Settings
        new("file_types.view", "file_types", "view", "UI-04", "settings", "view"),
        new("file_types.manage", "file_types", "manage", "UI-04", "settings", "manage"),

        new("requirements.view", "requirements", "view", "UI-23", "settings", "view"),
        new("requirements.manage", "requirements", "manage", "UI-23", "settings", "manage"),

        new("routes.view", "routes", "view", "UI-05", "settings", "view"),
        new("routes.manage", "routes", "manage", "UI-05", "settings", "manage"),

        new("changes.config.view", "changes.config", "view", "UI-06", "settings", "view"),
        new("changes.config.manage", "changes.config", "manage", "UI-06", "settings", "manage"),

        // API Documents & ACL
        new("api_documents.view", "api_documents", "view", "UI-21", "documents", "view"),
        new("api_documents.create", "api_documents", "create", "UI-21", "documents", "create"),
        new("api_documents.edit", "api_documents", "edit", "UI-21", "documents", "edit"),
        new("api_documents.delete", "api_documents", "delete", "UI-21", "documents", "delete"),
        new("api_documents.assign_developer", "api_documents", "assign_developer", "UI-21", "documents", "assign"),

        new("documents.permissions.view", "documents.permissions", "view", "UI-21A", "documents", "view"),
        new("documents.permissions.manage", "documents.permissions", "manage", "UI-21A", "documents", "manage"),

        // Integrations, Versions, Files, Workflows & Publishing
        new("integrations.view", "integrations", "view", "UI-22", "integrations", "view"),
        new("integrations.create", "integrations", "create", "UI-22", "integrations", "create"),
        new("integrations.assign_developer", "integrations", "assign_developer", "UI-22", "integrations", "assign"),

        new("versions.view", "versions", "view", "UI-12", "integrations", "view"),
        new("versions.create", "versions", "create", "UI-12", "integrations", "create"),

        new("versions.usage.view", "versions.usage", "view", "UI-12-USAGE", "integrations", "view"),
        new("versions.usage.manage", "versions.usage", "manage", "UI-12-USAGE", "integrations", "manage"),

        new("revisions.view", "revisions", "view", "UI-10-REV", "integrations", "view"),
        new("revisions.create", "revisions", "create", "UI-10-REV", "integrations", "create"),
        new("revisions.submit", "revisions", "submit", "UI-10-REV", "integrations", "submit"),

        new("files.view", "files", "view", "UI-10-FILES", "integrations", "view"),
        new("files.upload", "files", "upload", "UI-10-FILES", "integrations", "create"),
        new("files.delete", "files", "delete", "UI-10-FILES", "integrations", "delete"),
        new("files.download", "files", "download", "UI-10-FILES", "integrations", "download"),

        new("changes.view", "changes", "view", "UI-13", "integrations", "view"),
        new("changes.create", "changes", "create", "UI-13", "integrations", "create"),
        new("changes.edit", "changes", "edit", "UI-13", "integrations", "edit"),
        new("changes.submit", "changes", "submit", "UI-13", "integrations", "submit"),

        new("workflows.view", "workflows", "view", "UI-11", "integrations", "view"),
        new("workflows.decide", "workflows", "decide", "UI-11", "integrations", "decide"),

        new("publications.view", "publications", "view", "UI-14", "integrations", "view"),
        new("publications.create", "publications", "create", "UI-14", "integrations", "create"),
        new("publications.withdraw", "publications", "withdraw", "UI-14", "integrations", "withdraw")
    ];

    private static readonly Dictionary<string, PermissionCatalogMetadataEntry> ByCode =
        CanonicalEntries.ToDictionary(e => e.Code, StringComparer.OrdinalIgnoreCase);

    public static PermissionCatalogMetadataEntry ResolveMetadata(string code, string resource, string action)
    {
        if (ByCode.TryGetValue(code, out var entry))
        {
            return entry;
        }

        // Preserve any non-deleted legacy/extended permission row safely inside UI-16 / ungrouped metadata
        if (string.Equals(code, "audit_events.view", StringComparison.OrdinalIgnoreCase))
        {
            return new PermissionCatalogMetadataEntry(code, resource, action, "UI-16", "administration", "view");
        }

        return new PermissionCatalogMetadataEntry(code, resource, action, "UI-25-PERMS", "administration", "view");
    }
}

