using System.Reflection;
using Elsa.Agent.Specialists.Core.Models;
using Elsa.Agent.Specialists.Services;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Agent.Specialists.Tests.Runtime;

public sealed partial class SpecialistNativeTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Unrelated_checkpoints_do_not_read_workflow_or_activity_stores_and_reuse_the_pinned_burst_artifact(bool cancel, bool burst)
    {
        await using var provider = await ServicesAsync(new FixtureFactory("{\"Article\":\"unused\"}"));
        var specialist = await ExecutableAsync(provider);
        await StartAsync(provider, specialist);
        var workflow = (await provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("run"))!;
        var parent = (await provider.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync("run")).Single(state => state.ParentActivityExecutionId is null);
        var root = specialist.RootActivity;
        var ordinaryRoot = new ExecutableNode(root.ExecutableNodeId, root.AuthoredActivityId, "ordinary", "1",
            root.Descriptor, root.InputBindings, root.Metadata, root.ChildSlots, root.Structure, activityContract: null);
        var ordinary = new WorkflowExecutable(specialist.Identity with { ArtifactId = "ordinary", ArtifactHash = "ordinary-hash" }, ordinaryRoot,
            specialist.ResumeTargets, DateTimeOffset.UtcNow, new Dictionary<string, string>(), specialist.IncidentStrategy);
        var store = DispatchProxy.Create<IWorkflowExecutableStore, CountingCheckpointExecutableStore>();
        var counting = (CountingCheckpointExecutableStore)(object)store;
        counting.Executable = ordinary;
        var accessor = provider.GetRequiredService<IWorkflowBurstScopeAccessor>();
        await using var scope = new WorkflowBurstScope("run");
        using var ambient = accessor.Push(burst ? scope : null);
        var reader = new BurstCachedWorkflowExecutableReader(store, accessor);
        if (burst) _ = await reader.FindAsync(ordinary.Identity.ArtifactId); // The drain already loaded its immutable executable.
        counting.Reads = 0;
        counting.ForbidReads = burst;
        var workflows = DispatchProxy.Create<IWorkflowExecutionStateStore, ForbiddenCheckpointStore>();
        var activities = DispatchProxy.Create<IActivityExecutionStateStore, ForbiddenCheckpointStore>();
        var boundary = new SpecialistSettlementBoundary(workflows, activities, store, provider.GetRequiredService<IPersistenceAccessContextAccessor>());
        var enricher = new SpecialistCheckpointEnricher(boundary, activities, reader);
        var now = DateTimeOffset.UtcNow;
        parent = parent with { Status = cancel ? ActivityExecutionStatus.Cancelled : ActivityExecutionStatus.Completed, CompletedAt = now };
        workflow = workflow with { PinnedExecutable = ordinary.Identity, Status = WorkflowExecutionStatus.Cancelled, CompletedAt = now };
        var changes = new RuntimeCheckpointStateChangeSet(cancel ? new("run", RuntimeStateChangeOperation.Upsert, workflow, new Dictionary<string, string>()) : null,
            null, [new(parent.InvocationId, RuntimeStateChangeOperation.Upsert, parent, new Dictionary<string, string>())], [], [], [], []);
        var pin = new Dictionary<string, string> { [RuntimeMetadataKeys.ExecutableArtifactId] = ordinary.Identity.ArtifactId,
            [RuntimeMetadataKeys.ExecutableArtifactHash] = ordinary.Identity.ArtifactHash };
        var commit = new RuntimeCheckpointCommit("ordinary-commit", new("ordinary-checkpoint",
            cancel ? RuntimeCheckpointNames.ActivityCancelled : RuntimeCheckpointNames.ActivityCompleted, "run", now, [parent.InvocationId], pin), changes, [], new Dictionary<string, string>());
        var enriched = await enricher.EnrichAsync(commit);
        await enricher.ValidateFinalAsync(enriched);
        await enricher.ValidateFinalAsync(enriched); // Validation cannot acquire a second I/O failure mode.
        Assert.Empty(enriched.PostCommitIntents);
        Assert.Equal(burst ? 0 : 1, counting.Reads);
        Assert.Equal(0, ((ForbiddenCheckpointStore)(object)workflows).Calls);
        Assert.Equal(0, ((ForbiddenCheckpointStore)(object)activities).Calls);
        var injected = new RuntimePostCommitIntent("forged", "run", SpecialistSettlementBoundary.CancelKind, now, parent.InvocationId, null, null);
        await Assert.ThrowsAsync<SpecialistTurnException>(() => enricher.ValidateFinalAsync(enriched with { PostCommitIntents = [injected] }).AsTask());
        var altered = new RuntimeCheckpointStateChangeSet(null, null, [], [], [], [], []);
        await Assert.ThrowsAsync<SpecialistTurnException>(() => enricher.ValidateFinalAsync(enriched with { StateChanges = altered }).AsTask());
    }

    public class ForbiddenCheckpointStore : DispatchProxy
    {
        public int Calls { get; private set; }
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Calls++;
            throw new InvalidOperationException("An unrelated checkpoint must not consult the native state stores.");
        }
    }

    public class CountingCheckpointExecutableStore : DispatchProxy
    {
        public WorkflowExecutable Executable { get; set; } = null!;
        public int Reads { get; set; }
        public bool ForbidReads { get; set; }
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Assert.Equal(nameof(IWorkflowExecutableStore.FindAsync), targetMethod!.Name);
            Reads++;
            if (ForbidReads) throw new InvalidOperationException("The drain already loaded its pinned executable.");
            Assert.Equal(Executable.Identity.ArtifactId, args![0]);
            return ValueTask.FromResult<WorkflowExecutable?>(Executable);
        }
    }
}
