using CShells.Lifecycle;
using Elsa.Persistence.Groundwork.Unified.Composition;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.SchemaEvolution;
using Groundwork.PostgreSql;
using Groundwork.PostgreSql.PhysicalStorage;
using Groundwork.Sqlite;
using Groundwork.Sqlite.PhysicalStorage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

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
        return await scope.ServiceProvider
            .GetRequiredService<GroundworkStorageCompositionFactory>()
            .CreateSourceAsync(
                new GroundworkProviderCapabilitySnapshot(
                    capabilityReport,
                    new GroundworkProviderTopologySnapshot(
                        capabilityReport.Provider.Name,
                        topologyIdentity,
                        new HashSet<string>(StringComparer.Ordinal)),
                    []),
                providerNameNormalizer,
                cancellationToken);
    }
}
