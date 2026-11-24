using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace ApiGovernance.Shared.Persistence;

/// <summary>
/// Design-time factory for EF Core migrations using the dedicated non-superuser migration account
/// resolved from User Secrets or environment variables outside Git (Task F05).
/// </summary>
public sealed class ApiGovernanceDesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApiGovernanceDbContext>
{
    public ApiGovernanceDbContext CreateDbContext(string[] args)
    {
        var basePath = Directory.GetCurrentDirectory();
        var hostPath = Path.GetFullPath(Path.Combine(basePath, "..", "ApiGovernance.Host"));
        var configBasePath = Directory.Exists(hostPath) ? hostPath : basePath;

        var configuration = new ConfigurationBuilder()
            .SetBasePath(configBasePath)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddUserSecrets("ApiGovernance.Host.LocalDev")
            .AddEnvironmentVariables()
            .Build();

        string connectionString;
        try
        {
            connectionString = DatabaseConnectionResolver.ResolveMigrationsConnectionString(configuration);
        }
        catch (InvalidOperationException)
        {
            // Fallback for offline migration code generation / model inspection without a live DB secret
            connectionString = "Host=127.0.0.1;Port=5432;Database=api_governance_dev;Username=api_gov_migrator";
        }

        var optionsBuilder = new DbContextOptionsBuilder<ApiGovernanceDbContext>();
        optionsBuilder.UseNpgsql(connectionString, npgsqlOptions =>
        {
            npgsqlOptions.CommandTimeout(60);
            npgsqlOptions.MigrationsAssembly(typeof(ApiGovernanceDbContext).Assembly.FullName);
        });

        return new ApiGovernanceDbContext(optionsBuilder.Options);
    }
}

