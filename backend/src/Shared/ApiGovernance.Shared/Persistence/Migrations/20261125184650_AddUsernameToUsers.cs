using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApiGovernance.Shared.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUsernameToUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "normalized_username",
                table: "users",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "username",
                table: "users",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql(
                """
                WITH derived AS (
                    SELECT
                        id,
                        LEFT(COALESCE(NULLIF(SPLIT_PART(email, '@', 1), ''), 'user'), 85) AS base_name,
                        ROW_NUMBER() OVER (
                            PARTITION BY UPPER(LEFT(COALESCE(NULLIF(SPLIT_PART(email, '@', 1), ''), 'user'), 85))
                            ORDER BY created_at, id
                        ) AS rn
                    FROM users
                )
                UPDATE users u
                SET
                    username = CASE
                        WHEN d.rn = 1 THEN d.base_name
                        ELSE d.base_name || '-' || SUBSTRING(REPLACE(u.id::text, '-', ''), 1, 6)
                    END,
                    normalized_username = UPPER(CASE
                        WHEN d.rn = 1 THEN d.base_name
                        ELSE d.base_name || '-' || SUBSTRING(REPLACE(u.id::text, '-', ''), 1, 6)
                    END)
                FROM derived d
                WHERE u.id = d.id AND (u.username = '' OR u.normalized_username = '');
                """);

            migrationBuilder.CreateIndex(
                name: "ux_users_normalized_username",
                table: "users",
                column: "normalized_username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_users_username",
                table: "users",
                column: "username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_users_normalized_username",
                table: "users");

            migrationBuilder.DropIndex(
                name: "ux_users_username",
                table: "users");

            migrationBuilder.DropColumn(
                name: "normalized_username",
                table: "users");

            migrationBuilder.DropColumn(
                name: "username",
                table: "users");
        }
    }
}
