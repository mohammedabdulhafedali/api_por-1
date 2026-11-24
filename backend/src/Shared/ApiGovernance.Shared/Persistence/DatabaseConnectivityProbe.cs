using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace ApiGovernance.Shared.Persistence;

public sealed record DatabaseProbeResult(
    bool IsHealthy,
    string StatusMessage,
    double LatencyMs);

public sealed record DatabaseAccountPrivilegeSnapshot(
    string RoleName,
    bool IsSuperuser,
    bool CanCreateDb,
    bool CanCreateRole,
    bool CanReplicate,
    bool BypassRls,
    bool CanCreateInPublicSchema,
    bool CanUsePublicSchema);

public interface IDatabaseConnectivityProbe
{
    Task<DatabaseProbeResult> CheckConnectivityAsync(CancellationToken cancellationToken = default);

    Task<DatabaseAccountPrivilegeSnapshot> InspectCurrentAccountPrivilegesAsync(
        DbConnection connection,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Verifies live connectivity and actual database role privileges in PostgreSQL 18 via EF Core 10 / Npgsql 10
/// while guaranteeing that connection strings, passwords, and internal host details are never exposed in responses.
/// </summary>
public sealed class DatabaseConnectivityProbe(
    IServiceProvider serviceProvider,
    IConfiguration configuration,
    ILogger<DatabaseConnectivityProbe> logger) : IDatabaseConnectivityProbe
{
    public async Task<DatabaseProbeResult> CheckConnectivityAsync(CancellationToken cancellationToken = default)
    {
        var startTimestamp = TimeProvider.System.GetTimestamp();

        try
        {
            // 1. Validate connection configuration & least-privilege account rule first
            _ = DatabaseConnectionResolver.ResolveConnectionString(configuration);

            await using var scope = serviceProvider.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApiGovernanceDbContext>();

            // 2. Open physical Npgsql connection and verify actual database privileges (not just username)
            var connection = dbContext.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync(cancellationToken);
            }

            var privileges = await InspectCurrentAccountPrivilegesAsync(connection, cancellationToken);
            var (isValidPrivileges, violationReason) = ValidatePrivilegeSeparation(
                privileges,
                isMigrationContext: false,
                enforceRuntimeDdlRestriction: true);

            var elapsedMs = TimeProvider.System.GetElapsedTime(startTimestamp).TotalMilliseconds;

            if (!isValidPrivileges)
            {
                logger.LogWarning(
                    "Database privilege separation check failed for current runtime account: {ViolationReason}",
                    violationReason);

                return new DatabaseProbeResult(
                    IsHealthy: false,
                    StatusMessage: "Database runtime account violates required least-privilege separation.",
                    LatencyMs: Math.Round(elapsedMs, 3));
            }

            return new DatabaseProbeResult(
                IsHealthy: true,
                StatusMessage: "PostgreSQL database connection and least-privilege checks are operational.",
                LatencyMs: Math.Round(elapsedMs, 3));
        }
        catch (Exception ex)
        {
            var elapsedMs = TimeProvider.System.GetElapsedTime(startTimestamp).TotalMilliseconds;

            // Log error type safely on server without logging raw connection string or credentials
            logger.LogWarning(
                "Database connectivity check failed ({ExceptionType}). Verify PostgreSQL service status and User Secrets configuration.",
                ex.GetType().Name);

            return new DatabaseProbeResult(
                IsHealthy: false,
                StatusMessage: "Database connection is unavailable or misconfigured.",
                LatencyMs: Math.Round(elapsedMs, 3));
        }
    }

    public async Task<DatabaseAccountPrivilegeSnapshot> InspectCurrentAccountPrivilegesAsync(
        DbConnection connection,
        CancellationToken cancellationToken = default)
    {
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT
                current_user::text AS role_name,
                r.rolsuper,
                r.rolcreatedb,
                r.rolcreaterole,
                r.rolreplication,
                r.rolbypassrls,
                has_schema_privilege(current_user, 'public', 'CREATE') AS can_create_ddl,
                has_schema_privilege(current_user, 'public', 'USAGE') AS can_use_schema
            FROM pg_roles r
            WHERE r.rolname = current_user;
        ";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Unable to inspect current PostgreSQL role privileges from pg_roles.");
        }

        return new DatabaseAccountPrivilegeSnapshot(
            RoleName: reader.GetString(0),
            IsSuperuser: reader.GetBoolean(1),
            CanCreateDb: reader.GetBoolean(2),
            CanCreateRole: reader.GetBoolean(3),
            CanReplicate: reader.GetBoolean(4),
            BypassRls: reader.GetBoolean(5),
            CanCreateInPublicSchema: reader.GetBoolean(6),
            CanUsePublicSchema: reader.GetBoolean(7));
    }

    /// <summary>
    /// Evaluates actual PostgreSQL role capabilities (independent of account name).
    /// </summary>
    public static (bool IsValid, string? FailureReason) ValidatePrivilegeSeparation(
        DatabaseAccountPrivilegeSnapshot snapshot,
        bool isMigrationContext,
        bool enforceRuntimeDdlRestriction = true)
    {
        if (snapshot.IsSuperuser || snapshot.CanCreateDb || snapshot.CanCreateRole || snapshot.CanReplicate || snapshot.BypassRls)
        {
            return (false, "Database role holds prohibited administrative capabilities (SUPERUSER/CREATEDB/CREATEROLE/REPLICATION/BYPASSRLS).");
        }

        if (!snapshot.CanUsePublicSchema)
        {
            return (false, "Database role lacks USAGE privilege on schema 'public'.");
        }

        if (isMigrationContext)
        {
            return snapshot.CanCreateInPublicSchema
                ? (true, null)
                : (false, "Migration role lacks CREATE privilege on schema 'public'.");
        }

        if (enforceRuntimeDdlRestriction && snapshot.CanCreateInPublicSchema)
        {
            return (false, "Runtime application role must not hold DDL CREATE privilege on schema 'public'.");
        }

        return (true, null);
    }
}

/// <summary>
/// ASP.NET Core HealthCheck integration for PostgreSQL 18 readiness & privilege verification (Tasks F04 & F05).
/// </summary>
public sealed class PostgreSqlReadinessHealthCheck(
    IDatabaseConnectivityProbe probe) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var result = await probe.CheckConnectivityAsync(cancellationToken);
        return result.IsHealthy
            ? HealthCheckResult.Healthy(result.StatusMessage)
            : HealthCheckResult.Unhealthy(result.StatusMessage);
    }
}

public static class PersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddApiGovernancePersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<ApiGovernanceDbContext>((sp, options) =>
        {
            var currentConfig = sp.GetRequiredService<IConfiguration>();
            var connectionString = DatabaseConnectionResolver.ResolveConnectionString(currentConfig);

            options.UseNpgsql(connectionString, npgsqlOptions =>
            {
                npgsqlOptions.CommandTimeout(30);
                npgsqlOptions.MigrationsAssembly(typeof(ApiGovernanceDbContext).Assembly.FullName);
            });
        });

        services.AddScoped<IDatabaseConnectivityProbe, DatabaseConnectivityProbe>();

        return services;
    }
}
