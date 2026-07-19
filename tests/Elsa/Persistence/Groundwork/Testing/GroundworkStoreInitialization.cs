using CShells.Lifecycle;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.Unified.Composition;
using Elsa.Persistence.Groundwork.Unified.DependencyInjection;
using Groundwork.Core.Capabilities;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.SchemaEvolution;
using Groundwork.PostgreSql;
using Groundwork.PostgreSql.PhysicalStorage;
using Groundwork.Sqlite;
using Groundwork.Sqlite.PhysicalStorage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elsa.Persistence.Groundwork.Testing;

/// <summary>
/// Test helper that drives Groundwork document-store startup for bare <see cref="IServiceProvider"/>s. In a real
/// host the store is materialized by a hosted service / CShells shell initializer before the first consumer
/// resolves <c>IDocumentStore</c>; a bare provider built in a test has no such lifecycle, so the store must be
/// initialized explicitly. This runs every registered <see cref="IShellInitializer"/> (which includes the
/// provider's document-store initializer), mirroring the shell-activation path. Idempotent.
/// </summary>
public static class GroundworkStoreInitialization
{
    /// <summary>Compiles the exact runtime physical target used by production provider admission.</summary>
    public static async ValueTask<GroundworkPhysicalSchemaManifestSource> CreateRuntimePhysicalSchemaSourceAsync(
        ProviderCapabilityReport capabilityReport,
        GroundworkProviderTopologySnapshot topology,
        IProviderPhysicalNameNormalizer providerNameNormalizer,
        IGroundworkPhysicalSchemaTargetCompiler? targetCompiler = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(capabilityReport);
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(providerNameNormalizer);

        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddGroundworkRuntimeStores();
        services.AddGroundworkStorageComposition();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<GroundworkStorageCompositionFactory>()
            .CreateSourceAsync(
                GroundworkProviderCapabilitySnapshot.ForFeatureRoutes(
                    capabilityReport,
                    topology,
                    RuntimeGroundworkStorageManifestSource.FeatureName,
                    [RuntimeGroundworkStorageManifestSource.CreateCheckpointCommitRouteRequirement()]),
                providerNameNormalizer,
                cancellationToken,
                targetCompiler);
    }

    /// <summary>Compiles a physical target for the supplied manifest sources without selecting runtime implicitly.</summary>
    public static async ValueTask<GroundworkPhysicalSchemaManifestSource> CreatePhysicalSchemaSourceAsync(
        ProviderCapabilityReport capabilityReport,
        GroundworkProviderTopologySnapshot topology,
        IProviderPhysicalNameNormalizer providerNameNormalizer,
        IReadOnlyCollection<IGroundworkStorageManifestSource> manifestSources,
        IGroundworkPhysicalSchemaTargetCompiler? targetCompiler = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(capabilityReport);
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(providerNameNormalizer);
        ArgumentNullException.ThrowIfNull(manifestSources);
        if (manifestSources.Count == 0)
            throw new ArgumentException("At least one manifest source is required.", nameof(manifestSources));

        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddGroundworkStorageComposition();
        foreach (var manifestSource in manifestSources)
        {
            services.AddScoped<IGroundworkStorageManifestSource>(_ => manifestSource);
        }

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<GroundworkStorageCompositionFactory>()
            .CreateSourceAsync(
                await GroundworkProviderCapabilitySnapshotBuilder.ForSelectedSourcesAsync(
                    capabilityReport,
                    topology,
                    manifestSources,
                    cancellationToken),
                providerNameNormalizer,
                cancellationToken,
                targetCompiler);
    }

    /// <summary>Explicitly applies the selected SQLite target before exercising runtime admission.</summary>
    public static async Task ApplySqliteGroundworkSchemaAsync(
        this IServiceProvider provider,
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        var source = await CreateSourceAsync(
            provider,
            SqliteGroundworkCapabilities.Runtime(),
            "sqlite-file",
            SqliteGroundworkCapabilities.PhysicalNames,
            cancellationToken);
        await using var connection = new SqliteConnection(connectionString);
        var result = await PhysicalSchemaApplication.ApplyAsync(
            source.PhysicalTarget,
            new SqlitePhysicalSchemaExecutor(connection),
            cancellationToken: cancellationToken);
        if (result.Outcome is PhysicalSchemaApplicationOutcome.Rejected or PhysicalSchemaApplicationOutcome.AuthorizationRequired)
            throw new InvalidOperationException($"SQLite test schema application was not accepted: {result.Outcome}.");
    }

    /// <summary>Explicitly applies the selected PostgreSQL target before exercising runtime admission.</summary>
    public static async Task ApplyPostgreSqlGroundworkSchemaAsync(
        this IServiceProvider provider,
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        var source = await CreateSourceAsync(
            provider,
            PostgreSqlGroundworkCapabilities.Runtime(),
            "postgresql-server",
            PostgreSqlGroundworkCapabilities.PhysicalNames,
            cancellationToken);
        var result = await PhysicalSchemaApplication.ApplyAsync(
            source.PhysicalTarget,
            new PostgreSqlPhysicalSchemaExecutor(connectionString),
            cancellationToken: cancellationToken);
        if (result.Outcome is PhysicalSchemaApplicationOutcome.Rejected or PhysicalSchemaApplicationOutcome.AuthorizationRequired)
            throw new InvalidOperationException($"PostgreSQL test schema application was not accepted: {result.Outcome}.");
    }

    /// <summary>Runs all registered shell initializers so the Groundwork document store is materialized and usable.</summary>
    public static async Task InitializeGroundworkStoreAsync(this IServiceProvider provider, CancellationToken cancellationToken = default)
    {
        foreach (var initializer in provider.GetServices<IShellInitializer>())
            await initializer.InitializeAsync(cancellationToken);
    }

    private static async ValueTask<GroundworkPhysicalSchemaManifestSource> CreateSourceAsync(
        IServiceProvider provider,
        global::Groundwork.Core.Capabilities.ProviderCapabilityReport capabilityReport,
        string topologyIdentity,
        IProviderPhysicalNameNormalizer providerNameNormalizer,
        CancellationToken cancellationToken)
    {
        await using var scope = provider.CreateAsyncScope();
        var topology = new GroundworkProviderTopologySnapshot(
            capabilityReport.Provider.Name,
            topologyIdentity,
            new HashSet<string>(StringComparer.Ordinal)
            {
                RuntimeGroundworkStorageManifestSource.MultiDocumentTransactionsTopologyIdentity
            });
        return await scope.ServiceProvider
            .GetRequiredService<GroundworkStorageCompositionFactory>()
            .CreateSourceAsync(
                await GroundworkProviderCapabilitySnapshotBuilder.ForSelectedSourcesAsync(
                    capabilityReport,
                    topology,
                    scope.ServiceProvider.GetServices<IGroundworkStorageManifestSource>(),
                    cancellationToken),
                providerNameNormalizer,
                cancellationToken);
    }
}
