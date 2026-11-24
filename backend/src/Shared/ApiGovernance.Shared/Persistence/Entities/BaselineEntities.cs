namespace ApiGovernance.Shared.Persistence.Entities;

/// <summary>
/// Marker interface for append-only tables (OP-01: audit_events and release_history_events)
/// that must never be updated or deleted once inserted.
/// </summary>
public interface IAppendOnlyEntity
{
    Guid Id { get; }
}

// ============================================================================
// Group 01 • الهوية والصلاحيات (6 Tables: users, roles, user_roles, permissions, role_permissions, user_permission_overrides)
// Coordinated with ASP.NET Core Identity per 08_DB_and_plan_delta.md & 04_Authentication.md to avoid duplicate user tables in I01.
// ============================================================================

public sealed class UserEntity
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string NormalizedEmail { get; set; } = string.Empty;
    public string? PasswordHash { get; set; }
    public string SecurityStamp { get; set; } = string.Empty;
    public string ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString("N");
    public bool LockoutEnabled { get; set; } = true;
    public DateTimeOffset? LockoutEnd { get; set; }
    public int AccessFailedCount { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid? ConsumerEntityId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class RoleEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public bool IsPinned { get; set; }
}

public sealed class UserRoleEntity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid RoleId { get; set; }
}

public sealed class PermissionEntity
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Resource { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
}

public sealed class RolePermissionEntity
{
    public Guid Id { get; set; }
    public Guid RoleId { get; set; }
    public Guid PermissionId { get; set; }
}

public sealed class UserPermissionOverrideEntity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid PermissionId { get; set; }
    public string Decision { get; set; } = string.Empty; // GRANT | REVOKE
}

// ============================================================================
// Group 02 • الأنظمة والجهات (6 Tables: company_systems, internal_provider_systems, external_provider_systems, consumer_categories, consumer_entities, api_groups)
// ============================================================================

public sealed class CompanySystemEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class InternalProviderSystemEntity
{
    public Guid Id { get; set; }
    public Guid CompanySystemId { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class ExternalProviderSystemEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class ConsumerCategoryEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public sealed class ConsumerEntityRecord
{
    public Guid Id { get; set; }
    public Guid CategoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public sealed class ApiGroupEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
}

// ============================================================================
// Group 03 • قوالب الملفات ومسارات الحوكمة (9 Tables: file_types, file_type_extensions, file_requirement_templates, file_requirement_rules, approval_routes, approval_route_stages, change_types, change_policies, change_policy_types)
// ============================================================================

public sealed class FileTypeEntity
{
    public Guid Id { get; set; }
    public string CatalogCode { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public long MaxSizeBytes { get; set; }
    public Guid? WordTemplateObjectId { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class FileTypeExtensionEntity
{
    public Guid Id { get; set; }
    public Guid FileTypeId { get; set; }
    public string Extension { get; set; } = string.Empty;
}

public sealed class FileRequirementTemplateEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public sealed class FileRequirementRuleEntity
{
    public Guid Id { get; set; }
    public Guid TemplateId { get; set; }
    public Guid FileTypeId { get; set; }
    public string RequirementMode { get; set; } = string.Empty;
    public int MinCount { get; set; }
    public int MaxCount { get; set; }
    public string? ConditionSpecJson { get; set; }
}

public sealed class ApprovalRouteEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public sealed class ApprovalRouteStageEntity
{
    public Guid Id { get; set; }
    public Guid RouteId { get; set; }
    public int Position { get; set; }
    public string StageName { get; set; } = string.Empty;
    public Guid ResponsibleRoleId { get; set; }
}

public sealed class ChangeTypeEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid RouteId { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class ChangePolicyEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public sealed class ChangePolicyTypeEntity
{
    public Guid Id { get; set; }
    public Guid PolicyId { get; set; }
    public Guid ChangeTypeId { get; set; }
}

// ============================================================================
// Group 04 • وثائق API والتكاملات والوصول (6 Tables: api_documents, document_developers, document_role_permissions, document_user_permissions, integrations, integration_developers)
// ============================================================================

public sealed class ApiDocumentEntity
{
    public Guid Id { get; set; }
    public Guid? ProviderInternalSystemId { get; set; }
    public Guid? ProviderExternalSystemId { get; set; }
    public Guid? ConsumerCompanySystemId { get; set; }
    public Guid? ConsumerEntityId { get; set; }
    public Guid ApiGroupId { get; set; }
    public Guid RequirementTemplateId { get; set; }
    public Guid ApprovalRouteId { get; set; }
    public Guid ChangePolicyId { get; set; }
    public string PublicationOption { get; set; } = string.Empty;
    public Guid CreatedBy { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class DocumentDeveloperEntity
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public Guid UserId { get; set; }
}

public sealed class DocumentRolePermissionEntity
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public Guid RoleId { get; set; }
    public Guid PermissionId { get; set; }
    public string Decision { get; set; } = string.Empty; // GRANT | REVOKE
}

public sealed class DocumentUserPermissionEntity
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public Guid UserId { get; set; }
    public Guid PermissionId { get; set; }
    public string Decision { get; set; } = string.Empty; // GRANT | REVOKE
}

public sealed class IntegrationEntity
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public string ReferenceCode { get; set; } = string.Empty;
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class IntegrationDeveloperEntity
{
    public Guid Id { get; set; }
    public Guid IntegrationId { get; set; }
    public Guid UserId { get; set; }
}

// ============================================================================
// Group 05 • الإصدارات ومراجعات الملفات والتغيير (7 Tables: api_versions, document_revisions, revision_file_requirements, file_objects, revision_files, change_requests, change_request_files)
// ============================================================================

public sealed class ApiVersionEntity
{
    public Guid Id { get; set; }
    public Guid IntegrationId { get; set; }
    public string VersionLabel { get; set; } = string.Empty;
    public bool IsArchived { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class DocumentRevisionEntity
{
    public Guid Id { get; set; }
    public Guid ApiVersionId { get; set; }
    public int RevisionNumber { get; set; }
    public Guid? BaseRevisionId { get; set; }
    public Guid? ChangeRequestId { get; set; }
    public string Status { get; set; } = string.Empty;
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
}

public sealed class RevisionFileRequirementEntity
{
    public Guid Id { get; set; }
    public Guid RevisionId { get; set; }
    public Guid FileTypeId { get; set; }
    public string RequirementMode { get; set; } = string.Empty;
    public int MinCount { get; set; }
    public int MaxCount { get; set; }
}

public sealed class FileObjectEntity
{
    public Guid Id { get; set; }
    public string StorageKey { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string? MediaType { get; set; }
    public string ScanStatus { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class RevisionFileEntity
{
    public Guid Id { get; set; }
    public Guid RevisionId { get; set; }
    public Guid FileTypeId { get; set; }
    public Guid ObjectId { get; set; }
    public string OriginalName { get; set; } = string.Empty;
    public Guid? SourceRevisionFileId { get; set; }
    public Guid UploadedBy { get; set; }
    public DateTimeOffset UploadedAt { get; set; }
}

public sealed class ChangeRequestEntity
{
    public Guid Id { get; set; }
    public Guid IntegrationId { get; set; }
    public Guid SourceRevisionId { get; set; }
    public Guid ChangeTypeId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? ConsumerImpact { get; set; }
    public string Status { get; set; } = string.Empty;
    public Guid RequestedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ChangeRequestFileEntity
{
    public Guid Id { get; set; }
    public Guid ChangeRequestId { get; set; }
    public Guid SourceRevisionFileId { get; set; }
}

// ============================================================================
// Group 06 • قرارات سير العمل (3 Tables: workflow_runs, workflow_run_stages, workflow_decisions)
// ============================================================================

public sealed class WorkflowRunEntity
{
    public Guid Id { get; set; }
    public Guid RouteId { get; set; }
    public Guid? RevisionId { get; set; }
    public Guid? ChangeRequestId { get; set; }
    public string Status { get; set; } = string.Empty;
    public Guid SubmittedBy { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
}

public sealed class WorkflowRunStageEntity
{
    public Guid Id { get; set; }
    public Guid RunId { get; set; }
    public int Position { get; set; }
    public string StageNameSnapshot { get; set; } = string.Empty;
    public Guid ResponsibleRoleIdSnapshot { get; set; }
    public string Status { get; set; } = string.Empty;
}

public sealed class WorkflowDecisionEntity
{
    public Guid Id { get; set; }
    public Guid RunStageId { get; set; }
    public Guid ActorUserId { get; set; }
    public string Decision { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public DateTimeOffset DecidedAt { get; set; }
}

// ============================================================================
// Group 07 • النشر والإصدار المستخدم (3 Tables: publications, publication_files, consumer_version_usage)
// ============================================================================

public sealed class PublicationEntity
{
    public Guid Id { get; set; }
    public Guid RevisionId { get; set; }
    public string State { get; set; } = string.Empty;
    public Guid PublishedBy { get; set; }
    public DateTimeOffset PublishedAt { get; set; }
    public DateTimeOffset? WithdrawnAt { get; set; }
}

public sealed class PublicationFileEntity
{
    public Guid Id { get; set; }
    public Guid PublicationId { get; set; }
    public Guid RevisionFileId { get; set; }
}

public sealed class ConsumerVersionUsageEntity
{
    public Guid Id { get; set; }
    public Guid IntegrationId { get; set; }
    public Guid ApiVersionId { get; set; }
    public DateTimeOffset ValidFrom { get; set; }
    public DateTimeOffset? ValidTo { get; set; }
    public Guid AssignedBy { get; set; }
}

// ============================================================================
// Group 08 • الإشعارات والتاريخ والتدقيق الإلحاقي (3 Tables: notifications, audit_events, release_history_events)
// ============================================================================

public sealed class NotificationEntity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string ResourceKind { get; set; } = string.Empty;
    public Guid ResourceId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
}

public sealed class AuditEventEntity : IAppendOnlyEntity
{
    public Guid Id { get; set; }
    public Guid? ActorUserId { get; set; }
    public string Operation { get; set; } = string.Empty;
    public string ResourceKind { get; set; } = string.Empty;
    public Guid? ResourceId { get; set; }
    public string Result { get; set; } = string.Empty;
    public string? ChangeSummaryJson { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
}

public sealed class ReleaseHistoryEventEntity : IAppendOnlyEntity
{
    public Guid Id { get; set; }
    public Guid RevisionId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public Guid ActorUserId { get; set; }
    public Guid? ChangeRequestId { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
}

