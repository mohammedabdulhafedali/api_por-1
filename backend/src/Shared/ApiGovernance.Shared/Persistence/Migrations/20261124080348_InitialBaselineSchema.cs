using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApiGovernance.Shared.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialBaselineSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "api_groups",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_api_groups", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "approval_routes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_approval_routes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "change_policies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_change_policies", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "company_systems",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_company_systems", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "consumer_categories",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_consumer_categories", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "external_provider_systems",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_external_provider_systems", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "file_objects",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    storage_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    sha256 = table.Column<string>(type: "char(64)", nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    media_type = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    scan_status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_file_objects", x => x.id);
                    table.CheckConstraint("ck_file_objects_sha256_length", "length(trim(sha256)) = 64");
                    table.CheckConstraint("ck_file_objects_size_bytes_non_negative", "size_bytes >= 0");
                });

            migrationBuilder.CreateTable(
                name: "file_requirement_templates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_file_requirement_templates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "permissions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    resource = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    action = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_permissions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "roles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    is_pinned = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_roles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "change_types",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    route_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_change_types", x => x.id);
                    table.ForeignKey(
                        name: "fk_change_types_approval_routes_route_id",
                        column: x => x.route_id,
                        principalTable: "approval_routes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "internal_provider_systems",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_system_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_internal_provider_systems", x => x.id);
                    table.ForeignKey(
                        name: "fk_internal_provider_systems_company_systems_company_system_id",
                        column: x => x.company_system_id,
                        principalTable: "company_systems",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "consumer_entities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_consumer_entities", x => x.id);
                    table.ForeignKey(
                        name: "fk_consumer_entities_consumer_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "consumer_categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "file_types",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    catalog_code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    display_name = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: false),
                    max_size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    word_template_object_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_file_types", x => x.id);
                    table.CheckConstraint("ck_file_types_max_size_bytes_positive", "max_size_bytes > 0");
                    table.ForeignKey(
                        name: "fk_file_types_file_objects_word_template_object_id",
                        column: x => x.word_template_object_id,
                        principalTable: "file_objects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "approval_route_stages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    route_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    stage_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    responsible_role_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_approval_route_stages", x => x.id);
                    table.CheckConstraint("ck_approval_route_stages_position_positive", "position >= 1");
                    table.ForeignKey(
                        name: "fk_approval_route_stages_approval_routes_route_id",
                        column: x => x.route_id,
                        principalTable: "approval_routes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_approval_route_stages_roles_responsible_role_id",
                        column: x => x.responsible_role_id,
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "role_permissions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    permission_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_role_permissions", x => x.id);
                    table.ForeignKey(
                        name: "fk_role_permissions_permissions_permission_id",
                        column: x => x.permission_id,
                        principalTable: "permissions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_role_permissions_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "change_policy_types",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    policy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    change_type_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_change_policy_types", x => x.id);
                    table.ForeignKey(
                        name: "fk_change_policy_types_change_policies_policy_id",
                        column: x => x.policy_id,
                        principalTable: "change_policies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_change_policy_types_change_types_change_type_id",
                        column: x => x.change_type_id,
                        principalTable: "change_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    full_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    normalized_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    security_stamp = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    concurrency_stamp = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    lockout_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    lockout_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    access_failed_count = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    consumer_entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                    table.CheckConstraint("ck_users_access_failed_count_non_negative", "access_failed_count >= 0");
                    table.ForeignKey(
                        name: "fk_users_consumer_entities_consumer_entity_id",
                        column: x => x.consumer_entity_id,
                        principalTable: "consumer_entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "file_requirement_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requirement_mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    min_count = table.Column<int>(type: "integer", nullable: false),
                    max_count = table.Column<int>(type: "integer", nullable: false),
                    condition_spec_json = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_file_requirement_rules", x => x.id);
                    table.CheckConstraint("ck_file_requirement_rules_counts", "min_count >= 0 AND max_count >= min_count");
                    table.ForeignKey(
                        name: "fk_file_requirement_rules_file_types_file_type_id",
                        column: x => x.file_type_id,
                        principalTable: "file_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_file_requirement_rules_templates_template_id",
                        column: x => x.template_id,
                        principalTable: "file_requirement_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "file_type_extensions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    extension = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_file_type_extensions", x => x.id);
                    table.ForeignKey(
                        name: "fk_file_type_extensions_file_types_file_type_id",
                        column: x => x.file_type_id,
                        principalTable: "file_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "api_documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_internal_system_id = table.Column<Guid>(type: "uuid", nullable: true),
                    provider_external_system_id = table.Column<Guid>(type: "uuid", nullable: true),
                    consumer_company_system_id = table.Column<Guid>(type: "uuid", nullable: true),
                    consumer_entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    api_group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requirement_template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    approval_route_id = table.Column<Guid>(type: "uuid", nullable: false),
                    change_policy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    publication_option = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_api_documents", x => x.id);
                    table.CheckConstraint("ck_api_documents_consumer_xor", "(consumer_company_system_id IS NOT NULL AND consumer_entity_id IS NULL) OR (consumer_company_system_id IS NULL AND consumer_entity_id IS NOT NULL)");
                    table.CheckConstraint("ck_api_documents_internal_parties_distinct", "provider_internal_system_id IS NULL OR consumer_company_system_id IS NULL OR provider_internal_system_id <> consumer_company_system_id");
                    table.CheckConstraint("ck_api_documents_provider_xor", "(provider_internal_system_id IS NOT NULL AND provider_external_system_id IS NULL) OR (provider_internal_system_id IS NULL AND provider_external_system_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_api_documents_api_groups_api_group_id",
                        column: x => x.api_group_id,
                        principalTable: "api_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_api_documents_approval_routes_approval_route_id",
                        column: x => x.approval_route_id,
                        principalTable: "approval_routes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_api_documents_change_policies_change_policy_id",
                        column: x => x.change_policy_id,
                        principalTable: "change_policies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_api_documents_company_systems_consumer_company_system_id",
                        column: x => x.consumer_company_system_id,
                        principalTable: "company_systems",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_api_documents_company_systems_provider_internal_system_id",
                        column: x => x.provider_internal_system_id,
                        principalTable: "company_systems",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_api_documents_consumer_entities_consumer_entity_id",
                        column: x => x.consumer_entity_id,
                        principalTable: "consumer_entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_api_documents_external_providers_provider_external_system_id",
                        column: x => x.provider_external_system_id,
                        principalTable: "external_provider_systems",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_api_documents_requirement_templates_requirement_template_id",
                        column: x => x.requirement_template_id,
                        principalTable: "file_requirement_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_api_documents_users_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "audit_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    operation = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    resource_kind = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    resource_id = table.Column<Guid>(type: "uuid", nullable: true),
                    result = table.Column<string>(type: "character varying(25)", maxLength: 25, nullable: false),
                    change_summary_json = table.Column<string>(type: "jsonb", nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_audit_events_users_actor_user_id",
                        column: x => x.actor_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(90)", maxLength: 90, nullable: false),
                    resource_kind = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    resource_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notifications", x => x.id);
                    table.ForeignKey(
                        name: "fk_notifications_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_permission_overrides",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    permission_id = table.Column<Guid>(type: "uuid", nullable: false),
                    decision = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_permission_overrides", x => x.id);
                    table.CheckConstraint("ck_user_permission_overrides_decision", "decision IN ('GRANT', 'REVOKE')");
                    table.ForeignKey(
                        name: "fk_user_permission_overrides_permissions_permission_id",
                        column: x => x.permission_id,
                        principalTable: "permissions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_user_permission_overrides_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_roles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_roles", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_roles_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_user_roles_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "document_developers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document_developers", x => x.id);
                    table.ForeignKey(
                        name: "fk_document_developers_api_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "api_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_document_developers_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "document_role_permissions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    permission_id = table.Column<Guid>(type: "uuid", nullable: false),
                    decision = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document_role_permissions", x => x.id);
                    table.CheckConstraint("ck_document_role_permissions_decision", "decision IN ('GRANT', 'REVOKE')");
                    table.ForeignKey(
                        name: "fk_document_role_permissions_api_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "api_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_document_role_permissions_permissions_permission_id",
                        column: x => x.permission_id,
                        principalTable: "permissions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_document_role_permissions_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "document_user_permissions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    permission_id = table.Column<Guid>(type: "uuid", nullable: false),
                    decision = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document_user_permissions", x => x.id);
                    table.CheckConstraint("ck_document_user_permissions_decision", "decision IN ('GRANT', 'REVOKE')");
                    table.ForeignKey(
                        name: "fk_document_user_permissions_api_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "api_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_document_user_permissions_permissions_permission_id",
                        column: x => x.permission_id,
                        principalTable: "permissions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_document_user_permissions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "integrations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reference_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_integrations", x => x.id);
                    table.ForeignKey(
                        name: "fk_integrations_api_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "api_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_integrations_users_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "api_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    integration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_label = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    is_archived = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_api_versions", x => x.id);
                    table.ForeignKey(
                        name: "fk_api_versions_integrations_integration_id",
                        column: x => x.integration_id,
                        principalTable: "integrations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "integration_developers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    integration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_integration_developers", x => x.id);
                    table.ForeignKey(
                        name: "fk_integration_developers_integrations_integration_id",
                        column: x => x.integration_id,
                        principalTable: "integrations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_integration_developers_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "consumer_version_usage",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    integration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    api_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    valid_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    valid_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    assigned_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_consumer_version_usage", x => x.id);
                    table.CheckConstraint("ck_consumer_version_usage_validity_range", "valid_to IS NULL OR valid_to >= valid_from");
                    table.ForeignKey(
                        name: "fk_consumer_version_usage_api_versions_api_version_id",
                        column: x => x.api_version_id,
                        principalTable: "api_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_consumer_version_usage_integrations_integration_id",
                        column: x => x.integration_id,
                        principalTable: "integrations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_consumer_version_usage_users_assigned_by",
                        column: x => x.assigned_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "change_request_files",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    change_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_revision_file_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_change_request_files", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "change_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    integration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_revision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    change_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: false),
                    consumer_impact = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    requested_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_change_requests", x => x.id);
                    table.ForeignKey(
                        name: "fk_change_requests_change_types_change_type_id",
                        column: x => x.change_type_id,
                        principalTable: "change_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_change_requests_integrations_integration_id",
                        column: x => x.integration_id,
                        principalTable: "integrations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_change_requests_users_requested_by",
                        column: x => x.requested_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "document_revisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    api_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    base_revision_id = table.Column<Guid>(type: "uuid", nullable: true),
                    change_request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document_revisions", x => x.id);
                    table.CheckConstraint("ck_document_revisions_revision_number_positive", "revision_number >= 1");
                    table.ForeignKey(
                        name: "fk_document_revisions_api_versions_api_version_id",
                        column: x => x.api_version_id,
                        principalTable: "api_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_document_revisions_change_requests_change_request_id",
                        column: x => x.change_request_id,
                        principalTable: "change_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_document_revisions_document_revisions_base_revision_id",
                        column: x => x.base_revision_id,
                        principalTable: "document_revisions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_document_revisions_users_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "publications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    published_by = table.Column<Guid>(type: "uuid", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    withdrawn_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_publications", x => x.id);
                    table.ForeignKey(
                        name: "fk_publications_document_revisions_revision_id",
                        column: x => x.revision_id,
                        principalTable: "document_revisions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_publications_users_published_by",
                        column: x => x.published_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "release_history_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    change_request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_release_history_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_release_history_events_change_requests_change_request_id",
                        column: x => x.change_request_id,
                        principalTable: "change_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_release_history_events_document_revisions_revision_id",
                        column: x => x.revision_id,
                        principalTable: "document_revisions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_release_history_events_users_actor_user_id",
                        column: x => x.actor_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "revision_file_requirements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requirement_mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    min_count = table.Column<int>(type: "integer", nullable: false),
                    max_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_revision_file_requirements", x => x.id);
                    table.CheckConstraint("ck_revision_file_requirements_counts", "min_count >= 0 AND max_count >= min_count");
                    table.ForeignKey(
                        name: "fk_revision_file_requirements_document_revisions_revision_id",
                        column: x => x.revision_id,
                        principalTable: "document_revisions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_revision_file_requirements_file_types_file_type_id",
                        column: x => x.file_type_id,
                        principalTable: "file_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "revision_files",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    object_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    source_revision_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    uploaded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_revision_files", x => x.id);
                    table.ForeignKey(
                        name: "fk_revision_files_document_revisions_revision_id",
                        column: x => x.revision_id,
                        principalTable: "document_revisions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_revision_files_file_objects_object_id",
                        column: x => x.object_id,
                        principalTable: "file_objects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_revision_files_file_types_file_type_id",
                        column: x => x.file_type_id,
                        principalTable: "file_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_revision_files_revision_files_source_revision_file_id",
                        column: x => x.source_revision_file_id,
                        principalTable: "revision_files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_revision_files_users_uploaded_by",
                        column: x => x.uploaded_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "workflow_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    route_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision_id = table.Column<Guid>(type: "uuid", nullable: true),
                    change_request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    submitted_by = table.Column<Guid>(type: "uuid", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workflow_runs", x => x.id);
                    table.CheckConstraint("ck_workflow_runs_target_xor", "(revision_id IS NOT NULL AND change_request_id IS NULL) OR (revision_id IS NULL AND change_request_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_workflow_runs_approval_routes_route_id",
                        column: x => x.route_id,
                        principalTable: "approval_routes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_workflow_runs_change_requests_change_request_id",
                        column: x => x.change_request_id,
                        principalTable: "change_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_workflow_runs_document_revisions_revision_id",
                        column: x => x.revision_id,
                        principalTable: "document_revisions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_workflow_runs_users_submitted_by",
                        column: x => x.submitted_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "publication_files",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    publication_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision_file_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_publication_files", x => x.id);
                    table.ForeignKey(
                        name: "fk_publication_files_publications_publication_id",
                        column: x => x.publication_id,
                        principalTable: "publications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_publication_files_revision_files_revision_file_id",
                        column: x => x.revision_file_id,
                        principalTable: "revision_files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "workflow_run_stages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    stage_name_snapshot = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    responsible_role_id_snapshot = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workflow_run_stages", x => x.id);
                    table.CheckConstraint("ck_workflow_run_stages_position_positive", "position >= 1");
                    table.ForeignKey(
                        name: "fk_workflow_run_stages_roles_responsible_role_id_snapshot",
                        column: x => x.responsible_role_id_snapshot,
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_workflow_run_stages_workflow_runs_run_id",
                        column: x => x.run_id,
                        principalTable: "workflow_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "workflow_decisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_stage_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    decision = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workflow_decisions", x => x.id);
                    table.ForeignKey(
                        name: "fk_workflow_decisions_users_actor_user_id",
                        column: x => x.actor_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_workflow_decisions_workflow_run_stages_run_stage_id",
                        column: x => x.run_stage_id,
                        principalTable: "workflow_run_stages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_api_documents_api_group_id",
                table: "api_documents",
                column: "api_group_id");

            migrationBuilder.CreateIndex(
                name: "IX_api_documents_approval_route_id",
                table: "api_documents",
                column: "approval_route_id");

            migrationBuilder.CreateIndex(
                name: "IX_api_documents_change_policy_id",
                table: "api_documents",
                column: "change_policy_id");

            migrationBuilder.CreateIndex(
                name: "IX_api_documents_consumer_company_system_id",
                table: "api_documents",
                column: "consumer_company_system_id");

            migrationBuilder.CreateIndex(
                name: "IX_api_documents_consumer_entity_id",
                table: "api_documents",
                column: "consumer_entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_api_documents_created_at",
                table: "api_documents",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "IX_api_documents_created_by",
                table: "api_documents",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "IX_api_documents_requirement_template_id",
                table: "api_documents",
                column: "requirement_template_id");

            migrationBuilder.CreateIndex(
                name: "ux_api_documents_external_provider_external_consumer_group",
                table: "api_documents",
                columns: new[] { "provider_external_system_id", "consumer_entity_id", "api_group_id" },
                unique: true,
                filter: "provider_external_system_id IS NOT NULL AND consumer_entity_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_api_documents_external_provider_internal_consumer_group",
                table: "api_documents",
                columns: new[] { "provider_external_system_id", "consumer_company_system_id", "api_group_id" },
                unique: true,
                filter: "provider_external_system_id IS NOT NULL AND consumer_company_system_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_api_documents_internal_provider_external_consumer_group",
                table: "api_documents",
                columns: new[] { "provider_internal_system_id", "consumer_entity_id", "api_group_id" },
                unique: true,
                filter: "provider_internal_system_id IS NOT NULL AND consumer_entity_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_api_documents_internal_provider_internal_consumer_group",
                table: "api_documents",
                columns: new[] { "provider_internal_system_id", "consumer_company_system_id", "api_group_id" },
                unique: true,
                filter: "provider_internal_system_id IS NOT NULL AND consumer_company_system_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_api_groups_name",
                table: "api_groups",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_api_versions_created_at",
                table: "api_versions",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ux_api_versions_integration_id_version_label",
                table: "api_versions",
                columns: new[] { "integration_id", "version_label" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_approval_route_stages_responsible_role_id",
                table: "approval_route_stages",
                column: "responsible_role_id");

            migrationBuilder.CreateIndex(
                name: "ux_approval_route_stages_route_id_position",
                table: "approval_route_stages",
                columns: new[] { "route_id", "position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_approval_routes_name",
                table: "approval_routes",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_actor_user_id",
                table: "audit_events",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_recorded_at",
                table: "audit_events",
                column: "recorded_at");

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_resource_kind_resource_id_recorded_at",
                table: "audit_events",
                columns: new[] { "resource_kind", "resource_id", "recorded_at" });

            migrationBuilder.CreateIndex(
                name: "ux_change_policies_name",
                table: "change_policies",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_change_policy_types_change_type_id",
                table: "change_policy_types",
                column: "change_type_id");

            migrationBuilder.CreateIndex(
                name: "ux_change_policy_types_policy_id_change_type_id",
                table: "change_policy_types",
                columns: new[] { "policy_id", "change_type_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_change_request_files_source_revision_file_id",
                table: "change_request_files",
                column: "source_revision_file_id");

            migrationBuilder.CreateIndex(
                name: "ux_change_request_files_change_request_id_source_revision_file_id",
                table: "change_request_files",
                columns: new[] { "change_request_id", "source_revision_file_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_change_requests_change_type_id",
                table: "change_requests",
                column: "change_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_change_requests_created_at",
                table: "change_requests",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_change_requests_integration_id_status",
                table: "change_requests",
                columns: new[] { "integration_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_change_requests_requested_by",
                table: "change_requests",
                column: "requested_by");

            migrationBuilder.CreateIndex(
                name: "IX_change_requests_source_revision_id",
                table: "change_requests",
                column: "source_revision_id");

            migrationBuilder.CreateIndex(
                name: "ix_change_types_route_id",
                table: "change_types",
                column: "route_id");

            migrationBuilder.CreateIndex(
                name: "ux_change_types_name",
                table: "change_types",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_company_systems_name",
                table: "company_systems",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_consumer_categories_name",
                table: "consumer_categories",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_consumer_entities_category_id",
                table: "consumer_entities",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ux_consumer_entities_name",
                table: "consumer_entities",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_consumer_version_usage_api_version_id",
                table: "consumer_version_usage",
                column: "api_version_id");

            migrationBuilder.CreateIndex(
                name: "IX_consumer_version_usage_assigned_by",
                table: "consumer_version_usage",
                column: "assigned_by");

            migrationBuilder.CreateIndex(
                name: "ux_consumer_version_usage_active_integration_id",
                table: "consumer_version_usage",
                column: "integration_id",
                unique: true,
                filter: "valid_to IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_document_developers_user_id",
                table: "document_developers",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ux_document_developers_document_id_user_id",
                table: "document_developers",
                columns: new[] { "document_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_document_revisions_api_version_id_status",
                table: "document_revisions",
                columns: new[] { "api_version_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_document_revisions_created_by",
                table: "document_revisions",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ux_document_revisions_active_draft_per_base_revision",
                table: "document_revisions",
                column: "base_revision_id",
                unique: true,
                filter: "base_revision_id IS NOT NULL AND status = 'DRAFT'");

            migrationBuilder.CreateIndex(
                name: "ux_document_revisions_active_draft_per_change_request",
                table: "document_revisions",
                column: "change_request_id",
                unique: true,
                filter: "change_request_id IS NOT NULL AND status = 'DRAFT'");

            migrationBuilder.CreateIndex(
                name: "ux_document_revisions_api_version_id_revision_number",
                table: "document_revisions",
                columns: new[] { "api_version_id", "revision_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_document_role_permissions_permission_id",
                table: "document_role_permissions",
                column: "permission_id");

            migrationBuilder.CreateIndex(
                name: "IX_document_role_permissions_role_id",
                table: "document_role_permissions",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ux_document_role_permissions_document_id_role_id_permission_id",
                table: "document_role_permissions",
                columns: new[] { "document_id", "role_id", "permission_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_document_user_permissions_permission_id",
                table: "document_user_permissions",
                column: "permission_id");

            migrationBuilder.CreateIndex(
                name: "IX_document_user_permissions_user_id",
                table: "document_user_permissions",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ux_document_user_permissions_document_id_user_id_permission_id",
                table: "document_user_permissions",
                columns: new[] { "document_id", "user_id", "permission_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_external_provider_systems_name",
                table: "external_provider_systems",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_file_objects_sha256",
                table: "file_objects",
                column: "sha256");

            migrationBuilder.CreateIndex(
                name: "ux_file_objects_storage_key",
                table: "file_objects",
                column: "storage_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_file_requirement_rules_file_type_id",
                table: "file_requirement_rules",
                column: "file_type_id");

            migrationBuilder.CreateIndex(
                name: "ux_file_requirement_rules_template_id_file_type_id",
                table: "file_requirement_rules",
                columns: new[] { "template_id", "file_type_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_file_requirement_templates_name",
                table: "file_requirement_templates",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_file_type_extensions_file_type_id_extension",
                table: "file_type_extensions",
                columns: new[] { "file_type_id", "extension" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_file_types_word_template_object_id",
                table: "file_types",
                column: "word_template_object_id");

            migrationBuilder.CreateIndex(
                name: "ux_file_types_catalog_code",
                table: "file_types",
                column: "catalog_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_integration_developers_user_id",
                table: "integration_developers",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ux_integration_developers_integration_id_user_id",
                table: "integration_developers",
                columns: new[] { "integration_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_integrations_created_at",
                table: "integrations",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "IX_integrations_created_by",
                table: "integrations",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ux_integrations_document_id",
                table: "integrations",
                column: "document_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_integrations_reference_code",
                table: "integrations",
                column: "reference_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_internal_provider_systems_company_system_id",
                table: "internal_provider_systems",
                column: "company_system_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_notifications_created_at",
                table: "notifications",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_user_id_read_at",
                table: "notifications",
                columns: new[] { "user_id", "read_at" });

            migrationBuilder.CreateIndex(
                name: "ux_permissions_code",
                table: "permissions",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_publication_files_revision_file_id",
                table: "publication_files",
                column: "revision_file_id");

            migrationBuilder.CreateIndex(
                name: "ux_publication_files_publication_id_revision_file_id",
                table: "publication_files",
                columns: new[] { "publication_id", "revision_file_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_publications_published_by",
                table: "publications",
                column: "published_by");

            migrationBuilder.CreateIndex(
                name: "ix_publications_revision_id_state",
                table: "publications",
                columns: new[] { "revision_id", "state" });

            migrationBuilder.CreateIndex(
                name: "IX_release_history_events_actor_user_id",
                table: "release_history_events",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_release_history_events_change_request_id",
                table: "release_history_events",
                column: "change_request_id");

            migrationBuilder.CreateIndex(
                name: "ix_release_history_events_revision_id_recorded_at",
                table: "release_history_events",
                columns: new[] { "revision_id", "recorded_at" });

            migrationBuilder.CreateIndex(
                name: "IX_revision_file_requirements_file_type_id",
                table: "revision_file_requirements",
                column: "file_type_id");

            migrationBuilder.CreateIndex(
                name: "ux_revision_file_requirements_revision_id_file_type_id",
                table: "revision_file_requirements",
                columns: new[] { "revision_id", "file_type_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_revision_files_file_type_id",
                table: "revision_files",
                column: "file_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_revision_files_object_id",
                table: "revision_files",
                column: "object_id");

            migrationBuilder.CreateIndex(
                name: "ix_revision_files_revision_id_file_type_id",
                table: "revision_files",
                columns: new[] { "revision_id", "file_type_id" });

            migrationBuilder.CreateIndex(
                name: "IX_revision_files_source_revision_file_id",
                table: "revision_files",
                column: "source_revision_file_id");

            migrationBuilder.CreateIndex(
                name: "IX_revision_files_uploaded_by",
                table: "revision_files",
                column: "uploaded_by");

            migrationBuilder.CreateIndex(
                name: "ix_role_permissions_permission_id",
                table: "role_permissions",
                column: "permission_id");

            migrationBuilder.CreateIndex(
                name: "ux_role_permissions_role_id_permission_id",
                table: "role_permissions",
                columns: new[] { "role_id", "permission_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_roles_name",
                table: "roles",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_permission_overrides_permission_id",
                table: "user_permission_overrides",
                column: "permission_id");

            migrationBuilder.CreateIndex(
                name: "ux_user_permission_overrides_user_id_permission_id",
                table: "user_permission_overrides",
                columns: new[] { "user_id", "permission_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_roles_role_id",
                table: "user_roles",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ux_user_roles_user_id_role_id",
                table: "user_roles",
                columns: new[] { "user_id", "role_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_consumer_entity_id",
                table: "users",
                column: "consumer_entity_id");

            migrationBuilder.CreateIndex(
                name: "ux_users_email",
                table: "users",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_users_normalized_email",
                table: "users",
                column: "normalized_email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_workflow_decisions_actor_user_id",
                table: "workflow_decisions",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "ux_workflow_decisions_run_stage_id",
                table: "workflow_decisions",
                column: "run_stage_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_workflow_run_stages_role_status",
                table: "workflow_run_stages",
                columns: new[] { "responsible_role_id_snapshot", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_workflow_run_stages_run_id_position",
                table: "workflow_run_stages",
                columns: new[] { "run_id", "position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_workflow_runs_change_request_id",
                table: "workflow_runs",
                column: "change_request_id");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_runs_revision_id",
                table: "workflow_runs",
                column: "revision_id");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_runs_route_id",
                table: "workflow_runs",
                column: "route_id");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_runs_status_started_at",
                table: "workflow_runs",
                columns: new[] { "status", "started_at" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_runs_submitted_by",
                table: "workflow_runs",
                column: "submitted_by");

            migrationBuilder.AddForeignKey(
                name: "fk_change_request_files_change_requests_change_request_id",
                table: "change_request_files",
                column: "change_request_id",
                principalTable: "change_requests",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_change_request_files_revision_files_source_revision_file_id",
                table: "change_request_files",
                column: "source_revision_file_id",
                principalTable: "revision_files",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_change_requests_document_revisions_source_revision_id",
                table: "change_requests",
                column: "source_revision_id",
                principalTable: "document_revisions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // OP-01 & DB-16: Enforce append-only immutability on audit_events and release_history_events at PostgreSQL level
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION prevent_append_only_mutation()
                RETURNS TRIGGER AS $$
                BEGIN
                    RAISE EXCEPTION 'Table "%" is strictly append-only (OP-01): % operations are prohibited.', TG_TABLE_NAME, TG_OP
                        USING ERRCODE = 'check_violation';
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER trg_audit_events_append_only
                    BEFORE UPDATE OR DELETE ON audit_events
                    FOR EACH ROW
                    EXECUTE FUNCTION prevent_append_only_mutation();

                CREATE TRIGGER trg_release_history_events_append_only
                    BEFORE UPDATE OR DELETE ON release_history_events
                    FOR EACH ROW
                    EXECUTE FUNCTION prevent_append_only_mutation();

                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'api_gov_app') THEN
                        REVOKE CREATE ON SCHEMA public FROM api_gov_app;
                        REVOKE UPDATE, DELETE, TRUNCATE ON TABLE audit_events, release_history_events FROM api_gov_app;
                        GRANT SELECT, INSERT ON TABLE audit_events, release_history_events TO api_gov_app;
                    END IF;
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_audit_events_append_only ON audit_events;
                DROP TRIGGER IF EXISTS trg_release_history_events_append_only ON release_history_events;
                DROP FUNCTION IF EXISTS prevent_append_only_mutation();
                """);

            migrationBuilder.DropForeignKey(
                name: "fk_api_documents_api_groups_api_group_id",
                table: "api_documents");

            migrationBuilder.DropForeignKey(
                name: "fk_api_documents_approval_routes_approval_route_id",
                table: "api_documents");

            migrationBuilder.DropForeignKey(
                name: "fk_change_types_approval_routes_route_id",
                table: "change_types");

            migrationBuilder.DropForeignKey(
                name: "fk_api_documents_change_policies_change_policy_id",
                table: "api_documents");

            migrationBuilder.DropForeignKey(
                name: "fk_api_documents_company_systems_consumer_company_system_id",
                table: "api_documents");

            migrationBuilder.DropForeignKey(
                name: "fk_api_documents_company_systems_provider_internal_system_id",
                table: "api_documents");

            migrationBuilder.DropForeignKey(
                name: "fk_api_documents_consumer_entities_consumer_entity_id",
                table: "api_documents");

            migrationBuilder.DropForeignKey(
                name: "fk_users_consumer_entities_consumer_entity_id",
                table: "users");

            migrationBuilder.DropForeignKey(
                name: "fk_api_documents_external_providers_provider_external_system_id",
                table: "api_documents");

            migrationBuilder.DropForeignKey(
                name: "fk_api_documents_requirement_templates_requirement_template_id",
                table: "api_documents");

            migrationBuilder.DropForeignKey(
                name: "fk_api_documents_users_created_by",
                table: "api_documents");

            migrationBuilder.DropForeignKey(
                name: "fk_change_requests_users_requested_by",
                table: "change_requests");

            migrationBuilder.DropForeignKey(
                name: "fk_document_revisions_users_created_by",
                table: "document_revisions");

            migrationBuilder.DropForeignKey(
                name: "fk_integrations_users_created_by",
                table: "integrations");

            migrationBuilder.DropForeignKey(
                name: "fk_api_versions_integrations_integration_id",
                table: "api_versions");

            migrationBuilder.DropForeignKey(
                name: "fk_change_requests_integrations_integration_id",
                table: "change_requests");

            migrationBuilder.DropForeignKey(
                name: "fk_change_requests_change_types_change_type_id",
                table: "change_requests");

            migrationBuilder.DropForeignKey(
                name: "fk_document_revisions_change_requests_change_request_id",
                table: "document_revisions");

            migrationBuilder.DropTable(
                name: "approval_route_stages");

            migrationBuilder.DropTable(
                name: "audit_events");

            migrationBuilder.DropTable(
                name: "change_policy_types");

            migrationBuilder.DropTable(
                name: "change_request_files");

            migrationBuilder.DropTable(
                name: "consumer_version_usage");

            migrationBuilder.DropTable(
                name: "document_developers");

            migrationBuilder.DropTable(
                name: "document_role_permissions");

            migrationBuilder.DropTable(
                name: "document_user_permissions");

            migrationBuilder.DropTable(
                name: "file_requirement_rules");

            migrationBuilder.DropTable(
                name: "file_type_extensions");

            migrationBuilder.DropTable(
                name: "integration_developers");

            migrationBuilder.DropTable(
                name: "internal_provider_systems");

            migrationBuilder.DropTable(
                name: "notifications");

            migrationBuilder.DropTable(
                name: "publication_files");

            migrationBuilder.DropTable(
                name: "release_history_events");

            migrationBuilder.DropTable(
                name: "revision_file_requirements");

            migrationBuilder.DropTable(
                name: "role_permissions");

            migrationBuilder.DropTable(
                name: "user_permission_overrides");

            migrationBuilder.DropTable(
                name: "user_roles");

            migrationBuilder.DropTable(
                name: "workflow_decisions");

            migrationBuilder.DropTable(
                name: "publications");

            migrationBuilder.DropTable(
                name: "revision_files");

            migrationBuilder.DropTable(
                name: "permissions");

            migrationBuilder.DropTable(
                name: "workflow_run_stages");

            migrationBuilder.DropTable(
                name: "file_types");

            migrationBuilder.DropTable(
                name: "roles");

            migrationBuilder.DropTable(
                name: "workflow_runs");

            migrationBuilder.DropTable(
                name: "file_objects");

            migrationBuilder.DropTable(
                name: "api_groups");

            migrationBuilder.DropTable(
                name: "approval_routes");

            migrationBuilder.DropTable(
                name: "change_policies");

            migrationBuilder.DropTable(
                name: "company_systems");

            migrationBuilder.DropTable(
                name: "consumer_entities");

            migrationBuilder.DropTable(
                name: "consumer_categories");

            migrationBuilder.DropTable(
                name: "external_provider_systems");

            migrationBuilder.DropTable(
                name: "file_requirement_templates");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "integrations");

            migrationBuilder.DropTable(
                name: "api_documents");

            migrationBuilder.DropTable(
                name: "change_types");

            migrationBuilder.DropTable(
                name: "change_requests");

            migrationBuilder.DropTable(
                name: "document_revisions");

            migrationBuilder.DropTable(
                name: "api_versions");
        }
    }
}
