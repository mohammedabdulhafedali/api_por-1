using Microsoft.Extensions.Configuration;
using Npgsql;

namespace ApiGovernance.Shared.Persistence;

/// <summary>
/// Unified, secure resolver for PostgreSQL connection settings (Tasks F04 & F05).
/// Reads runtime and migration connection strings from .NET Configuration (User Secrets / Environment Variables)
/// and enforces least-privilege database role rules (forbids administrative superuser accounts).
/// </summary>
public static class DatabaseConnectionResolver
{
    public const string DefaultConnectionName = "DefaultConnection";
    public const string MigrationsConnectionName = "MigrationsConnection";
    public const string FallbackEnvVarName = "API_GOVERNANCE_DB_CONNECTION";
    public const string FallbackMigrationsEnvVarName = "API_GOVERNANCE_MIGRATIONS_DB_CONNECTION";
    public const string ForbiddenSuperuserAccount = "postgres";

    private static readonly HashSet<string> ForbiddenAdminAccounts = new(StringComparer.OrdinalIgnoreCase)
    {
        "postgres",
        "rds_superuser",
        "azure_pg_admin"
    };

    public static string ResolveConnectionString(IConfiguration configuration)
    {
        var raw = configuration.GetConnectionString(DefaultConnectionName);
        if (string.IsNullOrWhiteSpace(raw))
        {
            raw = configuration[FallbackEnvVarName] ?? Environment.GetEnvironmentVariable(FallbackEnvVarName);
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new InvalidOperationException(
                "Database connection string is not configured. Configure 'ConnectionStrings:DefaultConnection' via .NET User Secrets or environment variables outside Git.");
        }

        ValidateLeastPrivilegeConnectionString(raw);
        return raw;
    }

    public static string ResolveMigrationsConnectionString(IConfiguration configuration)
    {
        var raw = configuration.GetConnectionString(MigrationsConnectionName);
        if (string.IsNullOrWhiteSpace(raw))
        {
            raw = configuration[FallbackMigrationsEnvVarName] ?? Environment.GetEnvironmentVariable(FallbackMigrationsEnvVarName);
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new InvalidOperationException(
                "Migrations database connection string is not configured. Configure 'ConnectionStrings:MigrationsConnection' or 'API_GOVERNANCE_MIGRATIONS_DB_CONNECTION' outside Git.");
        }

        ValidateLeastPrivilegeConnectionString(raw);
        return raw;
    }

    public static void ValidateLeastPrivilegeConnectionString(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string cannot be empty.", nameof(connectionString));
        }

        NpgsqlConnectionStringBuilder builder;
        try
        {
            builder = new NpgsqlConnectionStringBuilder(connectionString);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Database connection string format is invalid.", ex);
        }

        if (string.IsNullOrWhiteSpace(builder.Host))
        {
            throw new InvalidOperationException("Database connection string must specify a valid Host.");
        }

        if (string.IsNullOrWhiteSpace(builder.Database))
        {
            throw new InvalidOperationException("Database connection string must specify a target Database.");
        }

        if (string.IsNullOrWhiteSpace(builder.Username))
        {
            throw new InvalidOperationException("Database connection string must specify a dedicated application Username.");
        }

        if (ForbiddenAdminAccounts.Contains(builder.Username.Trim()))
        {
            throw new InvalidOperationException(
                $"Using the administrative superuser account '{builder.Username}' is strictly prohibited. Use a dedicated non-superuser database role.");
        }
    }

    /// <summary>
    /// Redacts password and sensitive credentials from a connection string for safe diagnostics.
    /// </summary>
    public static string RedactConnectionString(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return "<unconfigured>";
        }

        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            if (!string.IsNullOrEmpty(builder.Password))
            {
                builder.Password = "***REDACTED***";
            }

            return $"Host={builder.Host};Port={builder.Port};Database={builder.Database};Username={builder.Username};Password=***REDACTED***";
        }
        catch
        {
            return "<invalid-connection-string>";
        }
    }
}
