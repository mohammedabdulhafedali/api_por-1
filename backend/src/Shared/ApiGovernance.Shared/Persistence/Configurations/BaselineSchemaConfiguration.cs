using ApiGovernance.Shared.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace ApiGovernance.Shared.Persistence.Configurations;

/// <summary>
/// Configures all 43 baseline tables, 77 foreign keys (with DeleteBehavior.Restrict),
/// unique constraints, conditional unique indexes (OP-05, DB-15), and CHECK constraints
/// per Database_v0.1.dbml, 08_DB_and_plan_delta.md, and docs/decisions/G3_F01.md.
/// </summary>
public static class BaselineSchemaConfiguration
{
    public static void ConfigureAll(ModelBuilder modelBuilder)
    {
        ConfigureGroup01IdentityAndAccess(modelBuilder);
        ConfigureGroup02SystemsAndConsumers(modelBuilder);
        ConfigureGroup03TemplatesAndRoutes(modelBuilder);
        ConfigureGroup04DocumentsAndIntegrations(modelBuilder);
        ConfigureGroup05VersionsFilesAndChanges(modelBuilder);
        ConfigureGroup06WorkflowDecisions(modelBuilder);
        ConfigureGroup07PublishingAndUsage(modelBuilder);
        ConfigureGroup08NotificationsAndAppendOnlyAudit(modelBuilder);
        ConfigureIdentitySessionTicketStore(modelBuilder);
    }

    private static void ConfigureGroup01IdentityAndAccess(ModelBuilder modelBuilder)
    {
        // 1. users (integrated with ASP.NET Core Identity credential & lockout fields per 08_DB_and_plan_delta.md)
        modelBuilder.Entity<UserEntity>(b =>
        {
            b.ToTable("users", t =>
            {
                t.HasCheckConstraint("ck_users_access_failed_count_non_negative", "access_failed_count >= 0");
            });
            b.HasKey(x => x.Id).HasName("pk_users");
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.FullName).HasColumnName("full_name").HasMaxLength(200).IsRequired();
            b.Property(x => x.Email).HasColumnName("email").HasMaxLength(320).IsRequired();
            b.Property(x => x.NormalizedEmail).HasColumnName("normalized_email").HasMaxLength(320).IsRequired();
            b.Property(x => x.PasswordHash).HasColumnName("password_hash").HasMaxLength(512);
            b.Property(x => x.SecurityStamp).HasColumnName("security_stamp").HasMaxLength(128).IsRequired();
            b.Property(x => x.ConcurrencyStamp).HasColumnName("concurrency_stamp").HasMaxLength(128).IsRequired().IsConcurrencyToken();
            b.Property(x => x.LockoutEnabled).HasColumnName("lockout_enabled").IsRequired();
            b.Property(x => x.LockoutEnd).HasColumnName("lockout_end");
            b.Property(x => x.AccessFailedCount).HasColumnName("access_failed_count").IsRequired();
            b.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
            b.Property(x => x.ConsumerEntityId).HasColumnName("consumer_entity_id");
            b.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();

            b.HasIndex(x => x.Email).IsUnique().HasDatabaseName("ux_users_email");
            b.HasIndex(x => x.NormalizedEmail).IsUnique().HasDatabaseName("ux_users_normalized_email");
            b.HasIndex(x => x.ConsumerEntityId).HasDatabaseName("ix_users_consumer_entity_id");

            b.HasOne<ConsumerEntityRecord>()
                .WithMany()
                .HasForeignKey(x => x.ConsumerEntityId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_users_consumer_entities_consumer_entity_id");
        });

        // 2. roles
        modelBuilder.Entity<RoleEntity>(b =>
        {
            b.ToTable("roles");
            b.HasKey(x => x.Id).HasName("pk_roles");
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.Name).HasColumnName("name").HasMaxLength(120).IsRequired();
            b.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
            b.Property(x => x.IsPinned).HasColumnName("is_pinned").IsRequired();

            b.HasIndex(x => x.Name).IsUnique().HasDatabaseName("ux_roles_name");
        });

        // 3. user_roles
        modelBuilder.Entity<UserRoleEntity>(b =>
        {
            b.ToTable("user_roles");
            b.HasKey(x => x.Id).HasName("pk_user_roles");
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
            b.Property(x => x.RoleId).HasColumnName("role_id").IsRequired();

            b.HasIndex(x => new { x.UserId, x.RoleId }).IsUnique().HasDatabaseName("ux_user_roles_user_id_role_id");
            b.HasIndex(x => x.RoleId).HasDatabaseName("ix_user_roles_role_id");

            b.HasOne<UserEntity>()
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_user_roles_users_user_id");

            b.HasOne<RoleEntity>()
                .WithMany()
                .HasForeignKey(x => x.RoleId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_user_roles_roles_role_id");
        });

        // 4. permissions
        modelBuilder.Entity<PermissionEntity>(b =>
        {
            b.ToTable("permissions");
            b.HasKey(x => x.Id).HasName("pk_permissions");
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.Code).HasColumnName("code").HasMaxLength(160).IsRequired();
            b.Property(x => x.Resource).HasColumnName("resource").HasMaxLength(80).IsRequired();
            b.Property(x => x.Action).HasColumnName("action").HasMaxLength(80).IsRequired();

            b.HasIndex(x => x.Code).IsUnique().HasDatabaseName("ux_permissions_code");
        });

        // 5. role_permissions
        modelBuilder.Entity<RolePermissionEntity>(b =>
        {
            b.ToTable("role_permissions");
            b.HasKey(x => x.Id).HasName("pk_role_permissions");
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.RoleId).HasColumnName("role_id").IsRequired();
            b.Property(x => x.PermissionId).HasColumnName("permission_id").IsRequired();

            b.HasIndex(x => new { x.RoleId, x.PermissionId }).IsUnique().HasDatabaseName("ux_role_permissions_role_id_permission_id");
            b.HasIndex(x => x.PermissionId).HasDatabaseName("ix_role_permissions_permission_id");

            b.HasOne<RoleEntity>()
                .WithMany()
                .HasForeignKey(x => x.RoleId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_role_permissions_roles_role_id");

            b.HasOne<PermissionEntity>()
                .WithMany()
                .HasForeignKey(x => x.PermissionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_role_permissions_permissions_permission_id");
        });

        // 6. user_permission_overrides
        modelBuilder.Entity<UserPermissionOverrideEntity>(b =>
        {
            b.ToTable("user_permission_overrides", t =>
            {
                t.HasCheckConstraint("ck_user_permission_overrides_decision", "decision IN ('GRANT', 'REVOKE')");
            });
            b.HasKey(x => x.Id).HasName("pk_user_permission_overrides");
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
            b.Property(x => x.PermissionId).HasColumnName("permission_id").IsRequired();
            b.Property(x => x.Decision).HasColumnName("decision").HasMaxLength(10).IsRequired();

            b.HasIndex(x => new { x.UserId, x.PermissionId }).IsUnique().HasDatabaseName("ux_user_permission_overrides_user_id_permission_id");
            b.HasIndex(x => x.PermissionId).HasDatabaseName("ix_user_permission_overrides_permission_id");

            b.HasOne<UserEntity>()
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_user_permission_overrides_users_user_id");

            b.HasOne<PermissionEntity>()
                .WithMany()
                .HasForeignKey(x => x.PermissionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_user_permission_overrides_permissions_permission_id");
        });
    }

    private static void ConfigureGroup02SystemsAndConsumers(ModelBuilder modelBuilder)
    {
        // 7. company_systems
        modelBuilder.Entity<CompanySystemEntity>(b =>
        {
            b.ToTable("company_systems");
            b.HasKey(x => x.Id).HasName("pk_company_systems");
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
            b.Property(x => x.Description).HasColumnName("description");
            b.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();

            b.HasIndex(x => x.Name).IsUnique().HasDatabaseName("ux_company_systems_name");
        });

        // 8. internal_provider_systems
        modelBuilder.Entity<InternalProviderSystemEntity>(b =>
        {
            b.ToTable("internal_provider_systems");
            b.HasKey(x => x.Id).HasName("pk_internal_provider_systems");
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.CompanySystemId).HasColumnName("company_system_id").IsRequired();
            b.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();

            b.HasIndex(x => x.CompanySystemId).IsUnique().HasDatabaseName("ux_internal_provider_systems_company_system_id");

            b.HasOne<CompanySystemEntity>()
                .WithMany()
                .HasForeignKey(x => x.CompanySystemId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_internal_provider_systems_company_systems_company_system_id");
        });

        // 9. external_provider_systems
        modelBuilder.Entity<ExternalProviderSystemEntity>(b =>
        {
            b.ToTable("external_provider_systems");
            b.HasKey(x => x.Id).HasName("pk_external_provider_systems");
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
            b.Property(x => x.Description).HasColumnName("description");
            b.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();

            b.HasIndex(x => x.Name).IsUnique().HasDatabaseName("ux_external_provider_systems_name");
        });

        // 10. consumer_categories
        modelBuilder.Entity<ConsumerCategoryEntity>(b =>
        {
            b.ToTable("consumer_categories");
            b.HasKey(x => x.Id).HasName("pk_consumer_categories");
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.Name).HasColumnName("name").HasMaxLength(160).IsRequired();
            b.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();

            b.HasIndex(x => x.Name).IsUnique().HasDatabaseName("ux_consumer_categories_name");
        });

        // 11. consumer_entities
        modelBuilder.Entity<ConsumerEntityRecord>(b =>
        {
            b.ToTable("consumer_entities");
            b.HasKey(x => x.Id).HasName("pk_consumer_entities");
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.CategoryId).HasColumnName("category_id").IsRequired();
            b.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
            b.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();

            b.HasIndex(x => x.Name).IsUnique().HasDatabaseName("ux_consumer_entities_name");
            b.HasIndex(x => x.CategoryId).HasDatabaseName("ix_consumer_entities_category_id");

            b.HasOne<ConsumerCategoryEntity>()
                .WithMany()
                .HasForeignKey(x => x.CategoryId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_consumer_entities_consumer_categories_category_id");
        });

        // 12. api_groups
        modelBuilder.Entity<ApiGroupEntity>(b =>
        {
            b.ToTable("api_groups");
            b.HasKey(x => x.Id).HasName("pk_api_groups");
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.Name).HasColumnName("name").HasMaxLength(180).IsRequired();
            b.Property(x => x.Description).HasColumnName("description");
            b.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();

            b.HasIndex(x => x.Name).IsUnique().HasDatabaseName("ux_api_groups_name");
        });
    }

    private static void ConfigureGroup03TemplatesAndRoutes(ModelBuilder modelBuilder)
    {
        // 13. file_types
        modelBuilder.Entity<FileTypeEntity>(b =>
        {
            b.ToTable("file_types", t =>
            {
                t.HasCheckConstraint("ck_file_types_max_size_bytes_positive", "max_size_bytes > 0");
            });
            b.HasKey(x => x.Id).HasName("pk_file_types");
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.CatalogCode).HasColumnName("catalog_code").HasMaxLength(80).IsRequired();
            b.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(140).IsRequired();
            b.Property(x => x.MaxSizeBytes).HasColumnName("max_size_bytes").IsRequired();
            b.Property(x => x.WordTemplateObjectId).HasColumnName("word_template_object_id");
            b.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();

            b.HasIndex(x => x.CatalogCode).IsUnique().HasDatabaseName("ux_file_types_catalog_code");
            b.HasIndex(x => x.WordTemplateObjectId).HasDatabaseName("ix_file_types_word_template_object_id");

            b.HasOne<FileObjectEntity>()
                .WithMany()
                .HasForeignKey(x => x.WordTemplateObjectId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_file_types_file_objects_word_template_object_id");
        });

        // 14. file_type_extensions
        modelBuilder.Entity<FileTypeExtensionEntity>(b =>
        {
            b.ToTable("file_type_extensions");
            b.HasKey(x => x.Id).HasName("pk_file_type_extensions");
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.FileTypeId).HasColumnName("file_type_id").IsRequired();
            b.Property(x => x.Extension).HasColumnName("extension").HasMaxLength(24).IsRequired();

            b.HasIndex(x => new { x.FileTypeId, x.Extension }).IsUnique().HasDatabaseName("ux_file_type_extensions_file_type_id_extension");

            b.HasOne<FileTypeEntity>()
                .WithMany()
                .HasForeignKey(x => x.FileTypeId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_file_type_extensions_file_types_file_type_id");
        });

        // 15. file_requirement_templates
        modelBuilder.Entity<FileRequirementTemplateEntity>(b =>
        {
            b.ToTable("file_requirement_templates");
            b.HasKey(x => x.Id).HasName("pk_file_requirement_templates");
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
            b.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();

            b.HasIndex(x => x.Name).IsUnique().HasDatabaseName("ux_file_requirement_templates_name");
        });

        // 16. file_requirement_rules
        modelBuilder.Entity<FileRequirementRuleEntity>(b =>
        {
            b.ToTable("file_requirement_rules", t =>
            {
                t.HasCheckConstraint("ck_file_requirement_rules_counts", "min_count >= 0 AND max_count >= min_count");
            });
            b.HasKey(x => x.Id).HasName("pk_file_requirement_rules");
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.TemplateId).HasColumnName("template_id").IsRequired();
            b.Property(x => x.FileTypeId).HasColumnName("file_type_id").IsRequired();
            b.Property(x => x.RequirementMode).HasColumnName("requirement_mode").HasMaxLength(16).IsRequired();
            b.Property(x => x.MinCount).HasColumnName("min_count").IsRequired();
            b.Property(x => x.MaxCount).HasColumnName("max_count").IsRequired();
            b.Property(x => x.ConditionSpecJson).HasColumnName("condition_spec_json").HasColumnType("jsonb");

            b.HasIndex(x => new { x.TemplateId, x.FileTypeId }).IsUnique().HasDatabaseName("ux_file_requirement_rules_template_id_file_type_id");
            b.HasIndex(x => x.FileTypeId).HasDatabaseName("ix_file_requirement_rules_file_type_id");

            b.HasOne<FileRequirementTemplateEntity>()
                .WithMany()
                .HasForeignKey(x => x.TemplateId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_file_requirement_rules_templates_template_id");

            b.HasOne<FileTypeEntity>()
                .WithMany()
                .HasForeignKey(x => x.FileTypeId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_file_requirement_rules_file_types_file_type_id");
        });

        // 17. approval_routes
        modelBuilder.Entity<ApprovalRouteEntity>(b =>
        {
            b.ToTable("approval_routes");
            b.HasKey(x => x.Id).HasName("pk_approval_routes");
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
            b.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();

            b.HasIndex(x => x.Name).IsUnique().HasDatabaseName("ux_approval_routes_name");
        });

        // 18. approval_route_stages (DB-07: stage_name + position + responsible_role_id ONLY; no stage_type or assigned user)
        modelBuilder.Entity<ApprovalRouteStageEntity>(b =>
        {
            b.ToTable("approval_route_stages", t =>
            {
                t.HasCheckConstraint("ck_approval_route_stages_position_positive", "position >= 1");
            });
            b.HasKey(x => x.Id).HasName("pk_approval_route_stages");
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.RouteId).HasColumnName("route_id").IsRequired();
            b.Property(x => x.Position).HasColumnName("position").IsRequired();
            b.Property(x => x.StageName).HasColumnName("stage_name").HasMaxLength(160).IsRequired();
            b.Property(x => x.ResponsibleRoleId).HasColumnName("responsible_role_id").IsRequired();

            b.HasIndex(x => new { x.RouteId, x.Position }).IsUnique().HasDatabaseName("ux_approval_route_stages_route_id_position");
            b.HasIndex(x => x.ResponsibleRoleId).HasDatabaseName("ix_approval_route_stages_responsible_role_id");

            b.HasOne<ApprovalRouteEntity>()
                .WithMany()
                .HasForeignKey(x => x.RouteId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_approval_route_stages_approval_routes_route_id");

            b.HasOne<RoleEntity>()
                .WithMany()
                .HasForeignKey(x => x.ResponsibleRoleId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_approval_route_stages_roles_responsible_role_id");
        });

        // 19. change_types
        modelBuilder.Entity<ChangeTypeEntity>(b =>
        {
            b.ToTable("change_types");
            b.HasKey(x => x.Id).HasName("pk_change_types");
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.Name).HasColumnName("name").HasMaxLength(160).IsRequired();
            b.Property(x => x.RouteId).HasColumnName("route_id").IsRequired();
            b.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();

            b.HasIndex(x => x.Name).IsUnique().HasDatabaseName("ux_change_types_name");
            b.HasIndex(x => x.RouteId).HasDatabaseName("ix_change_types_route_id");

            b.HasOne<ApprovalRouteEntity>()
                .WithMany()
                .HasForeignKey(x => x.RouteId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_change_types_approval_routes_route_id");
        });

        // 20. change_policies
        modelBuilder.Entity<ChangePolicyEntity>(b =>
        {
            b.ToTable("change_policies");
            b.HasKey(x => x.Id).HasName("pk_change_policies");
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.Name).HasColumnName("name").HasMaxLength(160).IsRequired();
            b.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();

            b.HasIndex(x => x.Name).IsUnique().HasDatabaseName("ux_change_policies_name");
        });

        // 21. change_policy_types
        modelBuilder.Entity<ChangePolicyTypeEntity>(b =>
        {
            b.ToTable("change_policy_types");
            b.HasKey(x => x.Id).HasName("pk_change_policy_types");
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.PolicyId).HasColumnName("policy_id").IsRequired();
            b.Property(x => x.ChangeTypeId).HasColumnName("change_type_id").IsRequired();

            b.HasIndex(x => new { x.PolicyId, x.ChangeTypeId }).IsUnique().HasDatabaseName("ux_change_policy_types_policy_id_change_type_id");
            b.HasIndex(x => x.ChangeTypeId).HasDatabaseName("ix_change_policy_types_change_type_id");

            b.HasOne<ChangePolicyEntity>()
                .WithMany()
                .HasForeignKey(x => x.PolicyId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_change_policy_types_change_policies_policy_id");

            b.HasOne<ChangeTypeEntity>()
                .WithMany()
                .HasForeignKey(x => x.ChangeTypeId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_change_policy_types_change_types_change_type_id");
        });
    }

    private static void ConfigureGroup04DocumentsAndIntegrations(ModelBuilder modelBuilder)
    {
        // 22. api_documents (DB-01 XOR provider/consumer, DB-02 internal provider != internal consumer, OP-05 unique per provider+consumer+api_group)
        modelBuilder.Entity<ApiDocumentEntity>(b =>
        {
            b.ToTable("api_documents", t =>
            {
                t.HasCheckConstraint(
                    "ck_api_documents_provider_xor",
                    "(provider_internal_system_id IS NOT NULL AND provider_external_system_id IS NULL) OR (provider_internal_system_id IS NULL AND provider_external_system_id IS NOT NULL)");

                t.HasCheckConstraint(
                    "ck_api_documents_consumer_xor",
                    "(consumer_company_system_id IS NOT NULL AND consumer_entity_id IS NULL) OR (consumer_company_system_id IS NULL AND consumer_entity_id IS NOT NULL)");

                t.HasCheckConstraint(
                    "ck_api_documents_internal_parties_distinct",
                    "provider_internal_system_id IS NULL OR consumer_company_system_id IS NULL OR provider_internal_system_id <> consumer_company_system_id");
            });

            b.HasKey(x => x.Id).HasName("pk_api_documents");
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.ProviderInternalSystemId).HasColumnName("provider_internal_system_id");
            b.Property(x => x.ProviderExternalSystemId).HasColumnName("provider_external_system_id");
            b.Property(x => x.ConsumerCompanySystemId).HasColumnName("consumer_company_system_id");
            b.Property(x => x.ConsumerEntityId).HasColumnName("consumer_entity_id");
            b.Property(x => x.ApiGroupId).HasColumnName("api_group_id").IsRequired();
            b.Property(x => x.RequirementTemplateId).HasColumnName("requirement_template_id").IsRequired();
            b.Property(x => x.ApprovalRouteId).HasColumnName("approval_route_id").IsRequired();
            b.Property(x => x.ChangePolicyId).HasColumnName("change_policy_id").IsRequired();
            b.Property(x => x.PublicationOption).HasColumnName("publication_option").HasMaxLength(32).IsRequired();
            b.Property(x => x.CreatedBy).HasColumnName("created_by").IsRequired();
            b.Property(x => x.State).HasColumnName("state").HasMaxLength(30).IsRequired();
            b.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();

            // OP-05: Unique API document per provider + consumer + api_group across all 4 valid XOR party combinations
            b.HasIndex(x => new { x.ProviderInternalSystemId, x.ConsumerEntityId, x.ApiGroupId })
                .IsUnique()
                .HasFilter("provider_internal_system_id IS NOT NULL AND consumer_entity_id IS NOT NULL")
                .HasDatabaseName("ux_api_documents_internal_provider_external_consumer_group");

            b.HasIndex(x => new { x.ProviderInternalSystemId, x.ConsumerCompanySystemId, x.ApiGroupId })
                .IsUnique()
                .HasFilter("provider_internal_system_id IS NOT NULL AND consumer_company_system_id IS NOT NULL")
                .HasDatabaseName("ux_api_documents_internal_provider_internal_consumer_group");

            b.HasIndex(x => new { x.ProviderExternalSystemId, x.ConsumerCompanySystemId, x.ApiGroupId })
                .IsUnique()
                .HasFilter("provider_external_system_id IS NOT NULL AND consumer_company_system_id IS NOT NULL")
                .HasDatabaseName("ux_api_documents_external_provider_internal_consumer_group");

            b.HasIndex(x => new { x.ProviderExternalSystemId, x.ConsumerEntityId, x.ApiGroupId })
                .IsUnique()
                .HasFilter("provider_external_system_id IS NOT NULL AND consumer_entity_id IS NOT NULL")
                .HasDatabaseName("ux_api_documents_external_provider_external_consumer_group");

            b.HasIndex(x => x.CreatedAt).HasDatabaseName("ix_api_documents_created_at");
            b.HasIndex(x => x.ApiGroupId).HasDatabaseName("ix_api_documents_api_group_id");

            b.HasOne<CompanySystemEntity>()
                .WithMany()
                .HasForeignKey(x => x.ProviderInternalSystemId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_api_documents_company_systems_provider_internal_system_id");

            b.HasOne<ExternalProviderSystemEntity>()
                .WithMany()
                .HasForeignKey(x => x.ProviderExternalSystemId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_api_documents_external_providers_provider_external_system_id");

            b.HasOne<CompanySystemEntity>()
                .WithMany()
                .HasForeignKey(x => x.ConsumerCompanySystemId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_api_documents_company_systems_consumer_company_system_id");

            b.HasOne<ConsumerEntityRecord>()
                .WithMany()
                .HasForeignKey(x => x.ConsumerEntityId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_api_documents_consumer_entities_consumer_entity_id");

            b.HasOne<ApiGroupEntity>()
                .WithMany()
                .HasForeignKey(x => x.ApiGroupId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_api_documents_api_groups_api_group_id");

            b.HasOne<FileRequirementTemplateEntity>()
                .WithMany()
                .HasForeignKey(x => x.RequirementTemplateId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_api_documents_requirement_templates_requirement_template_id");

            b.HasOne<ApprovalRouteEntity>()
                .WithMany()
                .HasForeignKey(x => x.ApprovalRouteId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_api_documents_approval_routes_approval_route_id");

            b.HasOne<ChangePolicyEntity>()
                .WithMany()
                .HasForeignKey(x => x.ChangePolicyId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_api_documents_change_policies_change_policy_id");

            b.HasOne<UserEntity>()
                .WithMany()
                .HasForeignKey(x => x.CreatedBy)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_api_documents_users_created_by");
        });

        // 23. document_developers
        modelBuilder.Entity<DocumentDeveloperEntity>(b =>
        {
            b.ToTable("document_developers");
            b.HasKey(x => x.Id).HasName("pk_document_developers");
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.DocumentId).HasColumnName("document_id").IsRequired();
            b.Property(x => x.UserId).HasColumnName("user_id").IsRequired();

            b.HasIndex(x => new { x.DocumentId, x.UserId }).IsUnique().HasDatabaseName("ux_document_developers_document_id_user_id");
            b.HasIndex(x => x.UserId).HasDatabaseName("ix_document_developers_user_id");

            b.HasOne<ApiDocumentEntity>()
                .WithMany()
                .HasForeignKey(x => x.DocumentId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_document_developers_api_documents_document_id");

            b.HasOne<UserEntity>()
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_document_developers_users_user_id");
        });

        // 24. document_role_permissions
        modelBuilder.Entity<DocumentRolePermissionEntity>(b =>
        {
            b.ToTable("document_role_permissions", t =>
            {
                t.HasCheckConstraint("ck_document_role_permissions_decision", "decision IN ('GRANT', 'REVOKE')");
            });
            b.HasKey(x => x.Id).HasName("pk_document_role_permissions");
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.DocumentId).HasColumnName("document_id").IsRequired();
            b.Property(x => x.RoleId).HasColumnName("role_id").IsRequired();
            b.Property(x => x.PermissionId).HasColumnName("permission_id").IsRequired();
            b.Property(x => x.Decision).HasColumnName("decision").HasMaxLength(10).IsRequired();

            b.HasIndex(x => new { x.DocumentId, x.RoleId, x.PermissionId })
                .IsUnique()
                .HasDatabaseName("ux_document_role_permissions_document_id_role_id_permission_id");

            b.HasOne<ApiDocumentEntity>()
                .WithMany()
                .HasForeignKey(x => x.DocumentId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_document_role_permissions_api_documents_document_id");

            b.HasOne<RoleEntity>()
                .WithMany()
                .HasForeignKey(x => x.RoleId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_document_role_permissions_roles_role_id");

            b.HasOne<PermissionEntity>()
                .WithMany()
                .HasForeignKey(x => x.PermissionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_document_role_permissions_permissions_permission_id");
        });

        // 25. document_user_permissions
        modelBuilder.Entity<DocumentUserPermissionEntity>(b =>
        {
            b.ToTable("document_user_permissions", t =>
            {
                t.HasCheckConstraint("ck_document_user_permissions_decision", "decision IN ('GRANT', 'REVOKE')");
            });
            b.HasKey(x => x.Id).HasName("pk_document_user_permissions");
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.DocumentId).HasColumnName("document_id").IsRequired();
            b.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
            b.Property(x => x.PermissionId).HasColumnName("permission_id").IsRequired();
            b.Property(x => x.Decision).HasColumnName("decision").HasMaxLength(10).IsRequired();

            b.HasIndex(x => new { x.DocumentId, x.UserId, x.PermissionId })
                .IsUnique()
                .HasDatabaseName("ux_document_user_permissions_document_id_user_id_permission_id");

            b.HasOne<ApiDocumentEntity>()
                .WithMany()
                .HasForeignKey(x => x.DocumentId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_document_user_permissions_api_documents_document_id");

            b.HasOne<UserEntity>()
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_document_user_permissions_users_user_id");

            b.HasOne<PermissionEntity>()
                .WithMany()
                .HasForeignKey(x => x.PermissionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_document_user_permissions_permissions_permission_id");
        });

        // 26. integrations (DB-03 & OP-05: 1:1 with api_documents via unique document_id + DeleteBehavior.Restrict prevents deleting linked document)
        modelBuilder.Entity<IntegrationEntity>(b =>
        {
            b.ToTable("integrations");
            b.HasKey(x => x.Id).HasName("pk_integrations");
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.DocumentId).HasColumnName("document_id").IsRequired();
            b.Property(x => x.ReferenceCode).HasColumnName("reference_code").HasMaxLength(60).IsRequired();
            b.Property(x => x.CreatedBy).HasColumnName("created_by").IsRequired();
            b.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();

            b.HasIndex(x => x.DocumentId).IsUnique().HasDatabaseName("ux_integrations_document_id");
            b.HasIndex(x => x.ReferenceCode).IsUnique().HasDatabaseName("ux_integrations_reference_code");
            b.HasIndex(x => x.CreatedAt).HasDatabaseName("ix_integrations_created_at");

            b.HasOne<ApiDocumentEntity>()
                .WithMany()
                .HasForeignKey(x => x.DocumentId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_integrations_api_documents_document_id");

            b.HasOne<UserEntity>()
                .WithMany()
                .HasForeignKey(x => x.CreatedBy)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_integrations_users_created_by");
        });

        // 27. integration_developers
        modelBuilder.Entity<IntegrationDeveloperEntity>(b =>
        {
            b.ToTable("integration_developers");
            b.HasKey(x => x.Id).HasName("pk_integration_developers");
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.IntegrationId).HasColumnName("integration_id").IsRequired();
            b.Property(x => x.UserId).HasColumnName("user_id").IsRequired();

            b.HasIndex(x => new { x.IntegrationId, x.UserId }).IsUnique().HasDatabaseName("ux_integration_developers_integration_id_user_id");
            b.HasIndex(x => x.UserId).HasDatabaseName("ix_integration_developers_user_id");

            b.HasOne<IntegrationEntity>()
                .WithMany()
                .HasForeignKey(x => x.IntegrationId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_integration_developers_integrations_integration_id");

            b.HasOne<UserEntity>()
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_integration_developers_users_user_id");
        });
    }

    private static void ConfigureGroup05VersionsFilesAndChanges(ModelBuilder modelBuilder)
    {
        // 28. api_versions (DB-12: free-text version_label unique per integration)
        modelBuilder.Entity<ApiVersionEntity>(b =>
        {
            b.ToTable("api_versions");
            b.HasKey(x => x.Id).HasName("pk_api_versions");
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.IntegrationId).HasColumnName("integration_id").IsRequired();
            b.Property(x => x.VersionLabel).HasColumnName("version_label").HasMaxLength(120).IsRequired();
            b.Property(x => x.IsArchived).HasColumnName("is_archived").IsRequired();
            b.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();

            b.HasIndex(x => new { x.IntegrationId, x.VersionLabel }).IsUnique().HasDatabaseName("ux_api_versions_integration_id_version_label");
            b.HasIndex(x => x.CreatedAt).HasDatabaseName("ix_api_versions_created_at");

            b.HasOne<IntegrationEntity>()
                .WithMany()
                .HasForeignKey(x => x.IntegrationId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_api_versions_integrations_integration_id");
        });

        // 29. document_revisions (OP-05: single active draft working copy per change_request / api_version)
        modelBuilder.Entity<DocumentRevisionEntity>(b =>
        {
            b.ToTable("document_revisions", t =>
            {
                t.HasCheckConstraint("ck_document_revisions_revision_number_positive", "revision_number >= 1");
            });
            b.HasKey(x => x.Id).HasName("pk_document_revisions");
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.ApiVersionId).HasColumnName("api_version_id").IsRequired();
            b.Property(x => x.RevisionNumber).HasColumnName("revision_number").IsRequired();
            b.Property(x => x.BaseRevisionId).HasColumnName("base_revision_id");
            b.Property(x => x.ChangeRequestId).HasColumnName("change_request_id");
            b.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
            b.Property(x => x.CreatedBy).HasColumnName("created_by").IsRequired();
            b.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            b.Property(x => x.ApprovedAt).HasColumnName("approved_at");

            b.HasIndex(x => new { x.ApiVersionId, x.RevisionNumber }).IsUnique().HasDatabaseName("ux_document_revisions_api_version_id_revision_number");

            // OP-05: Only one active working copy (DRAFT) per change request
            b.HasIndex(x => x.ChangeRequestId)
                .IsUnique()
                .HasFilter("change_request_id IS NOT NULL AND status = 'DRAFT'")
                .HasDatabaseName("ux_document_revisions_active_draft_per_change_request");

            // OP-05: Only one active working copy (DRAFT) per base revision
            b.HasIndex(x => x.BaseRevisionId)
                .IsUnique()
                .HasFilter("base_revision_id IS NOT NULL AND status = 'DRAFT'")
                .HasDatabaseName("ux_document_revisions_active_draft_per_base_revision");

            b.HasIndex(x => new { x.ApiVersionId, x.Status }).HasDatabaseName("ix_document_revisions_api_version_id_status");

            b.HasOne<ApiVersionEntity>()
                .WithMany()
                .HasForeignKey(x => x.ApiVersionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_document_revisions_api_versions_api_version_id");

            b.HasOne<DocumentRevisionEntity>()
                .WithMany()
                .HasForeignKey(x => x.BaseRevisionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_document_revisions_document_revisions_base_revision_id");

            b.HasOne<ChangeRequestEntity>()
                .WithMany()
                .HasForeignKey(x => x.ChangeRequestId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_document_revisions_change_requests_change_request_id");

            b.HasOne<UserEntity>()
                .WithMany()
                .HasForeignKey(x => x.CreatedBy)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_document_revisions_users_created_by");
        });

        // 30. revision_file_requirements
        modelBuilder.Entity<RevisionFileRequirementEntity>(b =>
        {
            b.ToTable("revision_file_requirements", t =>
            {
                t.HasCheckConstraint("ck_revision_file_requirements_counts", "min_count >= 0 AND max_count >= min_count");
            });
            b.HasKey(x => x.Id).HasName("pk_revision_file_requirements");
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.RevisionId).HasColumnName("revision_id").IsRequired();
            b.Property(x => x.FileTypeId).HasColumnName("file_type_id").IsRequired();
            b.Property(x => x.RequirementMode).HasColumnName("requirement_mode").HasMaxLength(16).IsRequired();
            b.Property(x => x.MinCount).HasColumnName("min_count").IsRequired();
            b.Property(x => x.MaxCount).HasColumnName("max_count").IsRequired();

            b.HasIndex(x => new { x.RevisionId, x.FileTypeId }).IsUnique().HasDatabaseName("ux_revision_file_requirements_revision_id_file_type_id");

            b.HasOne<DocumentRevisionEntity>()
                .WithMany()
                .HasForeignKey(x => x.RevisionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_revision_file_requirements_document_revisions_revision_id");

            b.HasOne<FileTypeEntity>()
                .WithMany()
                .HasForeignKey(x => x.FileTypeId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_revision_file_requirements_file_types_file_type_id");
        });

        // 31. file_objects
        modelBuilder.Entity<FileObjectEntity>(b =>
        {
            b.ToTable("file_objects", t =>
            {
                t.HasCheckConstraint("ck_file_objects_size_bytes_non_negative", "size_bytes >= 0");
                t.HasCheckConstraint("ck_file_objects_sha256_length", "length(trim(sha256)) = 64");
            });
            b.HasKey(x => x.Id).HasName("pk_file_objects");
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.StorageKey).HasColumnName("storage_key").HasMaxLength(500).IsRequired();
            b.Property(x => x.Sha256).HasColumnName("sha256").HasColumnType("char(64)").IsRequired();
            b.Property(x => x.SizeBytes).HasColumnName("size_bytes").IsRequired();
            b.Property(x => x.MediaType).HasColumnName("media_type").HasMaxLength(120);
            b.Property(x => x.ScanStatus).HasColumnName("scan_status").HasMaxLength(24).IsRequired();
            b.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();

            b.HasIndex(x => x.StorageKey).IsUnique().HasDatabaseName("ux_file_objects_storage_key");
            b.HasIndex(x => x.Sha256).HasDatabaseName("ix_file_objects_sha256");
        });

        // 32. revision_files
        modelBuilder.Entity<RevisionFileEntity>(b =>
        {
            b.ToTable("revision_files");
            b.HasKey(x => x.Id).HasName("pk_revision_files");
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.RevisionId).HasColumnName("revision_id").IsRequired();
            b.Property(x => x.FileTypeId).HasColumnName("file_type_id").IsRequired();
            b.Property(x => x.ObjectId).HasColumnName("object_id").IsRequired();
            b.Property(x => x.OriginalName).HasColumnName("original_name").HasMaxLength(255).IsRequired();
            b.Property(x => x.SourceRevisionFileId).HasColumnName("source_revision_file_id");
            b.Property(x => x.UploadedBy).HasColumnName("uploaded_by").IsRequired();
            b.Property(x => x.UploadedAt).HasColumnName("uploaded_at").IsRequired();

            b.HasIndex(x => new { x.RevisionId, x.FileTypeId }).HasDatabaseName("ix_revision_files_revision_id_file_type_id");
            b.HasIndex(x => x.ObjectId).HasDatabaseName("ix_revision_files_object_id");

            b.HasOne<DocumentRevisionEntity>()
                .WithMany()
                .HasForeignKey(x => x.RevisionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_revision_files_document_revisions_revision_id");

            b.HasOne<FileTypeEntity>()
                .WithMany()
                .HasForeignKey(x => x.FileTypeId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_revision_files_file_types_file_type_id");

            b.HasOne<FileObjectEntity>()
                .WithMany()
                .HasForeignKey(x => x.ObjectId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_revision_files_file_objects_object_id");

            b.HasOne<RevisionFileEntity>()
                .WithMany()
                .HasForeignKey(x => x.SourceRevisionFileId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_revision_files_revision_files_source_revision_file_id");

            b.HasOne<UserEntity>()
                .WithMany()
                .HasForeignKey(x => x.UploadedBy)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_revision_files_users_uploaded_by");
        });

        // 33. change_requests
        modelBuilder.Entity<ChangeRequestEntity>(b =>
        {
            b.ToTable("change_requests");
            b.HasKey(x => x.Id).HasName("pk_change_requests");
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.IntegrationId).HasColumnName("integration_id").IsRequired();
            b.Property(x => x.SourceRevisionId).HasColumnName("source_revision_id").IsRequired();
            b.Property(x => x.ChangeTypeId).HasColumnName("change_type_id").IsRequired();
            b.Property(x => x.Reason).HasColumnName("reason").IsRequired();
            b.Property(x => x.ConsumerImpact).HasColumnName("consumer_impact");
            b.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
            b.Property(x => x.RequestedBy).HasColumnName("requested_by").IsRequired();
            b.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();

            b.HasIndex(x => new { x.IntegrationId, x.Status }).HasDatabaseName("ix_change_requests_integration_id_status");
            b.HasIndex(x => x.CreatedAt).HasDatabaseName("ix_change_requests_created_at");

            b.HasOne<IntegrationEntity>()
                .WithMany()
                .HasForeignKey(x => x.IntegrationId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_change_requests_integrations_integration_id");

            b.HasOne<DocumentRevisionEntity>()
                .WithMany()
                .HasForeignKey(x => x.SourceRevisionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_change_requests_document_revisions_source_revision_id");

            b.HasOne<ChangeTypeEntity>()
                .WithMany()
                .HasForeignKey(x => x.ChangeTypeId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_change_requests_change_types_change_type_id");

            b.HasOne<UserEntity>()
                .WithMany()
                .HasForeignKey(x => x.RequestedBy)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_change_requests_users_requested_by");
        });

        // 34. change_request_files
        modelBuilder.Entity<ChangeRequestFileEntity>(b =>
        {
            b.ToTable("change_request_files");
            b.HasKey(x => x.Id).HasName("pk_change_request_files");
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.ChangeRequestId).HasColumnName("change_request_id").IsRequired();
            b.Property(x => x.SourceRevisionFileId).HasColumnName("source_revision_file_id").IsRequired();

            b.HasIndex(x => new { x.ChangeRequestId, x.SourceRevisionFileId })
                .IsUnique()
                .HasDatabaseName("ux_change_request_files_change_request_id_source_revision_file_id");

            b.HasOne<ChangeRequestEntity>()
                .WithMany()
                .HasForeignKey(x => x.ChangeRequestId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_change_request_files_change_requests_change_request_id");

            b.HasOne<RevisionFileEntity>()
                .WithMany()
                .HasForeignKey(x => x.SourceRevisionFileId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_change_request_files_revision_files_source_revision_file_id");
        });
    }

    private static void ConfigureGroup06WorkflowDecisions(ModelBuilder modelBuilder)
    {
        // 35. workflow_runs (DB-09: XOR target between revision_id and change_request_id)
        modelBuilder.Entity<WorkflowRunEntity>(b =>
        {
            b.ToTable("workflow_runs", t =>
            {
                t.HasCheckConstraint(
                    "ck_workflow_runs_target_xor",
                    "(revision_id IS NOT NULL AND change_request_id IS NULL) OR (revision_id IS NULL AND change_request_id IS NOT NULL)");
            });
            b.HasKey(x => x.Id).HasName("pk_workflow_runs");
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.RouteId).HasColumnName("route_id").IsRequired();
            b.Property(x => x.RevisionId).HasColumnName("revision_id");
            b.Property(x => x.ChangeRequestId).HasColumnName("change_request_id");
            b.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
            b.Property(x => x.SubmittedBy).HasColumnName("submitted_by").IsRequired();
            b.Property(x => x.StartedAt).HasColumnName("started_at").IsRequired();
            b.Property(x => x.EndedAt).HasColumnName("ended_at");

            b.HasIndex(x => new { x.Status, x.StartedAt }).HasDatabaseName("ix_workflow_runs_status_started_at");
            b.HasIndex(x => x.RevisionId).HasDatabaseName("ix_workflow_runs_revision_id");
            b.HasIndex(x => x.ChangeRequestId).HasDatabaseName("ix_workflow_runs_change_request_id");

            b.HasOne<ApprovalRouteEntity>()
                .WithMany()
                .HasForeignKey(x => x.RouteId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_workflow_runs_approval_routes_route_id");

            b.HasOne<DocumentRevisionEntity>()
                .WithMany()
                .HasForeignKey(x => x.RevisionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_workflow_runs_document_revisions_revision_id");

            b.HasOne<ChangeRequestEntity>()
                .WithMany()
                .HasForeignKey(x => x.ChangeRequestId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_workflow_runs_change_requests_change_request_id");

            b.HasOne<UserEntity>()
                .WithMany()
                .HasForeignKey(x => x.SubmittedBy)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_workflow_runs_users_submitted_by");
        });

        // 36. workflow_run_stages
        modelBuilder.Entity<WorkflowRunStageEntity>(b =>
        {
            b.ToTable("workflow_run_stages", t =>
            {
                t.HasCheckConstraint("ck_workflow_run_stages_position_positive", "position >= 1");
            });
            b.HasKey(x => x.Id).HasName("pk_workflow_run_stages");
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.RunId).HasColumnName("run_id").IsRequired();
            b.Property(x => x.Position).HasColumnName("position").IsRequired();
            b.Property(x => x.StageNameSnapshot).HasColumnName("stage_name_snapshot").HasMaxLength(160).IsRequired();
            b.Property(x => x.ResponsibleRoleIdSnapshot).HasColumnName("responsible_role_id_snapshot").IsRequired();
            b.Property(x => x.Status).HasColumnName("status").HasMaxLength(24).IsRequired();

            b.HasIndex(x => new { x.RunId, x.Position }).IsUnique().HasDatabaseName("ux_workflow_run_stages_run_id_position");
            b.HasIndex(x => new { x.ResponsibleRoleIdSnapshot, x.Status }).HasDatabaseName("ix_workflow_run_stages_role_status");

            b.HasOne<WorkflowRunEntity>()
                .WithMany()
                .HasForeignKey(x => x.RunId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_workflow_run_stages_workflow_runs_run_id");

            b.HasOne<RoleEntity>()
                .WithMany()
                .HasForeignKey(x => x.ResponsibleRoleIdSnapshot)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_workflow_run_stages_roles_responsible_role_id_snapshot");
        });

        // 37. workflow_decisions (Atomic single decision per run_stage_id)
        modelBuilder.Entity<WorkflowDecisionEntity>(b =>
        {
            b.ToTable("workflow_decisions");
            b.HasKey(x => x.Id).HasName("pk_workflow_decisions");
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.RunStageId).HasColumnName("run_stage_id").IsRequired();
            b.Property(x => x.ActorUserId).HasColumnName("actor_user_id").IsRequired();
            b.Property(x => x.Decision).HasColumnName("decision").HasMaxLength(20).IsRequired();
            b.Property(x => x.Notes).HasColumnName("notes");
            b.Property(x => x.DecidedAt).HasColumnName("decided_at").IsRequired();

            b.HasIndex(x => x.RunStageId).IsUnique().HasDatabaseName("ux_workflow_decisions_run_stage_id");
            b.HasIndex(x => x.ActorUserId).HasDatabaseName("ix_workflow_decisions_actor_user_id");

            b.HasOne<WorkflowRunStageEntity>()
                .WithMany()
                .HasForeignKey(x => x.RunStageId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_workflow_decisions_workflow_run_stages_run_stage_id");

            b.HasOne<UserEntity>()
                .WithMany()
                .HasForeignKey(x => x.ActorUserId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_workflow_decisions_users_actor_user_id");
        });
    }

    private static void ConfigureGroup07PublishingAndUsage(ModelBuilder modelBuilder)
    {
        // 38. publications
        modelBuilder.Entity<PublicationEntity>(b =>
        {
            b.ToTable("publications");
            b.HasKey(x => x.Id).HasName("pk_publications");
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.RevisionId).HasColumnName("revision_id").IsRequired();
            b.Property(x => x.State).HasColumnName("state").HasMaxLength(24).IsRequired();
            b.Property(x => x.PublishedBy).HasColumnName("published_by").IsRequired();
            b.Property(x => x.PublishedAt).HasColumnName("published_at").IsRequired();
            b.Property(x => x.WithdrawnAt).HasColumnName("withdrawn_at");

            b.HasIndex(x => new { x.RevisionId, x.State }).HasDatabaseName("ix_publications_revision_id_state");

            b.HasOne<DocumentRevisionEntity>()
                .WithMany()
                .HasForeignKey(x => x.RevisionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_publications_document_revisions_revision_id");

            b.HasOne<UserEntity>()
                .WithMany()
                .HasForeignKey(x => x.PublishedBy)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_publications_users_published_by");
        });

        // 39. publication_files
        modelBuilder.Entity<PublicationFileEntity>(b =>
        {
            b.ToTable("publication_files");
            b.HasKey(x => x.Id).HasName("pk_publication_files");
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.PublicationId).HasColumnName("publication_id").IsRequired();
            b.Property(x => x.RevisionFileId).HasColumnName("revision_file_id").IsRequired();

            b.HasIndex(x => new { x.PublicationId, x.RevisionFileId }).IsUnique().HasDatabaseName("ux_publication_files_publication_id_revision_file_id");

            b.HasOne<PublicationEntity>()
                .WithMany()
                .HasForeignKey(x => x.PublicationId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_publication_files_publications_publication_id");

            b.HasOne<RevisionFileEntity>()
                .WithMany()
                .HasForeignKey(x => x.RevisionFileId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_publication_files_revision_files_revision_file_id");
        });

        // 40. consumer_version_usage (DB-15: conditional unique index on integration_id WHERE valid_to IS NULL)
        modelBuilder.Entity<ConsumerVersionUsageEntity>(b =>
        {
            b.ToTable("consumer_version_usage", t =>
            {
                t.HasCheckConstraint("ck_consumer_version_usage_validity_range", "valid_to IS NULL OR valid_to >= valid_from");
            });
            b.HasKey(x => x.Id).HasName("pk_consumer_version_usage");
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.IntegrationId).HasColumnName("integration_id").IsRequired();
            b.Property(x => x.ApiVersionId).HasColumnName("api_version_id").IsRequired();
            b.Property(x => x.ValidFrom).HasColumnName("valid_from").IsRequired();
            b.Property(x => x.ValidTo).HasColumnName("valid_to");
            b.Property(x => x.AssignedBy).HasColumnName("assigned_by").IsRequired();

            b.HasIndex(x => x.IntegrationId)
                .IsUnique()
                .HasFilter("valid_to IS NULL")
                .HasDatabaseName("ux_consumer_version_usage_active_integration_id");

            b.HasIndex(x => x.ApiVersionId).HasDatabaseName("ix_consumer_version_usage_api_version_id");

            b.HasOne<IntegrationEntity>()
                .WithMany()
                .HasForeignKey(x => x.IntegrationId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_consumer_version_usage_integrations_integration_id");

            b.HasOne<ApiVersionEntity>()
                .WithMany()
                .HasForeignKey(x => x.ApiVersionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_consumer_version_usage_api_versions_api_version_id");

            b.HasOne<UserEntity>()
                .WithMany()
                .HasForeignKey(x => x.AssignedBy)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_consumer_version_usage_users_assigned_by");
        });
    }

    private static void ConfigureGroup08NotificationsAndAppendOnlyAudit(ModelBuilder modelBuilder)
    {
        // 41. notifications
        modelBuilder.Entity<NotificationEntity>(b =>
        {
            b.ToTable("notifications");
            b.HasKey(x => x.Id).HasName("pk_notifications");
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
            b.Property(x => x.Kind).HasColumnName("kind").HasMaxLength(90).IsRequired();
            b.Property(x => x.ResourceKind).HasColumnName("resource_kind").HasMaxLength(60).IsRequired();
            b.Property(x => x.ResourceId).HasColumnName("resource_id").IsRequired();
            b.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            b.Property(x => x.ReadAt).HasColumnName("read_at");

            b.HasIndex(x => new { x.UserId, x.ReadAt }).HasDatabaseName("ix_notifications_user_id_read_at");
            b.HasIndex(x => x.CreatedAt).HasDatabaseName("ix_notifications_created_at");

            b.HasOne<UserEntity>()
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_notifications_users_user_id");
        });

        // 42. audit_events (OP-01 & DB-16: Append-Only Audit Log)
        modelBuilder.Entity<AuditEventEntity>(b =>
        {
            b.ToTable("audit_events");
            b.HasKey(x => x.Id).HasName("pk_audit_events");
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.ActorUserId).HasColumnName("actor_user_id");
            b.Property(x => x.Operation).HasColumnName("operation").HasMaxLength(100).IsRequired();
            b.Property(x => x.ResourceKind).HasColumnName("resource_kind").HasMaxLength(60).IsRequired();
            b.Property(x => x.ResourceId).HasColumnName("resource_id");
            b.Property(x => x.Result).HasColumnName("result").HasMaxLength(25).IsRequired();
            b.Property(x => x.ChangeSummaryJson).HasColumnName("change_summary_json").HasColumnType("jsonb");
            b.Property(x => x.RecordedAt).HasColumnName("recorded_at").IsRequired();

            b.HasIndex(x => new { x.ResourceKind, x.ResourceId, x.RecordedAt })
                .HasDatabaseName("ix_audit_events_resource_kind_resource_id_recorded_at");
            b.HasIndex(x => x.RecordedAt).HasDatabaseName("ix_audit_events_recorded_at");
            b.HasIndex(x => x.ActorUserId).HasDatabaseName("ix_audit_events_actor_user_id");

            b.HasOne<UserEntity>()
                .WithMany()
                .HasForeignKey(x => x.ActorUserId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_audit_events_users_actor_user_id");
        });

        // 43. release_history_events (DB-16: Append-Only Release History Log)
        modelBuilder.Entity<ReleaseHistoryEventEntity>(b =>
        {
            b.ToTable("release_history_events");
            b.HasKey(x => x.Id).HasName("pk_release_history_events");
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.RevisionId).HasColumnName("revision_id").IsRequired();
            b.Property(x => x.EventType).HasColumnName("event_type").HasMaxLength(40).IsRequired();
            b.Property(x => x.ActorUserId).HasColumnName("actor_user_id").IsRequired();
            b.Property(x => x.ChangeRequestId).HasColumnName("change_request_id");
            b.Property(x => x.RecordedAt).HasColumnName("recorded_at").IsRequired();

            b.HasIndex(x => new { x.RevisionId, x.RecordedAt }).HasDatabaseName("ix_release_history_events_revision_id_recorded_at");

            b.HasOne<DocumentRevisionEntity>()
                .WithMany()
                .HasForeignKey(x => x.RevisionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_release_history_events_document_revisions_revision_id");

            b.HasOne<UserEntity>()
                .WithMany()
                .HasForeignKey(x => x.ActorUserId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_release_history_events_users_actor_user_id");

            b.HasOne<ChangeRequestEntity>()
                .WithMany()
                .HasForeignKey(x => x.ChangeRequestId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_release_history_events_change_requests_change_request_id");
        });
    }

    private static void ConfigureIdentitySessionTicketStore(ModelBuilder modelBuilder)
    {
        // auth_sessions (Task I01: revocable server-side ITicketStore per 04_Authentication.md & 08_DB_and_plan_delta.md)
        modelBuilder.Entity<AuthSessionEntity>(b =>
        {
            b.ToTable("auth_sessions");
            b.HasKey(x => x.Id).HasName("pk_auth_sessions");
            b.Property(x => x.Id).HasColumnName("id").HasMaxLength(128).ValueGeneratedNever();
            b.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
            b.Property(x => x.SecurityStamp).HasColumnName("security_stamp").HasMaxLength(128).IsRequired();
            b.Property(x => x.TicketBytes).HasColumnName("ticket_bytes").IsRequired();
            b.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            b.Property(x => x.LastSeenAt).HasColumnName("last_seen_at").IsRequired();
            b.Property(x => x.ExpiresAt).HasColumnName("expires_at").IsRequired();
            b.Property(x => x.AbsoluteExpiresAt).HasColumnName("absolute_expires_at").IsRequired();
            b.Property(x => x.RevokedAt).HasColumnName("revoked_at");

            b.HasIndex(x => x.UserId).HasDatabaseName("ix_auth_sessions_user_id");
            b.HasIndex(x => x.ExpiresAt).HasDatabaseName("ix_auth_sessions_expires_at");

            b.HasOne<UserEntity>()
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_auth_sessions_users_user_id");
        });
    }
}

