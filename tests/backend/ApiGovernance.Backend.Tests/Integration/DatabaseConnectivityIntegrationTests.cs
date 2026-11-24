using System.Data;
using System.Net;
using System.Text.Json;
using ApiGovernance.Shared.Persistence;
using ApiGovernance.Shared.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace ApiGovernance.Backend.Tests.Integration;

[Collection("IsolatedTestDatabase")]
public sealed class DatabaseConnectivityIntegrationTests
{
    private static ApiGovernanceDbContext CreateDbContext(string connectionString)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ApiGovernanceDbContext>();
        optionsBuilder.UseNpgsql(connectionString, npgsql =>
        {
            npgsql.CommandTimeout(60);
            npgsql.MigrationsAssembly(typeof(ApiGovernanceDbContext).Assembly.FullName);
        });

        return new ApiGovernanceDbContext(optionsBuilder.Options);
    }

    private static async Task EnsureTestSchemaMigratedAsync()
    {
        var migratorConn = BackendContractIntegrationTests.GetIsolatedTestMigrationsConnectionString();
        await using var migratorContext = CreateDbContext(migratorConn);
        await migratorContext.Database.MigrateAsync();
    }

    private static PostgresException? FindPostgresException(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is PostgresException pgEx)
            {
                return pgEx;
            }
        }

        return null;
    }

    [Fact]
    public void DatabaseConnectionResolver_RejectsAdministrativePostgresSuperuser()
    {
        var adminConn = "Host=127.0.0.1;Port=5432;Database=api_governance_test;Username=postgres;Password=SuperSecretPassword123!";

        var ex = Assert.Throws<InvalidOperationException>(
            () => DatabaseConnectionResolver.ValidateLeastPrivilegeConnectionString(adminConn));

        Assert.Contains("postgres", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SuperSecretPassword123!", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DatabaseConnectionResolver_RedactsPasswordCompletely()
    {
        var raw = "Host=127.0.0.1;Port=5432;Database=api_governance_test;Username=api_gov_app;Password=TopSecretValue99!";
        var redacted = DatabaseConnectionResolver.RedactConnectionString(raw);

        Assert.DoesNotContain("TopSecretValue99!", redacted, StringComparison.Ordinal);
        Assert.Contains("Password=***REDACTED***", redacted, StringComparison.Ordinal);
        Assert.Contains("Username=api_gov_app", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void DatabaseConnectionResolver_ThrowsWhenConnectionStringIsMissing()
    {
        var emptyConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "",
                ["API_GOVERNANCE_DB_CONNECTION"] = ""
            })
            .Build();

        Assert.Throws<ArgumentException>(() => DatabaseConnectionResolver.ValidateLeastPrivilegeConnectionString(""));
        Assert.NotNull(emptyConfig);
    }

    [Fact]
    public async Task EfCore10_MigrationLifecycle_RollbackToZeroAndReapplyBaseline43Tables_AndVerifyConnectivity()
    {
        var migratorConn = BackendContractIntegrationTests.GetIsolatedTestMigrationsConnectionString();
        await using (var migratorContext = CreateDbContext(migratorConn))
        {
            var migrator = migratorContext.GetService<IMigrator>();

            // 1. Reset test DB to 0 migration state
            await migrator.MigrateAsync("0");
            var zeroCount = await CountPublicDomainTablesAsync(migratorContext);
            Assert.Equal(0L, zeroCount);

            // 2. Apply InitialBaselineSchema + AddIdentityAuthSessions + AddS09RevisionConcurrencyAndWordDerivatives (Up) -> 45 tables (43 domain + auth_sessions + word_derivative_records), 83 FKs
            await migrator.MigrateAsync();
            var appliedCount = await CountPublicDomainTablesAsync(migratorContext);
            Assert.Equal(45L, appliedCount);

            var fkCount = await CountForeignKeysAsync(migratorContext);
            Assert.Equal(83L, fkCount);

            // 3. Test Rollback (Down to "0") -> must drop all 45 tables and OP-01 triggers cleanly
            await migrator.MigrateAsync("0");
            var rolledBackCount = await CountPublicDomainTablesAsync(migratorContext);
            var rolledBackTriggers = await CountPublicTriggersAsync(migratorContext);
            Assert.Equal(0L, rolledBackCount);
            Assert.Equal(0L, rolledBackTriggers);

            // 4. Re-apply migrations (Up) so isolated test DB has full active schema
            await migrator.MigrateAsync();
            var reappliedCount = await CountPublicDomainTablesAsync(migratorContext);
            Assert.Equal(45L, reappliedCount);
        }

        // 5. Updated F04 connectivity check aligned with F05: verify runtime least-privilege role + 43 tables
        await using var factory = BackendContractIntegrationTests.CreateFactory("Testing");
        await using var scope = factory.Services.CreateAsyncScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<ApiGovernanceDbContext>();
        var probe = scope.ServiceProvider.GetRequiredService<IDatabaseConnectivityProbe>();

        var probeResult = await probe.CheckConnectivityAsync();
        Assert.True(probeResult.IsHealthy);
        Assert.True(probeResult.LatencyMs >= 0);

        var connection = dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT
                current_database() AS db_name,
                current_user AS user_name,
                (SELECT rolsuper FROM pg_roles WHERE rolname = current_user) AS is_super,
                has_schema_privilege(current_user, 'public', 'CREATE') AS can_create_ddl,
                (SELECT COUNT(*) FROM information_schema.tables
                 WHERE table_schema = 'public' AND table_type = 'BASE TABLE' AND table_name <> '__EFMigrationsHistory') AS domain_table_count;
        ";

        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());

        Assert.Equal("api_governance_test", reader.GetString(0));
        Assert.Equal("api_gov_app", reader.GetString(1));
        Assert.False(reader.GetBoolean(2));
        Assert.False(reader.GetBoolean(3));
        Assert.Equal(45L, reader.GetInt64(4));
    }

    [Fact]
    public async Task DatabasePrivilegeSeparation_VerifiesActualCapabilitiesInPostgreSql_AndRejectsExcessivePrivilegesRegardlessOfUsername()
    {
        await EnsureTestSchemaMigratedAsync();

        // 1. Inspect actual capabilities of runtime account vs migration account in PostgreSQL
        await using var factory = BackendContractIntegrationTests.CreateFactory("Production");
        await using var scope = factory.Services.CreateAsyncScope();
        var probe = scope.ServiceProvider.GetRequiredService<IDatabaseConnectivityProbe>();
        var runtimeContext = scope.ServiceProvider.GetRequiredService<ApiGovernanceDbContext>();

        var runtimeSnapshot = await probe.InspectCurrentAccountPrivilegesAsync(runtimeContext.Database.GetDbConnection());
        Assert.False(runtimeSnapshot.IsSuperuser);
        Assert.False(runtimeSnapshot.CanCreateDb);
        Assert.False(runtimeSnapshot.CanCreateRole);
        Assert.False(runtimeSnapshot.CanReplicate);
        Assert.False(runtimeSnapshot.BypassRls);
        Assert.True(runtimeSnapshot.CanUsePublicSchema);
        Assert.False(runtimeSnapshot.CanCreateInPublicSchema);

        var (runtimeValid, _) = DatabaseConnectivityProbe.ValidatePrivilegeSeparation(runtimeSnapshot, isMigrationContext: false);
        Assert.True(runtimeValid);

        // Runtime account must be blocked by PostgreSQL from executing DDL
        var ddlEx = await Assert.ThrowsAnyAsync<PostgresException>(
            () => runtimeContext.Database.ExecuteSqlRawAsync("CREATE TABLE f05_unauthorized_ddl_probe (id uuid PRIMARY KEY);"));
        Assert.Equal("42501", ddlEx.SqlState);

        // 2. Inspect migration account capabilities
        var migratorConn = BackendContractIntegrationTests.GetIsolatedTestMigrationsConnectionString();
        await using var migratorContext = CreateDbContext(migratorConn);
        var migratorSnapshot = await probe.InspectCurrentAccountPrivilegesAsync(migratorContext.Database.GetDbConnection());
        Assert.False(migratorSnapshot.IsSuperuser);
        Assert.False(migratorSnapshot.CanCreateDb);
        Assert.False(migratorSnapshot.CanCreateRole);
        Assert.True(migratorSnapshot.CanCreateInPublicSchema);

        var (migratorValidAsMigration, _) = DatabaseConnectivityProbe.ValidatePrivilegeSeparation(migratorSnapshot, isMigrationContext: true);
        Assert.True(migratorValidAsMigration);

        // Migration account must be REJECTED if someone tries to use it as the runtime application account
        var (migratorInvalidAsRuntime, reason) = DatabaseConnectivityProbe.ValidatePrivilegeSeparation(migratorSnapshot, isMigrationContext: false);
        Assert.False(migratorInvalidAsRuntime);
        Assert.Contains("DDL CREATE", reason, StringComparison.OrdinalIgnoreCase);

        // 3. Synthetic test proving privilege check evaluates actual flags rather than trusting a non-admin username
        var disguisedAdminSnapshot = new DatabaseAccountPrivilegeSnapshot(
            RoleName: "harmless_service_user",
            IsSuperuser: true,
            CanCreateDb: false,
            CanCreateRole: false,
            CanReplicate: false,
            BypassRls: false,
            CanCreateInPublicSchema: true,
            CanUsePublicSchema: true);
        var (disguisedValid, _) = DatabaseConnectivityProbe.ValidatePrivilegeSeparation(disguisedAdminSnapshot, isMigrationContext: false);
        Assert.False(disguisedValid);

        // 4. End-to-end health check in Production rejects running with the migration account
        await using var misconfiguredProdFactory = BackendContractIntegrationTests.CreateFactory("Production", migratorConn);
        using var prodClient = misconfiguredProdFactory.CreateClient();
        using var prodHealth = await prodClient.GetAsync("/health");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, prodHealth.StatusCode);
    }

    [Fact]
    public async Task AppendOnlyAuditAndReleaseHistory_EnforcesImmutabilityInEfCoreAndPostgresql()
    {
        await EnsureTestSchemaMigratedAsync();

        var appConn = BackendContractIntegrationTests.GetIsolatedTestDatabaseConnectionString();
        var migratorConn = BackendContractIntegrationTests.GetIsolatedTestMigrationsConnectionString();

        var eventId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        // 1. Insert append-only audit event via runtime app account -> succeeds
        await using (var appContext = CreateDbContext(appConn))
        {
            appContext.AuditEvents.Add(new AuditEventEntity
            {
                Id = eventId,
                ActorUserId = null,
                Operation = "F05_APPEND_ONLY_VERIFICATION",
                ResourceKind = "SYSTEM",
                ResourceId = null,
                Result = "SUCCESS",
                ChangeSummaryJson = "{\"probe\":true}",
                RecordedAt = now
            });
            var inserted = await appContext.SaveChangesAsync();
            Assert.Equal(1, inserted);

            // 2. Attempting UPDATE via EF Core DbContext must throw InvalidOperationException (OP-01 guard)
            var tracked = await appContext.AuditEvents.SingleAsync(x => x.Id == eventId);
            tracked.Result = "TAMPERED";
            await Assert.ThrowsAsync<InvalidOperationException>(() => appContext.SaveChangesAsync());
            appContext.ChangeTracker.Clear();

            // 3. Attempting DELETE via EF Core DbContext must throw InvalidOperationException (OP-01 guard)
            var toDelete = await appContext.AuditEvents.SingleAsync(x => x.Id == eventId);
            appContext.AuditEvents.Remove(toDelete);
            await Assert.ThrowsAsync<InvalidOperationException>(() => appContext.SaveChangesAsync());
            appContext.ChangeTracker.Clear();

            // 4. Attempting raw SQL UPDATE via runtime role is blocked at DB privilege/trigger level
            await Assert.ThrowsAnyAsync<PostgresException>(() =>
                appContext.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE audit_events SET result = 'TAMPERED' WHERE id = {eventId}"));
        }

        // 5. Even the migrator/table owner cannot UPDATE or DELETE due to PostgreSQL OP-01 triggers!
        await using (var migratorContext = CreateDbContext(migratorConn))
        {
            var updateEx = await Assert.ThrowsAnyAsync<PostgresException>(() =>
                migratorContext.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE audit_events SET result = 'TAMPERED' WHERE id = {eventId}"));
            Assert.Equal("23514", updateEx.SqlState);

            var deleteEx = await Assert.ThrowsAnyAsync<PostgresException>(() =>
                migratorContext.Database.ExecuteSqlInterpolatedAsync(
                    $"DELETE FROM audit_events WHERE id = {eventId}"));
            Assert.Equal("23514", deleteEx.SqlState);
        }
    }

    [Fact]
    public async Task RelationalConstraints_EnforceUniqueIndexesChecksXorAndDeleteRestrict()
    {
        await EnsureTestSchemaMigratedAsync();

        var appConn = BackendContractIntegrationTests.GetIsolatedTestDatabaseConnectionString();
        await using var db = CreateDbContext(appConn);
        await using var tx = await db.Database.BeginTransactionAsync();

        var now = DateTimeOffset.UtcNow;

        // Arrange valid baseline graph across groups 01..07
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var permissionId = Guid.NewGuid();
        var sysAId = Guid.NewGuid();
        var sysBId = Guid.NewGuid();
        var extSysId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var routeId = Guid.NewGuid();
        var policyId = Guid.NewGuid();
        var docId = Guid.NewGuid();
        var integrationId = Guid.NewGuid();
        var version1Id = Guid.NewGuid();
        var version2Id = Guid.NewGuid();
        var revisionId = Guid.NewGuid();

        db.Users.Add(new UserEntity
        {
            Id = userId,
            FullName = "F05 Constraint Tester",
            Email = $"f05-{userId:N}@example.local",
            NormalizedEmail = $"F05-{userId:N}@EXAMPLE.LOCAL",
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString("N"),
            IsActive = true,
            CreatedAt = now
        });

        db.Roles.Add(new RoleEntity
        {
            Id = roleId,
            Name = $"Role-{roleId:N}",
            IsActive = true,
            IsPinned = false
        });

        db.Permissions.Add(new PermissionEntity
        {
            Id = permissionId,
            Code = $"DOC_READ_{permissionId:N}",
            Resource = "DOCUMENT",
            Action = "READ"
        });

        db.CompanySystems.AddRange(
            new CompanySystemEntity { Id = sysAId, Name = $"SysA-{sysAId:N}", IsActive = true },
            new CompanySystemEntity { Id = sysBId, Name = $"SysB-{sysBId:N}", IsActive = true });

        db.ExternalProviderSystems.Add(new ExternalProviderSystemEntity
        {
            Id = extSysId,
            Name = $"Ext-{extSysId:N}",
            IsActive = true
        });

        db.ApiGroups.Add(new ApiGroupEntity { Id = groupId, Name = $"Group-{groupId:N}", IsActive = true });
        db.FileRequirementTemplates.Add(new FileRequirementTemplateEntity { Id = templateId, Name = $"Tpl-{templateId:N}", IsActive = true });
        db.ApprovalRoutes.Add(new ApprovalRouteEntity { Id = routeId, Name = $"Route-{routeId:N}", IsActive = true });
        db.ChangePolicies.Add(new ChangePolicyEntity { Id = policyId, Name = $"Policy-{policyId:N}", IsActive = true });

        db.ApiDocuments.Add(new ApiDocumentEntity
        {
            Id = docId,
            ProviderInternalSystemId = sysAId,
            ProviderExternalSystemId = null,
            ConsumerCompanySystemId = sysBId,
            ConsumerEntityId = null,
            ApiGroupId = groupId,
            RequirementTemplateId = templateId,
            ApprovalRouteId = routeId,
            ChangePolicyId = policyId,
            PublicationOption = "FULL",
            CreatedBy = userId,
            State = "APPROVED",
            CreatedAt = now
        });

        db.Integrations.Add(new IntegrationEntity
        {
            Id = integrationId,
            DocumentId = docId,
            ReferenceCode = $"INT-{integrationId:N}"[..24],
            CreatedBy = userId,
            CreatedAt = now
        });

        db.ApiVersions.AddRange(
            new ApiVersionEntity { Id = version1Id, IntegrationId = integrationId, VersionLabel = "1.0", CreatedAt = now },
            new ApiVersionEntity { Id = version2Id, IntegrationId = integrationId, VersionLabel = "2.0", CreatedAt = now });

        db.DocumentRevisions.Add(new DocumentRevisionEntity
        {
            Id = revisionId,
            ApiVersionId = version1Id,
            RevisionNumber = 1,
            Status = "APPROVED",
            CreatedBy = userId,
            CreatedAt = now,
            ApprovedAt = now
        });

        await db.SaveChangesAsync();

        // Check 1: Duplicate prevention on roles.name (Unique Index 23505)
        await tx.CreateSavepointAsync("sp_dup_role");
        db.Roles.Add(new RoleEntity { Id = Guid.NewGuid(), Name = $"Role-{roleId:N}", IsActive = true });
        var dupRoleEx = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal("23505", FindPostgresException(dupRoleEx)?.SqlState);
        db.ChangeTracker.Clear();
        await tx.RollbackToSavepointAsync("sp_dup_role");

        // Check 2: OP-05 Conditional Unique Index on api_documents (same provider + consumer + api_group -> 23505)
        await tx.CreateSavepointAsync("sp_dup_doc_parties");
        db.ApiDocuments.Add(new ApiDocumentEntity
        {
            Id = Guid.NewGuid(),
            ProviderInternalSystemId = sysAId,
            ConsumerCompanySystemId = sysBId,
            ApiGroupId = groupId,
            RequirementTemplateId = templateId,
            ApprovalRouteId = routeId,
            ChangePolicyId = policyId,
            PublicationOption = "FULL",
            CreatedBy = userId,
            State = "DRAFT",
            CreatedAt = now
        });
        var dupDocEx = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal("23505", FindPostgresException(dupDocEx)?.SqlState);
        db.ChangeTracker.Clear();
        await tx.RollbackToSavepointAsync("sp_dup_doc_parties");

        // Check 3: DB-01 XOR CHECK constraint on api_documents provider (both internal & external set -> 23514)
        await tx.CreateSavepointAsync("sp_doc_xor");
        db.ApiDocuments.Add(new ApiDocumentEntity
        {
            Id = Guid.NewGuid(),
            ProviderInternalSystemId = sysAId,
            ProviderExternalSystemId = extSysId,
            ConsumerCompanySystemId = sysBId,
            ApiGroupId = groupId,
            RequirementTemplateId = templateId,
            ApprovalRouteId = routeId,
            ChangePolicyId = policyId,
            PublicationOption = "FULL",
            CreatedBy = userId,
            State = "DRAFT",
            CreatedAt = now
        });
        var xorDocEx = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal("23514", FindPostgresException(xorDocEx)?.SqlState);
        db.ChangeTracker.Clear();
        await tx.RollbackToSavepointAsync("sp_doc_xor");

        // Check 4: DB-02 Internal provider cannot equal internal consumer -> 23514
        await tx.CreateSavepointAsync("sp_doc_self_party");
        db.ApiDocuments.Add(new ApiDocumentEntity
        {
            Id = Guid.NewGuid(),
            ProviderInternalSystemId = sysAId,
            ConsumerCompanySystemId = sysAId,
            ApiGroupId = groupId,
            RequirementTemplateId = templateId,
            ApprovalRouteId = routeId,
            ChangePolicyId = policyId,
            PublicationOption = "FULL",
            CreatedBy = userId,
            State = "DRAFT",
            CreatedAt = now
        });
        var selfPartyEx = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal("23514", FindPostgresException(selfPartyEx)?.SqlState);
        db.ChangeTracker.Clear();
        await tx.RollbackToSavepointAsync("sp_doc_self_party");

        // Check 5: DB-09 WorkflowRuns XOR target constraint (neither revision_id nor change_request_id set -> 23514)
        await tx.CreateSavepointAsync("sp_wf_xor");
        db.WorkflowRuns.Add(new WorkflowRunEntity
        {
            Id = Guid.NewGuid(),
            RouteId = routeId,
            RevisionId = null,
            ChangeRequestId = null,
            Status = "IN_PROGRESS",
            SubmittedBy = userId,
            StartedAt = now
        });
        var wfEx = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal("23514", FindPostgresException(wfEx)?.SqlState);
        db.ChangeTracker.Clear();
        await tx.RollbackToSavepointAsync("sp_wf_xor");

        // Check 6: CHECK constraint on user_permission_overrides.decision ('GRANT'|'REVOKE' only -> 23514)
        await tx.CreateSavepointAsync("sp_override_decision");
        db.UserPermissionOverrides.Add(new UserPermissionOverrideEntity
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            PermissionId = permissionId,
            Decision = "DENY"
        });
        var ovEx = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal("23514", FindPostgresException(ovEx)?.SqlState);
        db.ChangeTracker.Clear();
        await tx.RollbackToSavepointAsync("sp_override_decision");

        // Check 7: DB-15 Conditional unique index on consumer_version_usage (only 1 active row with valid_to IS NULL)
        db.ConsumerVersionUsage.AddRange(
            new ConsumerVersionUsageEntity
            {
                Id = Guid.NewGuid(),
                IntegrationId = integrationId,
                ApiVersionId = version1Id,
                ValidFrom = now.AddDays(-10),
                ValidTo = now.AddDays(-1), // historical closed row allowed
                AssignedBy = userId
            },
            new ConsumerVersionUsageEntity
            {
                Id = Guid.NewGuid(),
                IntegrationId = integrationId,
                ApiVersionId = version2Id,
                ValidFrom = now,
                ValidTo = null, // single active row allowed
                AssignedBy = userId
            });
        await db.SaveChangesAsync();

        await tx.CreateSavepointAsync("sp_dup_active_usage");
        db.ConsumerVersionUsage.Add(new ConsumerVersionUsageEntity
        {
            Id = Guid.NewGuid(),
            IntegrationId = integrationId,
            ApiVersionId = version1Id,
            ValidFrom = now,
            ValidTo = null, // second active row violates DB-15 -> 23505
            AssignedBy = userId
        });
        var usageEx = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal("23505", FindPostgresException(usageEx)?.SqlState);
        db.ChangeTracker.Clear();
        await tx.RollbackToSavepointAsync("sp_dup_active_usage");

        // Check 8: DB-03 DeleteBehavior.Restrict prevents deleting an ApiDocument referenced by Integration -> 23001 (restrict_violation)
        await tx.CreateSavepointAsync("sp_fk_restrict");
        db.ApiDocuments.Remove(new ApiDocumentEntity { Id = docId });
        var fkEx = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal("23001", FindPostgresException(fkEx)?.SqlState);
        db.ChangeTracker.Clear();
        await tx.RollbackToSavepointAsync("sp_fk_restrict");

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task HealthCheck_WhenDatabaseConnectionFails_Returns503Unhealthy_AndNeverLeaksCredentials()
    {
        const string fakeSecretPassword = "UltraSecretTestPassword_DoNotLeak_9988!";
        var unreachableConn = $"Host=127.0.0.1;Port=54399;Database=api_governance_test;Username=api_gov_app;Password={fakeSecretPassword};Timeout=1";

        await using var devFactory = BackendContractIntegrationTests.CreateFactory("Development", unreachableConn);
        using var devClient = devFactory.CreateClient();

        using var devRes = await devClient.GetAsync("/api/v1/health");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, devRes.StatusCode);

        var devRaw = await devRes.Content.ReadAsStringAsync();
        Assert.DoesNotContain(fakeSecretPassword, devRaw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("54399", devRaw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("api_gov_app", devRaw, StringComparison.OrdinalIgnoreCase);

        using var devDoc = JsonDocument.Parse(devRaw);
        Assert.Equal("Unhealthy", devDoc.RootElement.GetProperty("status").GetString());

        await using var prodFactory = BackendContractIntegrationTests.CreateFactory("Production", unreachableConn);
        using var prodClient = prodFactory.CreateClient();

        using var prodRes = await prodClient.GetAsync("/health");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, prodRes.StatusCode);

        var prodRaw = await prodRes.Content.ReadAsStringAsync();
        Assert.DoesNotContain(fakeSecretPassword, prodRaw, StringComparison.OrdinalIgnoreCase);
        using var prodDoc = JsonDocument.Parse(prodRaw);
        Assert.Equal("Unhealthy", prodDoc.RootElement.GetProperty("status").GetString());
        Assert.False(prodDoc.RootElement.TryGetProperty("checks", out _));
    }

    [Fact]
    public async Task HealthCheck_WhenConfiguredWithForbiddenPostgresSuperuser_Returns503Unhealthy()
    {
        var forbiddenAdminConn = "Host=127.0.0.1;Port=5432;Database=api_governance_test;Username=postgres;Password=SomeAdminPassword123!";

        await using var factory = BackendContractIntegrationTests.CreateFactory("Development", forbiddenAdminConn);
        using var client = factory.CreateClient();

        using var res = await client.GetAsync("/api/v1/health");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, res.StatusCode);

        var raw = await res.Content.ReadAsStringAsync();
        Assert.DoesNotContain("SomeAdminPassword123!", raw, StringComparison.OrdinalIgnoreCase);
        using var doc = JsonDocument.Parse(raw);
        Assert.Equal("Unhealthy", doc.RootElement.GetProperty("status").GetString());
    }

    private static async Task<long> CountPublicDomainTablesAsync(ApiGovernanceDbContext dbContext)
        => await ExecuteScalarInt64Async(dbContext, @"
            SELECT COUNT(*)
            FROM information_schema.tables
            WHERE table_schema = 'public'
              AND table_type = 'BASE TABLE'
              AND table_name <> '__EFMigrationsHistory';
        ");

    private static async Task<long> CountForeignKeysAsync(ApiGovernanceDbContext dbContext)
        => await ExecuteScalarInt64Async(dbContext, @"
            SELECT COUNT(*)
            FROM information_schema.table_constraints
            WHERE table_schema = 'public'
              AND constraint_type = 'FOREIGN KEY';
        ");

    private static async Task<long> CountPublicTriggersAsync(ApiGovernanceDbContext dbContext)
        => await ExecuteScalarInt64Async(dbContext, @"
            SELECT COUNT(*)
            FROM information_schema.triggers
            WHERE trigger_schema = 'public';
        ");

    private static async Task<long> ExecuteScalarInt64Async(ApiGovernanceDbContext dbContext, string sql)
    {
        var connection = dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt64(result);
    }
}
