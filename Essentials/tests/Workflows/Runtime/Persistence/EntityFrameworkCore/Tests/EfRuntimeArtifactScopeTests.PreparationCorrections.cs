using System.Data.Common;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class EfRuntimeArtifactScopeTests
{
    [Theory]
    [InlineData(false, "valid")]
    [InlineData(true, "valid")]
    [InlineData(false, "changed-marker")]
    [InlineData(true, "changed-marker")]
    [InlineData(false, "changed-snapshot")]
    [InlineData(true, "changed-snapshot")]
    [InlineData(false, "restored-row")]
    [InlineData(true, "restored-row")]
    public async Task Scoped_doomed_deletes_accept_only_the_checked_persisted_snapshot(bool guarded, string scenario)
    {
        await using var fixture = await PreparationGateFixture.CreateAsync();
        await fixture.FinalizeArtifactAsync(RuntimeArtifactEfModule.ScopedPublicationSchemaVersion);
        var live = ScopedPredecessorReference();
        await fixture.Executables.SaveAsync(Executable(live.ArtifactId));
        await SeedScopedPredecessorAsync(fixture, live);
        var now = DateTimeOffset.UtcNow;
        var retired = live.Retire(now, "cleanup");
        Assert.True(await fixture.Sources.TryRetireAsync(live, retired));
        var guard = Assert.IsType<WorkflowExecutableDeletionGuard>(await fixture.Executables.TryBeginDeletionAsync(
            live.ArtifactId, "scope-cleanup", now.AddMinutes(1), now));
        var expected = scenario switch
        {
            "changed-marker" => retired with { PublicationScope = new("other-physical-scope") },
            "changed-snapshot" => retired with { DeletedReason = "other-retirement" },
            _ => retired
        };
        if (scenario == "restored-row") Assert.True(await fixture.Sources.TryRestoreAsync(retired, live));

        var deleted = guarded
            ? await fixture.Sources.TryDeleteDoomedAsync(expected, now, guard)
            : await fixture.Sources.TryDeleteDoomedAsync(expected, now);
        Assert.Equal(scenario == "valid", deleted);
        if (scenario == "valid") Assert.Null(await fixture.Sources.FindAsync(live.SourceReferenceId));
        else
        {
            var remaining = (await fixture.Sources.FindAsync(live.SourceReferenceId))!;
            Assert.True(WorkflowExecutableSourceReferenceComparer.SameSnapshot(
                scenario == "restored-row" ? live : retired, remaining));
            // A forged expected snapshot must not damage the real row or its subsequent checked delete.
            if (scenario != "restored-row")
                Assert.True(guarded
                    ? await fixture.Sources.TryDeleteDoomedAsync(retired, now, guard)
                    : await fixture.Sources.TryDeleteDoomedAsync(retired, now));
        }
        Assert.NotNull(await fixture.Executables.FindAsync(live.ArtifactId));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Scoped_doomed_deletes_keep_native_revision_and_incarnation_fences(bool guarded, bool incarnation)
    {
        var race = new ScopedDeleteTokenRace(incarnation);
        await using var fixture = await PreparationGateFixture.CreateAsync(race);
        await fixture.FinalizeArtifactAsync(RuntimeArtifactEfModule.ScopedPublicationSchemaVersion);
        var live = ScopedPredecessorReference();
        await fixture.Executables.SaveAsync(Executable(live.ArtifactId));
        await SeedScopedPredecessorAsync(fixture, live);
        var now = DateTimeOffset.UtcNow;
        var retired = live.Retire(now, "cleanup");
        Assert.True(await fixture.Sources.TryRetireAsync(live, retired));
        var guard = Assert.IsType<WorkflowExecutableDeletionGuard>(await fixture.Executables.TryBeginDeletionAsync(
            live.ArtifactId, "scope-cas-cleanup", now.AddMinutes(1), now));
        var before = await fixture.Context.WorkflowExecutableSourceReferences.AsNoTracking().SingleAsync();
        race.RowId = before.Id;
        Assert.False(guarded
            ? await fixture.Sources.TryDeleteDoomedAsync(retired, now, guard)
            : await fixture.Sources.TryDeleteDoomedAsync(retired, now));
        Assert.True(race.Changed);
        var after = await fixture.Context.WorkflowExecutableSourceReferences.AsNoTracking().SingleAsync();
        Assert.Equal(before.ContentJson, after.ContentJson);
        Assert.True(WorkflowExecutableSourceReferenceComparer.SameSnapshot(retired,
            (await fixture.Sources.FindAsync(retired.SourceReferenceId))!));
    }

    [Theory]
    [InlineData("activation-slot:10:definition:")]
    [InlineData("activation-slot:10:definition:test")]
    [InlineData("activation-slot:10:definition:0:")]
    [InlineData("activation-slot:10:definition:4:")]
    [InlineData("activation-slot:10:definition:3:test")]
    [InlineData("activation-slot:10:definition:5:test")]
    [InlineData("activation-slot:10:definition:4:test:trailing")]
    [InlineData("activation-slot:10:definition:04:test")]
    [InlineData("activation-slot:10:definition:+4:test")]
    [InlineData("activation-slot:10:definition:2147483648:test")]
    [InlineData("activation-slot:10:definition:1: ")]
    public async Task Scoped_source_reads_refuse_incomplete_or_noncanonical_slot_frames(string slotId)
    {
        await using var fixture = await PreparationGateFixture.CreateAsync();
        await fixture.FinalizeArtifactAsync(RuntimeArtifactEfModule.ScopedPublicationSchemaVersion);
        var malformed = ScopedPredecessorReference() with { SlotId = slotId };
        await SeedScopedPredecessorAsync(fixture, malformed);
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Sources.FindAsync(malformed.SourceReferenceId).AsTask());
    }

    [Theory]
    [InlineData("definition", "test:with:separators")]
    [InlineData("definition:😀", "slot:😀:end")]
    public async Task Scoped_source_slot_frames_follow_the_canonical_factory_and_UTF16_lengths(string definition, string slot)
    {
        await using var fixture = await PreparationGateFixture.CreateAsync();
        await fixture.FinalizeArtifactAsync(RuntimeArtifactEfModule.ScopedPublicationSchemaVersion);
        var value = ScopedPredecessorReference() with { DefinitionId = definition,
            SlotId = WorkflowActivationSlotIdentity.Create(definition, slot) };
        await SeedScopedPredecessorAsync(fixture, value);
        var found = (await fixture.Sources.FindAsync(value.SourceReferenceId))!;
        Assert.True(WorkflowExecutableSourceReferenceComparer.SameSnapshot(value, found));
        Assert.True(await fixture.Sources.TryRetireAsync(found, found.Retire(DateTimeOffset.UtcNow)));
    }

    [Theory]
    [InlineData("1.1.0", false)]
    [InlineData("1.1.0", true)]
    [InlineData("1.2.0", false)]
    [InlineData("1.2.0", true)]
    public async Task A_tracked_source_rewrite_confirms_a_newer_stamp_without_a_scheduled_writer_refresh(string version, bool finalized)
    {
        await using var fixture = await PreparationGateFixture.CreateAsync();
        var live = Reference("stale-gate-source", "stale-gate-artifact");
        await fixture.Sources.SaveAsync(live);
        await fixture.Context.WorkflowExecutableSourceReferences.ExecuteUpdateAsync(update => update.SetProperty(row => row.SchemaVersion, version));
        fixture.Context.ChangeTracker.Clear();
        var before = await fixture.Context.WorkflowExecutableSourceReferences.AsNoTracking().SingleAsync();
        if (finalized) await fixture.FinalizeElsewhereWithoutRefreshingWriterAsync(version);
        var retired = live.Retire(DateTimeOffset.UtcNow);
        if (finalized) Assert.True(await fixture.Sources.TryRetireAsync(live, retired));
        else await Assert.ThrowsAsync<EfSchemaWriteRefusedException>(() => fixture.Sources.TryRetireAsync(live, retired).AsTask());
        fixture.Context.ChangeTracker.Clear();
        var after = await fixture.Context.WorkflowExecutableSourceReferences.AsNoTracking().SingleAsync();
        Assert.Equal(version, after.SchemaVersion);
        Assert.Equal(before.IncarnationId, after.IncarnationId);
        Assert.Equal(before.Revision + (finalized ? 1 : 0), after.Revision);
        if (!finalized) Assert.Equal(before.ContentJson, after.ContentJson);
        Assert.Equal(finalized ? version : RuntimeArtifactEfModule.SchemaVersion,
            fixture.Gate.StateOf(RuntimeArtifactEfModule.SchemaFamily)!.WriteVersion);
    }

    [Theory]
    [InlineData("1.1.0", false)]
    [InlineData("1.1.0", true)]
    [InlineData("1.2.0", false)]
    [InlineData("1.2.0", true)]
    public async Task A_header_only_lease_update_confirms_durable_finalization_on_its_writing_context(string version, bool finalized)
    {
        await using var fixture = await PreparationGateFixture.CreateAsync();
        await fixture.Executables.SaveAsync(Executable("stale-gate-lease-artifact"));
        var now = DateTimeOffset.UtcNow;
        var lease = Assert.IsType<WorkflowExecutableRootWriteLease>(await fixture.Executables.TryAcquireRootWriteLeaseAsync(
            "stale-gate-lease-artifact", "stale-gate-lease", now.AddMinutes(5), now));
        await fixture.Context.WorkflowExecutableRootWriteLeases.ExecuteUpdateAsync(update => update.SetProperty(row => row.SchemaVersion, version));
        fixture.Context.ChangeTracker.Clear();
        var before = await fixture.Context.WorkflowExecutableRootWriteLeases.AsNoTracking().SingleAsync();
        if (finalized) await fixture.FinalizeElsewhereWithoutRefreshingWriterAsync(version);
        if (finalized) Assert.True(await fixture.Executables.RenewRootWriteLeaseAsync(lease, now.AddMinutes(6), now.AddSeconds(1)));
        else await Assert.ThrowsAsync<EfSchemaWriteRefusedException>(() => fixture.Executables.RenewRootWriteLeaseAsync(
            lease, now.AddMinutes(6), now.AddSeconds(1)).AsTask());
        var after = await fixture.Context.WorkflowExecutableRootWriteLeases.AsNoTracking().SingleAsync();
        Assert.Equal(version, after.SchemaVersion);
        Assert.Equal(before.Token, after.Token);
        Assert.Equal(before.IncarnationId, after.IncarnationId);
        Assert.Equal(before.Revision + (finalized ? 1 : 0), after.Revision);
        Assert.Equal(finalized ? now.AddMinutes(6).UtcTicks : before.ExpiresAtUtcTicks, after.ExpiresAtUtcTicks);
        Assert.Equal(finalized ? version : RuntimeArtifactEfModule.SchemaVersion,
            fixture.Gate.StateOf(RuntimeArtifactEfModule.SchemaFamily)!.WriteVersion);
    }

    [Fact]
    public async Task On_demand_raw_fence_confirmation_preserves_the_callers_transaction_and_cancellation()
    {
        await using var fixture = await PreparationGateFixture.CreateAsync();
        await fixture.Executables.SaveAsync(Executable("transactional-stale-gate"));
        var now = DateTimeOffset.UtcNow;
        var lease = Assert.IsType<WorkflowExecutableRootWriteLease>(await fixture.Executables.TryAcquireRootWriteLeaseAsync(
            "transactional-stale-gate", "transactional-lease", now.AddMinutes(5), now));
        const string version = RuntimeArtifactEfModule.ScopedPublicationSchemaVersion;
        await fixture.Context.WorkflowExecutableCoordinations.ExecuteUpdateAsync(update => update.SetProperty(row => row.SchemaVersion, version));
        await fixture.Context.WorkflowExecutableRootWriteLeases.ExecuteUpdateAsync(update => update.SetProperty(row => row.SchemaVersion, version));
        fixture.Context.ChangeTracker.Clear();
        var coordination = await fixture.Context.WorkflowExecutableCoordinations.AsNoTracking().SingleAsync();
        var before = await fixture.Context.WorkflowExecutableRootWriteLeases.AsNoTracking().SingleAsync();
        await fixture.FinalizeElsewhereWithoutRefreshingWriterAsync(version);
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        await using (var transaction = await fixture.Context.Database.BeginTransactionAsync())
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Executables.FenceRootWriteLeasesAsync(
                [lease], TimeProvider.System, stop.Token).AsTask());
            Assert.Equal(RuntimeArtifactEfModule.SchemaVersion, fixture.Gate.StateOf(RuntimeArtifactEfModule.SchemaFamily)!.WriteVersion);
            Assert.Single(await fixture.Executables.FenceRootWriteLeasesAsync([lease], TimeProvider.System, CancellationToken.None));
            Assert.Same(transaction, fixture.Context.Database.CurrentTransaction);
            Assert.Equal(version, fixture.Gate.StateOf(RuntimeArtifactEfModule.SchemaFamily)!.WriteVersion);
            await transaction.RollbackAsync();
        }
        var after = await fixture.Context.WorkflowExecutableRootWriteLeases.AsNoTracking().SingleAsync();
        Assert.Equal(before.Revision, after.Revision);
        Assert.Equal(before.SchemaVersion, after.SchemaVersion);
        Assert.Equal(before.ExpiresAtUtcTicks, after.ExpiresAtUtcTicks);
        var rolledBack = await fixture.Context.WorkflowExecutableCoordinations.AsNoTracking().SingleAsync();
        Assert.Equal(coordination.Revision, rolledBack.Revision);
        Assert.Equal(coordination.ContentJson, rolledBack.ContentJson);
    }

    private sealed class ScopedDeleteTokenRace(bool incarnation) : DbCommandInterceptor
    {
        public string? RowId { get; set; }
        public bool Changed { get; private set; }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            await ChangeTokenAsync(command, cancellationToken);
            return result;
        }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            await ChangeTokenAsync(command, cancellationToken);
            return result;
        }

        private async Task ChangeTokenAsync(DbCommand command, CancellationToken cancellationToken)
        {
            if (RowId is null || Changed || !command.CommandText.TrimStart().StartsWith("DELETE", StringComparison.OrdinalIgnoreCase) ||
                !command.CommandText.Contains(RuntimeArtifactEfModule.SourceReferenceTableName, StringComparison.Ordinal)) return;
            // Change only the row token after the checked read. This is an injected CAS control,
            // not an assertion that a separate writer ran or that provider qualification passed.
            await using var change = command.Connection!.CreateCommand();
            change.Transaction = command.Transaction;
            var assignment = incarnation ? "\"IncarnationId\" = 'replacement-incarnation'" : "\"Revision\" = \"Revision\" + 1";
            change.CommandText = $"UPDATE \"{RuntimeArtifactEfModule.SourceReferenceTableName}\" SET {assignment} WHERE \"Id\" = @id";
            var id = change.CreateParameter(); id.ParameterName = "@id"; id.Value = RowId;
            change.Parameters.Add(id);
            Changed = await change.ExecuteNonQueryAsync(cancellationToken) == 1;
        }
    }
}
