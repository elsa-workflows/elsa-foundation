using Groundwork.Documents.Store;
using Elsa.Persistence.Groundwork.Scoping;
using Elsa.Persistence.Groundwork.Targets;
using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.Unified.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elsa.Persistence.Groundwork.Sqlite.DependencyInjection;

/// <summary>
/// Shared wiring that declares one SQLite-backed Groundwork target and registers its scoped sessions.
/// A static provider factory is published after read-only schema admission by a
/// <see cref="SqliteGroundworkDocumentStoreInitializer"/>, which runs under the shared
/// <see cref="GroundworkTargetAdmissionInitializer"/> in the Prepare phase. Scoped
/// <see cref="IDocumentStore"/> adapters then acquire immutable access-bound sessions without retaining
/// request state. The initializer never applies or repairs schema.
/// </summary>
public static class SqliteGroundworkDocumentStoreRegistration
{
    public static IServiceCollection AddSqliteGroundworkDocumentStore(
        this IServiceCollection services,
        string connectionString,
        bool autoApplyOnStartup = false,
        bool skipInspectionWhenPlanUnchanged = false,
        SqliteGroundworkStoreCacheOptions? storeCacheOptions = null,
        string? targetName = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        var target = GroundworkTargetNames.Normalize(targetName);
        services.AddGroundworkStorageComposition();

        // Declaring the target is the composition guard. Repeating the same store under the same name is
        // idempotent, so composing this provider twice stays safe; a second, different connection under the
        // same name throws instead of being silently dropped the way an initializer-type check would.
        var declaration = services.DeclareGroundworkTarget(target, ProviderIdentity, connectionString);
        if (declaration is GroundworkTargetDeclarationResult.AlreadyDeclared)
            return services;

        services.AddGroundworkStoreSessions(target);

        services.AddGroundworkProviderCapabilityAdmission(target);
        services.AddKeyedSingleton(target, (serviceProvider, key) => new SqliteGroundworkDocumentStoreInitializer(
            (string)key,
            connectionString,
            autoApplyOnStartup,
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            serviceProvider.GetRequiredKeyedService<GroundworkStoreSessionSource>(key),
            // Provider composition is also used by source-only tooling hosts that intentionally omit logging.
            serviceProvider.GetService<ILogger<SqliteGroundworkDocumentStoreInitializer>>()
            ?? NullLogger<SqliteGroundworkDocumentStoreInitializer>.Instance,
            serviceProvider.GetRequiredKeyedService<Elsa.Persistence.Groundwork.Unified.Composition.GroundworkProviderCapabilityAdmission>(key),
            skipInspectionWhenPlanUnchanged,
            storeCacheOptions));
        services.AddGroundworkTargetAdmission<SqliteGroundworkDocumentStoreInitializer>(target);
        services.AddGroundworkSchemaReadinessGuard();
        return services;
    }

    /// <summary>The concrete Groundwork provider leaf identity recorded on every target this leaf declares.</summary>
    public const string ProviderIdentity = "sqlite";
}
