using Elsa.Activities.Design.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Persistence.Groundwork.Sqlite;
using Elsa.Persistence.Groundwork.Unified.Composition;
using Elsa.Secrets.Persistence.Groundwork;
using Elsa.Workflows.Design.Persistence.Groundwork;
using Elsa.Workflows.Publishing.Persistence.Groundwork;
using Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork;
using Groundwork.Core.SchemaEvolution;
using Groundwork.Sqlite;
using Groundwork.Sqlite.PhysicalStorage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

/// <summary>
/// Covers the spec-133 skip-if-current fast path: the Elsa-owned applied-plan stamp store and the
/// <see cref="GroundworkAdmissionSkipStamp"/> equivalence used to decide whether the full inspection walk
/// can be skipped. The crash-safety invariant is pinned: a crash between applying schema and writing the
/// stamp leaves no stamp, so the next boot re-walks and re-admits idempotently — the stamp is never a
/// correctness gate.
/// </summary>
public sealed class GroundworkAdmissionSkipStampTests
{
    private readonly SqliteGroundworkAdmissionStampStore _store = new();

    private static IReadOnlyList<IGroundworkStorageManifestSource> ReferenceDeploymentSources() =>
    [
        // The design and publishing catalogs declare their v2 storage units directly and contribute no
        // composed declaration, so the reference deployment admits these two.
        new RuntimeGroundworkStorageManifestSource(),
        new GroundworkDesignAtomicWriteStorageManifestSource()
    ];

    private static GroundworkAdmissionSkipStamp Stamp(
        string target = "t", string composition = "c", string providerVersion = "1.0", int format = GroundworkAdmissionSkipStamp.CurrentFormatVersion) =>
        new(format, target, composition, providerVersion);

    [Fact]
    public void Covers_is_true_only_when_every_component_matches()
    {
        var current = Stamp();
        Assert.True(Stamp().Covers(current));
        Assert.False(Stamp(target: "other").Covers(current));
        Assert.False(Stamp(composition: "other").Covers(current));
        Assert.False(Stamp(providerVersion: "2.0").Covers(current));
        Assert.False(Stamp(format: GroundworkAdmissionSkipStamp.CurrentFormatVersion + 1).Covers(current));
    }

    [Fact]
    public async Task Reading_a_fresh_database_returns_no_stamp()
    {
        await WithDatabaseAsync(async connection =>
        {
            var stamp = await _store.TryReadAsync(connection, "manifest", "sqlite", CancellationToken.None);
            Assert.Null(stamp);
        });
    }

    [Fact]
    public async Task Stamp_round_trips_and_is_upserted_in_place()
    {
        await WithDatabaseAsync(async connection =>
        {
            var first = Stamp(target: "fp-1");
            await _store.WriteAsync(connection, "manifest", "sqlite", first, CancellationToken.None);
            Assert.Equal(first, await _store.TryReadAsync(connection, "manifest", "sqlite", CancellationToken.None));

            // A second write for the same target replaces the row rather than duplicating it.
            var second = Stamp(target: "fp-2");
            await _store.WriteAsync(connection, "manifest", "sqlite", second, CancellationToken.None);
            Assert.Equal(second, await _store.TryReadAsync(connection, "manifest", "sqlite", CancellationToken.None));

            // A different physical target (provider) is stored independently.
            Assert.Null(await _store.TryReadAsync(connection, "manifest", "postgres", CancellationToken.None));
        });
    }

    [Fact]
    public async Task Crash_between_apply_and_stamp_write_leaves_no_stamp_and_the_next_boot_re_walks()
    {
        var databasePath = Path.Join(Path.GetTempPath(), $"elsa-skip-stamp-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={databasePath}";
        try
        {
            var source = await CreateSourceAsync();
            var current = GroundworkAdmissionSkipStamp.ForSource(source);
            var manifestId = source.PhysicalTarget.ManifestIdentity.Value;
            var providerName = source.PhysicalTarget.Provider.Name;

            // 1. First boot applies the schema (this commits durably) but "crashes" before stamping.
            await using (var connection = SqliteConnectionFactory.Create(connectionString))
            {
                var admission = await source.InspectRuntimeAdmissionAsync(
                    new SqlitePhysicalSchemaExecutor(connection),
                    new GroundworkRuntimeSchemaAdmissionOptions { AutoApplyOnStartup = true });
                Assert.True(admission.IsReady);
                Assert.True(admission.AppliedOperationCount > 0);
                // No stamp write here — simulate a crash between apply and stamp.
            }

            // 2. Crash-safety invariant: no stamp persisted, so a skip is impossible and the next boot
            //    must re-walk (and re-admit idempotently as a no-op).
            await using (var connection = SqliteConnectionFactory.Create(connectionString))
            {
                Assert.Null(await _store.TryReadAsync(connection, manifestId, providerName, CancellationToken.None));

                var reWalk = await source.InspectRuntimeAdmissionAsync(
                    new SqlitePhysicalSchemaExecutor(connection),
                    new GroundworkRuntimeSchemaAdmissionOptions { AutoApplyOnStartup = true });
                Assert.True(reWalk.IsReady);
                Assert.Equal(0, reWalk.AppliedOperationCount);

                // A ready re-walk now records the stamp (as the initializer does after a ready admission).
                await _store.WriteAsync(connection, manifestId, providerName, current, CancellationToken.None);
            }

            // 3. Warm boot: the persisted stamp covers the current plan, so the walk is skipped.
            await using (var connection = SqliteConnectionFactory.Create(connectionString))
            {
                var persisted = await _store.TryReadAsync(connection, manifestId, providerName, CancellationToken.None);
                Assert.NotNull(persisted);
                Assert.True(persisted!.Covers(current));
            }

            // 4. A plan change (different composition) no longer covers: the boot falls through to the walk.
            var changedPlan = current with { CompositionFingerprint = current.CompositionFingerprint + "-changed" };
            await using (var connection = SqliteConnectionFactory.Create(connectionString))
            {
                var persisted = await _store.TryReadAsync(connection, manifestId, providerName, CancellationToken.None);
                Assert.NotNull(persisted);
                Assert.False(persisted!.Covers(changedPlan));
            }
        }
        finally
        {
            if (File.Exists(databasePath))
                File.Delete(databasePath);
        }
    }

    private static async Task WithDatabaseAsync(Func<SqliteConnection, Task> action)
    {
        var databasePath = Path.Join(Path.GetTempPath(), $"elsa-skip-stamp-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={databasePath}";
        try
        {
            await using var connection = SqliteConnectionFactory.Create(connectionString);
            await action(connection);
        }
        finally
        {
            if (File.Exists(databasePath))
                File.Delete(databasePath);
        }
    }

    private static ValueTask<GroundworkPhysicalSchemaManifestSource> CreateSourceAsync()
    {
        var topology = new GroundworkProviderTopologySnapshot(
            SqliteGroundworkCapabilities.Provider.Name,
            "sqlite-file",
            new HashSet<string>(StringComparer.Ordinal)
            {
                RuntimeGroundworkStorageManifestSource.MultiDocumentTransactionsTopologyIdentity
            });
        return GroundworkStoreInitialization.CreatePhysicalSchemaSourceAsync(
            SqliteGroundworkCapabilities.Runtime(),
            topology,
            SqliteGroundworkCapabilities.PhysicalNames,
            ReferenceDeploymentSources());
    }
}
