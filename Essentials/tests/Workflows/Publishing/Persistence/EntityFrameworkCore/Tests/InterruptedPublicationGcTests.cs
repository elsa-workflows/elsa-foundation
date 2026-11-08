using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Configuration;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Executables;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// Qualifies the native publish save-before-reference window against EF reference GC on SQLite.
/// Does not change retention, enrollment, or publish fencing.
/// </summary>
public sealed class InterruptedPublicationGcTests : IAsyncLifetime
{
    private readonly ITestOutputHelper output;
    private SqliteTestDatabase publishing = null!;
    private SqliteTestDatabase runtime = null!;

    public InterruptedPublicationGcTests(ITestOutputHelper output) => this.output = output;

    public async Task InitializeAsync()
    {
        publishing = await SqliteTestDatabase.CreateAsync<PublishingSnapshotReviewSqliteDbContext>(options => new(options));
        runtime = await SqliteTestDatabase.CreateAsync<RuntimeSqliteDbContext>(options => new(options));
    }

    public async Task DisposeAsync()
    {
        await publishing.DisposeAsync();
        await runtime.DisposeAsync();
    }

    [Fact]
    public async Task Native_publish_interrupted_after_artifact_save_is_classified_against_host_grace_gc()
    {
        var databases = new JournalDatabases(PublishingNativeProvider.Sqlite, publishing.ConnectionString, runtime.ConnectionString);
        var grace = new WorkflowExecutableGarbageCollectionOptions().ArtifactCreationGracePeriod;
        var pumpSweep = TimeSpan.FromMinutes(5);
        var pumpMaxBackoff = TimeSpan.FromMinutes(30);
        Assert.Equal(TimeSpan.FromMinutes(5), grace);

        InterruptedPublicationGcWitness before;
        InterruptedPublicationGcWitness afterGc;
        WorkflowExecutableReferenceSweepResult sweep;
        DateTimeOffset createdAt;
        string artifactId;
        await using (var interrupted = new PublicationJournalConvergence.PublishingNode(
            databases, wrapExecutables: inner => new InterruptAfterNativeSaveStore(inner)))
        {
            var error = await Assert.ThrowsAsync<PublicationInterruptedAfterArtifactSaveException>(
                () => interrupted.PublishAsync("version-1"));
            Assert.Contains("after the artifact save", error.Message);

            var artifacts = (await interrupted.Executables.ListAllAsync()).ToArray();
            var saved = Assert.Single(artifacts);
            artifactId = saved.Identity.ArtifactId;
            createdAt = saved.CreatedAt;
            before = await CaptureNativeAsync(interrupted, artifactId);

            var collector = CreateCollector(interrupted, grace);
            var sweepNow = createdAt + grace + TimeSpan.FromMinutes(1);
            sweep = await collector.SweepAsync(sweepNow);
            afterGc = await CaptureNativeAsync(interrupted, artifactId);

            output.WriteLine(
                "native interrupted-publication GC: grace={0} pumpSweep={1} pumpMaxBackoff={2} createdAt={3:o} sweepNow={4:o} deletedArtifacts={5} deletedReferences={6}",
                grace, pumpSweep, pumpMaxBackoff, createdAt, sweepNow, sweep.DeletedArtifactCount, sweep.DeletedReferenceCount);
            output.WriteLine("before GC: {0}", before.Describe());
            output.WriteLine("after GC: {0}", afterGc.Describe());
        }

        Assert.True(before.ArtifactPresent, "The interrupt must land after the native artifact is persisted.");
        Assert.Empty(before.LiveSourceReferenceIds);
        Assert.Empty(before.Journal);
        Assert.Null(before.SlotPublicationId);

        string? retryPublicationId = null;
        Exception? retryError = null;
        InterruptedPublicationGcWitness afterRetry;
        await using (var retry = new PublicationJournalConvergence.PublishingNode(databases))
        {
            try
            {
                retryPublicationId = (await retry.PublishAsync("version-1")).PublicationId;
            }
            catch (Exception exception)
            {
                retryError = exception;
            }

            afterRetry = await CaptureNativeAsync(retry, artifactId);
            output.WriteLine("after retry: {0} retryError={1} retryPublicationId={2}", afterRetry.Describe(), retryError?.GetType().FullName, retryPublicationId);
            var classification = Classify(afterGc, afterRetry, retryError, retryPublicationId);
            output.WriteLine("classification={0}", classification);
            Assert.Equal(InterruptedPublicationGcOutcome.LossSafelyRefusedOrRetryable, classification);
            if (retryError is null)
            {
                Assert.False(string.IsNullOrWhiteSpace(retryPublicationId));
                Assert.True(afterRetry.ArtifactPresent);
                Assert.Contains(artifactId, afterRetry.LiveSourceReferenceArtifactIds);
            }
        }

        Assert.Equal(0, sweep.DeletedReferenceCount);
    }

    private static WorkflowExecutableReferenceGarbageCollector CreateCollector(
        PublicationJournalConvergence.PublishingNode node,
        TimeSpan grace) =>
        new(
            node.Executables,
            node.References,
            new InMemoryExecutableActivityTemplateStore(),
            new EfWorkflowExecutionStateStore(node.Runtime, node.Access, ActivityPublicationScope.RecoveryCodec),
            null,
            Options.Create(new WorkflowExecutableGarbageCollectionOptions { ArtifactCreationGracePeriod = grace }),
            TimeProvider.System,
            NullLogger<WorkflowExecutableReferenceGarbageCollector>.Instance);

    private static async Task<InterruptedPublicationGcWitness> CaptureNativeAsync(
        PublicationJournalConvergence.PublishingNode node,
        string artifactId)
    {
        var artifact = await node.Executables.FindAsync(artifactId);
        var references = await node.References.ListAllAsync();
        var journal = await node.JournalAsync();
        return new InterruptedPublicationGcWitness(
            artifact is not null,
            references.Select(reference => reference.SourceReferenceId).ToArray(),
            references.Select(reference => reference.ArtifactId).ToArray(),
            journal.Select(record => $"{record.PublicationId}:{record.Status}:{record.ArtifactId}").ToArray(),
            await node.SlotPublicationAsync());
    }

    private static InterruptedPublicationGcOutcome Classify(
        InterruptedPublicationGcWitness afterGc,
        InterruptedPublicationGcWitness afterRetry,
        Exception? retryError,
        string? retryPublicationId)
    {
        if (HasUnsafeCommit(afterGc) || HasUnsafeCommit(afterRetry))
            return InterruptedPublicationGcOutcome.UnsafeCommit;
        if (retryError is null && string.IsNullOrWhiteSpace(retryPublicationId))
            return InterruptedPublicationGcOutcome.UnsafeCommit;
        return InterruptedPublicationGcOutcome.LossSafelyRefusedOrRetryable;
    }

    private static bool HasUnsafeCommit(InterruptedPublicationGcWitness witness)
    {
        if (!witness.ArtifactPresent && witness.LiveSourceReferenceArtifactIds.Count > 0)
            return true;
        if (!witness.ArtifactPresent && witness.Journal.Any(row => row.Contains($":{PublicationStatus.Active}:", StringComparison.Ordinal)))
            return true;
        return !witness.ArtifactPresent && witness.SlotPublicationId is not null;
    }

    private sealed class InterruptAfterNativeSaveStore(IWorkflowExecutableStore inner) : WorkflowExecutableStoreDecorator(inner)
    {
        public override async ValueTask SaveAsync(WorkflowExecutable executable, CancellationToken cancellationToken = default)
        {
            await Inner.SaveAsync(executable, cancellationToken);
            throw new PublicationInterruptedAfterArtifactSaveException();
        }
    }
}

internal sealed record InterruptedPublicationGcWitness(
    bool ArtifactPresent,
    IReadOnlyList<string> LiveSourceReferenceIds,
    IReadOnlyList<string> LiveSourceReferenceArtifactIds,
    IReadOnlyList<string> Journal,
    string? SlotPublicationId)
{
    public string Describe() =>
        $"ArtifactPresent={ArtifactPresent}; liveRefs=[{string.Join(',', LiveSourceReferenceIds)}]; liveRefArtifacts=[{string.Join(',', LiveSourceReferenceArtifactIds)}]; journal=[{string.Join(',', Journal)}]; slot={SlotPublicationId}";
}

internal enum InterruptedPublicationGcOutcome
{
    LossSafelyRefusedOrRetryable = 1,
    UnsafeCommit = 2
}

internal sealed class PublicationInterruptedAfterArtifactSaveException()
    : Exception("Publication was interrupted after the artifact save and before the retention root.");
