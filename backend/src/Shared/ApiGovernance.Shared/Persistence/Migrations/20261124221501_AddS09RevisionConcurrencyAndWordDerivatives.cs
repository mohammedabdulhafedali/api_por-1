using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApiGovernance.Shared.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddS09RevisionConcurrencyAndWordDerivatives : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "word_derivative_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision_file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_object_id = table.Column<Guid>(type: "uuid", nullable: false),
                    derived_object_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    template_kind_detected = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    fields_updated_count = table.Column<int>(type: "integer", nullable: false),
                    matched_fields_json = table.Column<string>(type: "jsonb", nullable: false),
                    validation_errors_json = table.Column<string>(type: "jsonb", nullable: false),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    is_publishable = table.Column<bool>(type: "boolean", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_word_derivative_records", x => x.id);
                    table.CheckConstraint("ck_word_derivative_records_fields_non_negative", "fields_updated_count >= 0");
                    table.ForeignKey(
                        name: "fk_word_derivative_records_document_revisions_revision_id",
                        column: x => x.revision_id,
                        principalTable: "document_revisions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_word_derivative_records_file_objects_derived_object_id",
                        column: x => x.derived_object_id,
                        principalTable: "file_objects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_word_derivative_records_file_objects_source_object_id",
                        column: x => x.source_object_id,
                        principalTable: "file_objects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_word_derivative_records_revision_files_revision_file_id",
                        column: x => x.revision_file_id,
                        principalTable: "revision_files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_word_derivative_records_users_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_document_revisions_active_draft_per_api_version",
                table: "document_revisions",
                column: "api_version_id",
                unique: true,
                filter: "status = 'DRAFT'");

            migrationBuilder.CreateIndex(
                name: "IX_word_derivative_records_created_by",
                table: "word_derivative_records",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "IX_word_derivative_records_derived_object_id",
                table: "word_derivative_records",
                column: "derived_object_id");

            migrationBuilder.CreateIndex(
                name: "ix_word_derivative_records_revision_file_id_created_at",
                table: "word_derivative_records",
                columns: new[] { "revision_file_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_word_derivative_records_revision_id",
                table: "word_derivative_records",
                column: "revision_id");

            migrationBuilder.CreateIndex(
                name: "IX_word_derivative_records_source_object_id",
                table: "word_derivative_records",
                column: "source_object_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "word_derivative_records");

            migrationBuilder.DropIndex(
                name: "ux_document_revisions_active_draft_per_api_version",
                table: "document_revisions");
        }
    }
}
