using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.Targets;
using Elsa.Persistence.Groundwork.Unified.DependencyInjection;
using Elsa.Persistence.Groundwork.Scoping;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elsa.Persistence.Groundwork.SqlServer.DependencyInjection;

/// <summary>
/// Declares one admission-gated SQL Server Groundwork target. The initializer inspects the exact
/// host-selected physical target and never applies schema at runtime.
/// </summary>
public static class SqlServerGroundworkDocumentStoreRegistration
{
    public static IServiceCollection AddSqlServerGroundworkDocumentStore(
        this IServiceCollection services,
        string connectionString,
        bool autoApplyOnStartup = false,
        string? targetName = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        var target = GroundworkTargetNames.Normalize(targetName);
        services.AddGroundworkStorageComposition();

        // Declaring the target is the composition guard: an exact repeat is idempotent, a second and
        // different connection under the same name throws instead of being silently dropped.
        if (services.DeclareGroundworkTarget(target, ProviderIdentity, connectionString)
            is GroundworkTargetDeclarationResult.AlreadyDeclared)
        {
            return services;
        }

        services.AddGroundworkStoreSessions(target);

        services.AddGroundworkProviderCapabilityAdmission(target);

        services.AddKeyedSingleton(target, (serviceProvider, key) => new SqlServerGroundworkDocumentStoreInitializer(
            (string)key,
            connectionString,
            autoApplyOnStartup,
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            serviceProvider.GetRequiredKeyedService<GroundworkStoreSessionSource>(key),
            // Provider composition is also used by source-only tooling hosts that intentionally omit logging.
            serviceProvider.GetService<ILogger<SqlServerGroundworkDocumentStoreInitializer>>()
            ?? NullLogger<SqlServerGroundworkDocumentStoreInitializer>.Instance,
            serviceProvider.GetRequiredKeyedService<Elsa.Persistence.Groundwork.Unified.Composition.GroundworkProviderCapabilityAdmission>(key)));
        services.AddGroundworkTargetAdmission<SqlServerGroundworkDocumentStoreInitializer>(target);
        services.AddGroundworkSchemaReadinessGuard();
        return services;
    }

    /// <summary>The concrete Groundwork provider leaf identity recorded on every target this leaf declares.</summary>
    public const string ProviderIdentity = "sqlserver";
}
