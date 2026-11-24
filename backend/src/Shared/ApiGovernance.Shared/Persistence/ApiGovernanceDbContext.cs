using ApiGovernance.Shared.Persistence.Configurations;
using ApiGovernance.Shared.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace ApiGovernance.Shared.Persistence;

/// <summary>
/// Contract allowing each isolated module in the Modular Monolith to register its own
/// supplementary EF Core configurations without breaking module boundaries.
/// </summary>
public interface IModuleModelConfiguration
{
    string ModuleCode { get; }

    void ConfigureModel(ModelBuilder modelBuilder);
}

/// <summary>
/// Unified EF Core 10 DbContext for the API Governance Modular Monolith (Tasks F04 & F05).
/// Exposes all 43 approved domain tables and enforces OP-01 append-only immutability in code.
/// </summary>
public sealed class ApiGovernanceDbContext : DbContext
{
    private readonly IEnumerable<IModuleModelConfiguration> _moduleConfigurations;

    public ApiGovernanceDbContext(
        DbContextOptions<ApiGovernanceDbContext> options,
        IEnumerable<IModuleModelConfiguration>? moduleConfigurations = null)
        : base(options)
    {
        _moduleConfigurations = moduleConfigurations ?? [];
    }

    // Group 01 • Identity & Access (6 tables)
    public DbSet<UserEntity> Users => Set<UserEntity>();
    public DbSet<RoleEntity> Roles => Set<RoleEntity>();
    public DbSet<UserRoleEntity> UserRoles => Set<UserRoleEntity>();
    public DbSet<PermissionEntity> Permissions => Set<PermissionEntity>();
    public DbSet<RolePermissionEntity> RolePermissions => Set<RolePermissionEntity>();
    public DbSet<UserPermissionOverrideEntity> UserPermissionOverrides => Set<UserPermissionOverrideEntity>();

    // Group 02 • Master Data & Consumers (6 tables)
    public DbSet<CompanySystemEntity> CompanySystems => Set<CompanySystemEntity>();
    public DbSet<InternalProviderSystemEntity> InternalProviderSystems => Set<InternalProviderSystemEntity>();
    public DbSet<ExternalProviderSystemEntity> ExternalProviderSystems => Set<ExternalProviderSystemEntity>();
    public DbSet<ConsumerCategoryEntity> ConsumerCategories => Set<ConsumerCategoryEntity>();
    public DbSet<ConsumerEntityRecord> ConsumerEntities => Set<ConsumerEntityRecord>();
    public DbSet<ApiGroupEntity> ApiGroups => Set<ApiGroupEntity>();

    // Group 03 • File Templates & Governance Routes (9 tables)
    public DbSet<FileTypeEntity> FileTypes => Set<FileTypeEntity>();
    public DbSet<FileTypeExtensionEntity> FileTypeExtensions => Set<FileTypeExtensionEntity>();
    public DbSet<FileRequirementTemplateEntity> FileRequirementTemplates => Set<FileRequirementTemplateEntity>();
    public DbSet<FileRequirementRuleEntity> FileRequirementRules => Set<FileRequirementRuleEntity>();
    public DbSet<ApprovalRouteEntity> ApprovalRoutes => Set<ApprovalRouteEntity>();
    public DbSet<ApprovalRouteStageEntity> ApprovalRouteStages => Set<ApprovalRouteStageEntity>();
    public DbSet<ChangeTypeEntity> ChangeTypes => Set<ChangeTypeEntity>();
    public DbSet<ChangePolicyEntity> ChangePolicies => Set<ChangePolicyEntity>();
    public DbSet<ChangePolicyTypeEntity> ChangePolicyTypes => Set<ChangePolicyTypeEntity>();

    // Group 04 • API Documents, Integrations & Access (6 tables)
    public DbSet<ApiDocumentEntity> ApiDocuments => Set<ApiDocumentEntity>();
    public DbSet<DocumentDeveloperEntity> DocumentDevelopers => Set<DocumentDeveloperEntity>();
    public DbSet<DocumentRolePermissionEntity> DocumentRolePermissions => Set<DocumentRolePermissionEntity>();
    public DbSet<DocumentUserPermissionEntity> DocumentUserPermissions => Set<DocumentUserPermissionEntity>();
    public DbSet<IntegrationEntity> Integrations => Set<IntegrationEntity>();
    public DbSet<IntegrationDeveloperEntity> IntegrationDevelopers => Set<IntegrationDeveloperEntity>();

    // Group 05 • Versions, Revisions, Files & Change Requests (7 tables)
    public DbSet<ApiVersionEntity> ApiVersions => Set<ApiVersionEntity>();
    public DbSet<DocumentRevisionEntity> DocumentRevisions => Set<DocumentRevisionEntity>();
    public DbSet<RevisionFileRequirementEntity> RevisionFileRequirements => Set<RevisionFileRequirementEntity>();
    public DbSet<FileObjectEntity> FileObjects => Set<FileObjectEntity>();
    public DbSet<RevisionFileEntity> RevisionFiles => Set<RevisionFileEntity>();
    public DbSet<ChangeRequestEntity> ChangeRequests => Set<ChangeRequestEntity>();
    public DbSet<ChangeRequestFileEntity> ChangeRequestFiles => Set<ChangeRequestFileEntity>();

    // Group 06 • Workflow Decisions (3 tables)
    public DbSet<WorkflowRunEntity> WorkflowRuns => Set<WorkflowRunEntity>();
    public DbSet<WorkflowRunStageEntity> WorkflowRunStages => Set<WorkflowRunStageEntity>();
    public DbSet<WorkflowDecisionEntity> WorkflowDecisions => Set<WorkflowDecisionEntity>();

    // Group 07 • Publishing & Consumer Usage (3 tables)
    public DbSet<PublicationEntity> Publications => Set<PublicationEntity>();
    public DbSet<PublicationFileEntity> PublicationFiles => Set<PublicationFileEntity>();
    public DbSet<ConsumerVersionUsageEntity> ConsumerVersionUsage => Set<ConsumerVersionUsageEntity>();

    // Group 08 • Notifications, Release History & Append-Only Audit (3 tables)
    public DbSet<NotificationEntity> Notifications => Set<NotificationEntity>();
    public DbSet<AuditEventEntity> AuditEvents => Set<AuditEventEntity>();
    public DbSet<ReleaseHistoryEventEntity> ReleaseHistoryEvents => Set<ReleaseHistoryEventEntity>();

    // Infrastructure Identity Session Ticket Store (Task I01)
    public DbSet<AuthSessionEntity> AuthSessions => Set<AuthSessionEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        BaselineSchemaConfiguration.ConfigureAll(modelBuilder);

        foreach (var moduleConfig in _moduleConfigurations)
        {
            moduleConfig.ConfigureModel(modelBuilder);
        }
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnforceAppendOnlyRules();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        EnforceAppendOnlyRules();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void EnforceAppendOnlyRules()
    {
        foreach (var entry in ChangeTracker.Entries<IAppendOnlyEntity>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                var tableName = entry.Metadata.GetTableName() ?? entry.Entity.GetType().Name;
                throw new InvalidOperationException(
                    $"Append-only table '{tableName}' cannot be modified or deleted once recorded (OP-01).");
            }
        }
    }
}
