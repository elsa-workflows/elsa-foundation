using System.Data.Common;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>Additional physical retention controls; prepared separately from the frozen remote baseline.</summary>
public sealed partial class StimulusIngressSourceStoreTests
{
    [Fact]
    public async Task Canonical_source_retains_the_full_material_closure_without_a_publishing_execution_or_reference()
    {
        await using var fixture = await Fixture.Create();
        var dependency = Material(Pin with { ArtifactId = "dependency", ArtifactHash = "dependency-hash" });
        await using (var context = fixture.Open())
        {
            await fixture.Material(context).SaveBatchAsync([dependency, Material(Pin, dependency)]);
            await fixture.SourceStore(context).InstallAsync(Source("source", null, true));
            Assert.Empty(await context.WorkflowExecutionStates.AsNoTracking().ToArrayAsync());
            Assert.Empty(await context.WorkflowExecutableSourceReferences.AsNoTracking().ToArrayAsync());
        }
        await using var reopened = fixture.Open();
        var artifacts = fixture.Material(reopened);
        foreach (var artifactId in new[] { Pin.ArtifactId, dependency.Identity.ArtifactId })
        {
            Assert.Null(await artifacts.TryBeginDeletionAsync(artifactId, "collection", Now.AddMinutes(1), Now));
            Assert.False(await artifacts.DeleteAsync(artifactId));
            Assert.NotNull(await artifacts.FindAsync(artifactId));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Contradictory_canonical_retention_evidence_refuses_collection_instead_of_hiding_a_source(bool corruptScopeProjection)
    {
        await using var fixture = await Fixture.Create();
        await using (var context = fixture.Open())
        {
            await fixture.Material(context).SaveAsync(Material());
            await fixture.SourceStore(context).InstallAsync(Source("source", null, true));
            var row = await context.StimulusIngressSources.SingleAsync();
            if (corruptScopeProjection) row.ScopeKey = EfRelationalIdentity.Encode("other-scope");
            else row.SourceFingerprint = new string('0', 64);
            await context.SaveChangesAsync();
        }
        await using var reopened = fixture.Open();
        var artifacts = fixture.Material(reopened);
        await Assert.ThrowsAsync<InvalidDataException>(() => artifacts.TryBeginDeletionAsync(Pin.ArtifactId, "collection", Now.AddMinutes(1), Now).AsTask());
        await Assert.ThrowsAsync<InvalidDataException>(() => artifacts.DeleteAsync(Pin.ArtifactId).AsTask());
        Assert.NotNull(await artifacts.FindAsync(Pin.ArtifactId));
        Assert.Single(await reopened.StimulusIngressSources.AsNoTracking().ToArrayAsync());
    }

    [Fact]
    public async Task Deletion_before_install_leaves_a_sticky_tombstone_and_no_accepted_source()
    {
        await using var fixture = await Fixture.Create();
        var source = Source("source", null, true);
        var executionId = Assert.Single(source.GetExecutionDependencies());
        await using (var context = fixture.Open())
        {
            await fixture.Material(context).SaveAsync(Material());
            await fixture.Executions(context).SaveAsync(Execution(executionId));
            Assert.True(await fixture.Executions(context).DeleteAsync(executionId));
        }
        await using var reopened = fixture.Open();
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.SourceStore(reopened).InstallAsync(source).AsTask());
        Assert.Empty(await reopened.StimulusIngressSources.AsNoTracking().ToArrayAsync());
        Assert.Null(await fixture.Executions(reopened).FindAsync(executionId));
        Assert.True(await fixture.Executions(reopened).IsDeletionTombstonedAsync(executionId));
    }

    [Fact]
    public async Task An_empty_committed_winner_is_reused_without_acquiring_a_losing_candidates_missing_material()
    {
        await using var fixture = await Fixture.Create();
        await using (var context = fixture.Open())
            await fixture.SourceStore(context).InstallAsync(Source("first", "key", false));
        await using var reopened = fixture.Open();
        var winner = await fixture.SourceStore(reopened).InstallAsync(Source("loser", "key", true));
        Assert.Equal("first", winner.SourceId);
        Assert.Empty(winner.Cohort.Starts);
        Assert.Empty(await reopened.ExecutionLivenessStates.AsNoTracking().ToArrayAsync());
        Assert.Null(await fixture.Material(reopened).FindAsync(Pin.ArtifactId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_complete_looking_grant_set_cannot_replace_actual_provider_lease_authority(bool manufactureMatchingGrant)
    {
        await using var fixture = await Fixture.Create();
        await using var context = fixture.Open();
        await fixture.Material(context).SaveAsync(Material());
        var manager = new UnbackedRootManager(manufactureMatchingGrant);
        var store = fixture.SourceStore(context, manager);
        if (manufactureMatchingGrant)
            await Assert.ThrowsAsync<WorkflowExecutableRootWriteLeaseLostException>(() => store.InstallAsync(Source("source", null, true)).AsTask());
        else
            await Assert.ThrowsAsync<InvalidDataException>(() => store.InstallAsync(Source("source", null, true)).AsTask());
        Assert.True(manager.CallbackInvoked);
        Assert.Empty(await context.StimulusIngressSources.AsNoTracking().ToArrayAsync());
        Assert.Empty(await context.ExecutionLivenessStates.AsNoTracking().ToArrayAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_peer_commit_at_transaction_entry_preserves_the_first_selection_or_reports_content_conflict(bool conflict)
    {
        await using var fixture = await Fixture.Create();
        await using (var seed = fixture.Open()) await fixture.Material(seed).SaveAsync(Material());
        var peer = Source("winner", "key", true, conflict ? "different-request" : "hash");
        var interleave = new AtTransactionEntry
        {
            Action = async () =>
            {
                // The losing context has completed its optimistic read but has not entered a
                // database transaction. The peer commits through the real source/lease stores.
                await using var other = fixture.Open();
                await fixture.SourceStore(other).InstallAsync(peer);
            }
        };
        await using (var context = fixture.Open(interleave))
        {
            var proposal = Source("loser", "key", false);
            if (conflict)
                await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.SourceStore(context).InstallAsync(proposal).AsTask());
            else
                Assert.Equal("winner", (await fixture.SourceStore(context).InstallAsync(proposal)).SourceId);
        }
        Assert.Equal(1, interleave.Invocations);
        await using var reopened = fixture.Open();
        var saved = (await fixture.SourceStore(reopened).FindByKeyAsync(Origin, "key"))!;
        Assert.Equal("winner", saved.SourceId);
        Assert.Equal(peer.RequestFingerprint, saved.RequestFingerprint);
        Assert.Equal(peer.Cohort.Fingerprint, saved.Cohort.Fingerprint);
        Assert.Single(saved.Cohort.Starts);
        Assert.Single(await reopened.StimulusIngressSources.AsNoTracking().ToArrayAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Constructed_provider_failure_is_acceptance_only_when_the_original_winner_committed(bool afterCommit)
    {
        await using var fixture = await Fixture.Create();
        await using var context = fixture.Open();
        await fixture.Material(context).SaveAsync(Material());
        var manager = new LoseAcknowledgement(fixture.RootManager(context), afterCommit);
        var proposal = Source("source", "key", true);
        if (afterCommit)
        {
            var accepted = await fixture.SourceStore(context, manager).InstallAsync(proposal);
            Assert.Equal(proposal.SourceId, accepted.SourceId);
            Assert.Equal(proposal.Cohort.Fingerprint, accepted.Cohort.Fingerprint);
        }
        else
            await Assert.ThrowsAsync<SqliteException>(() => fixture.SourceStore(context, manager).InstallAsync(proposal).AsTask());
        Assert.True(manager.FailureInjected);
        await using var reopened = fixture.Open();
        var stored = await fixture.SourceStore(reopened).FindByKeyAsync(Origin, "key");
        if (afterCommit)
        {
            Assert.Equal(proposal.Cohort.Fingerprint, stored!.Cohort.Fingerprint);
            Assert.Single(await reopened.StimulusIngressSources.AsNoTracking().ToArrayAsync());
            Assert.Single(await reopened.ExecutionLivenessStates.AsNoTracking().ToArrayAsync());
        }
        else
        {
            Assert.Null(stored);
            Assert.Empty(await reopened.StimulusIngressSources.AsNoTracking().ToArrayAsync());
            Assert.Empty(await reopened.ExecutionLivenessStates.AsNoTracking().ToArrayAsync());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Shared_header_codec_preserves_the_original_EF_wire_with_empty_or_populated_cohorts(bool withStart)
    {
        var source = Source("source-\ud800", "key-\ud801", withStart);
        var originalWire = Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.RuntimeArtifactJson.Serialize(source);
        var sharedHeader = StimulusIngressSourceCodec.Encode(source);
        Assert.Equal(originalWire, sharedHeader.GetRawText());
        Assert.Equal(originalWire, Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.StimulusIngressJson.Write(source));
        using var original = System.Text.Json.JsonDocument.Parse(originalWire);
        var decoded = StimulusIngressSourceCodec.Decode(original.RootElement);
        Assert.Equal(source.SourceId, decoded.SourceId);
        Assert.Equal(source.ClientKey, decoded.ClientKey);
        Assert.Equal(RuntimeCheckpointCommitFingerprint.ComputeIngressSource(source), RuntimeCheckpointCommitFingerprint.ComputeIngressSource(decoded));
    }

    private sealed class AtTransactionEntry : DbTransactionInterceptor
    {
        public Func<Task>? Action { get; init; }
        public int Invocations { get; private set; }
        public override async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(DbConnection connection,
            TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result, CancellationToken cancellationToken = default)
        {
            if (Invocations == 0 && Action is { } action)
            {
                Invocations++;
                await action();
            }
            return result;
        }
    }

    private sealed class LoseAcknowledgement(IWorkflowExecutableRootWriteLeaseManager inner, bool afterCommit) : IWorkflowExecutableRootWriteLeaseManager
    {
        public bool FailureInjected { get; private set; }
        public ValueTask ExecuteAsync(string artifactId, string leaseId, Func<CancellationToken, ValueTask> write,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public async ValueTask ExecuteFencedAsync(IReadOnlyCollection<WorkflowExecutableIdentity> roots, string leaseId,
            Func<IReadOnlyList<WorkflowExecutableRootWriteLease>, CancellationToken, ValueTask> write,
            CancellationToken cancellationToken = default)
        {
            if (afterCommit) await inner.ExecuteFencedAsync(roots, leaseId, write, cancellationToken);
            FailureInjected = true;
            throw new SqliteException("Constructed provider failure at the selected source-commit boundary.", 5);
        }
    }

    private sealed class UnbackedRootManager(bool manufactureMatchingGrant) : IWorkflowExecutableRootWriteLeaseManager
    {
        public bool CallbackInvoked { get; private set; }
        public ValueTask ExecuteAsync(string artifactId, string leaseId, Func<CancellationToken, ValueTask> write,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask ExecuteFencedAsync(IReadOnlyCollection<WorkflowExecutableIdentity> roots, string leaseId,
            Func<IReadOnlyList<WorkflowExecutableRootWriteLease>, CancellationToken, ValueTask> write,
            CancellationToken cancellationToken = default)
        {
            CallbackInvoked = true;
            IReadOnlyList<WorkflowExecutableRootWriteLease> grants = manufactureMatchingGrant
                ? roots.Select(root => new WorkflowExecutableRootWriteLease(root.ArtifactId, leaseId, "not-a-provider-grant")).ToArray()
                : [];
            return write(grants, cancellationToken);
        }
    }
}
