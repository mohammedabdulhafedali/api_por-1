using Microsoft.EntityFrameworkCore.Migrations;

namespace ApiGovernance.Shared.Persistence.Migrations;

public partial class SplitDocumentActionPermissions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Immutable mapping of the permissions enforced before this migration.
        // Materialize existing decisions once, preserving explicit atomic decisions.
        // Subsequent grants to doc.view/doc.edit do not grant unrelated actions.
        var mappings = new (string Code, string Parent)[]
        {
            ("doc.files.view", "doc.view"), ("doc.files.download", "doc.view"),
            ("doc.files.upload", "doc.files_manage"), ("doc.files.delete", "doc.files_manage"),
            ("doc.files.rescan", "doc.files_manage"), ("doc.files.word_generate", "doc.files_manage"),
            ("doc.versions.create", "doc.version_manage"), ("doc.versions.usage", "doc.publish"),
            ("doc.revisions.create", "doc.edit"), ("doc.revisions.submit", "doc.edit"),
            ("doc.review.approve", "doc.review"), ("doc.review.return", "doc.review"), ("doc.review.reject", "doc.review"),
            ("doc.publications.create", "doc.publish"), ("doc.publications.withdraw", "doc.publish"),
            ("doc.changes.create", "doc.change"), ("doc.changes.edit", "doc.change"), ("doc.changes.submit", "doc.change")
        };
        foreach (var (code, parent) in mappings)
        {
            // All SQL literals are the fixed codes above, never request input.
            migrationBuilder.Sql($"""
                INSERT INTO permissions (id, code, resource, action)
                VALUES (gen_random_uuid(), '{code}', 'document', '{code[4..]}') ON CONFLICT (code) DO NOTHING;
                INSERT INTO document_role_permissions (id, document_id, role_id, permission_id, decision)
                SELECT gen_random_uuid(), acl.document_id, acl.role_id, child.id, acl.decision
                FROM document_role_permissions acl
                JOIN permissions parent ON parent.id = acl.permission_id AND parent.code = '{parent}'
                CROSS JOIN permissions child
                WHERE child.code = '{code}'
                ON CONFLICT (document_id, role_id, permission_id) DO NOTHING;
                INSERT INTO document_user_permissions (id, document_id, user_id, permission_id, decision)
                SELECT gen_random_uuid(), acl.document_id, acl.user_id, child.id, acl.decision
                FROM document_user_permissions acl
                JOIN permissions parent ON parent.id = acl.permission_id AND parent.code = '{parent}'
                CROSS JOIN permissions child
                WHERE child.code = '{code}'
                ON CONFLICT (document_id, user_id, permission_id) DO NOTHING;
                """);
        }
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // The permission dictionary and historical ACL decisions are permanent.
        // Rolling back application code must not erase these explicit decisions.
    }
}
