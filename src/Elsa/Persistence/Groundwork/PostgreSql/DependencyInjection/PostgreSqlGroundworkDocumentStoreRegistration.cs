using Groundwork.Documents.Store;
using Elsa.Persistence.Groundwork.Scoping;
using Elsa.Persistence.Groundwork.Targets;
using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.Unified.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elsa.Persistence.Groundwork.PostgreSql.DependencyInjection;

/// <summary>
/// Shared wiring that declares one PostgreSQL-backed Groundwork target and registers its scoped sessions.
/// A static provider factory is published after read-only schema admission by a
/// <see cref="PostgreSqlGroundworkDocumentStoreInitializer"/>, which runs under the shared
/// <see cref="GroundworkTargetAdmissionInitializer"/> in the Prepare phase. Scoped
/// <see cref="IDocumentStore"/> adapters then acquire immutable access-bound sessions without retaining
/// request state. The initializer never applies or repairs schema.
/// </summary>
public static class PostgreSqlGroundworkDocumentStoreRegistration
{
    public static IServiceCollection AddPostgreSqlGroundworkDocumentStore(
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
        services.AddKeyedSingleton(target, (serviceProvider, key) => new PostgreSqlGroundworkDocumentStoreInitializer(
            (string)key,
            connectionString,
            autoApplyOnStartup,
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            serviceProvider.GetRequiredKeyedService<GroundworkStoreSessionSource>(key),
            // Provider composition is also used by source-only tooling hosts that intentionally omit logging.
            serviceProvider.GetService<ILogger<PostgreSqlGroundworkDocumentStoreInitializer>>()
            ?? NullLogger<PostgreSqlGroundworkDocumentStoreInitializer>.Instance,
            serviceProvider.GetRequiredKeyedService<Elsa.Persistence.Groundwork.Unified.Composition.GroundworkProviderCapabilityAdmission>(key)));
        // Aggregate dashboard reads query the physical tables directly and need this target's connection.
        services.AddKeyedSingleton<IGroundworkTargetConnectionSource>(
            target,
            (_, key) => new GroundworkTargetConnectionSource(
                (string)key,
                ProviderIdentity,
                () => new Npgsql.NpgsqlConnection(connectionString)));
        services.AddGroundworkTargetAdmission<PostgreSqlGroundworkDocumentStoreInitializer>(target);
        services.AddGroundworkSchemaReadinessGuard();
        return services;
    }

    /// <summary>The concrete Groundwork provider leaf identity recorded on every target this leaf declares.</summary>
    public const string ProviderIdentity = "postgresql";
}
