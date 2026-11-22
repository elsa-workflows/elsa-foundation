using Elsa.Workflows.Runtime.Core.Contracts;
using System.Data.Common;
using System.Text.Json;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Values;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using C = Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.RuntimeCheckpointCommitContract;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class EfRuntimeCheckpointCommitStoreTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StepState_precommit_failure_rolls_back_header_frame_outputs_identity_and_successor(bool failParticipant)
    {
        await using var database = await TestDatabase.CreateAsync();
        var commit = StepStateCommit("step-state-rollback");
        var markerFailure = new FailMarkerInsertInterceptor();
        markerFailure.Arm();
        await using (var context = database.Open("tenant-a", failParticipant ? new FailStepOutputInsert() : markerFailure))
        {
            await Assert.ThrowsAsync<DbUpdateException>(() => new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a"), new FixedTimeProvider(OccurredAt), new PassThroughRootWriteLeaseManager())
                .CommitAsync(commit, Decision()).AsTask());
            Assert.Empty(context.ChangeTracker.Entries());
        }
        await using var restarted = database.Open("tenant-a");
        Assert.Empty(await restarted.WorkflowExecutionStates.ToArrayAsync());
        Assert.Empty(await restarted.DurableValueStates.ToArrayAsync());
        Assert.Empty(await restarted.ActivityExecutionStates.ToArrayAsync());
        Assert.Empty(await restarted.RuntimePostCommitOutbox.ToArrayAsync());
        Assert.Empty(await restarted.RuntimeCheckpointCommits.ToArrayAsync());
        await new EfRuntimeCheckpointCommitStore(restarted, new FixedAccessor("tenant-a"), new FixedTimeProvider(OccurredAt), new PassThroughRootWriteLeaseManager()).CommitAsync(commit, Decision());
        await AssertStepState(restarted);
    }

    [Fact]
    public async Task StepState_postcommit_redelivery_has_one_marker_and_one_successor_after_restart()
    {
        await using var database = await TestDatabase.CreateAsync();
        var commit = StepStateCommit("step-state-replay");
        await using (var context = database.Open("tenant-a"))
            await new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a"), new FixedTimeProvider(OccurredAt), new PassThroughRootWriteLeaseManager()).CommitAsync(commit, Decision());
        await using var restarted = database.Open("tenant-a");
        await new EfRuntimeCheckpointCommitStore(restarted, new FixedAccessor("tenant-a"), new FixedTimeProvider(OccurredAt), new PassThroughRootWriteLeaseManager()).CommitAsync(commit, Decision());
        await AssertStepState(restarted);
        Assert.Single(await restarted.RuntimeCheckpointCommits.ToArrayAsync());
        Assert.Single(await restarted.RuntimePostCommitOutbox.ToArrayAsync());
        Assert.Equal(3, await restarted.DurableValueStates.CountAsync());
    }

    private static RuntimeCheckpointCommit StepStateCommit(string id)
    {
        var type = new ValueTypeDescriptor("Int32");
        var workflow = C.Execution("workflow-a") with
        {
            CorrelationId = "new",
            SystemMetadata = new Dictionary<string, string> { [RuntimeMetadataKeys.InstanceName] = "Ready" },
            RootVariableFrame = new VariableFrameState("frame-root", "workflow", "workflow-a", null, VariableFrameKind.Root,
                new Dictionary<string, ValueEnvelope> { ["count"] = ValueEnvelope.Inline(type, JsonSerializer.SerializeToElement(1), ValueProtectionPolicy.InstanceInline) })
        };
        var values = RuntimeWorkflowStateSeed.BuildWorkflowOutputChanges("workflow-a", new Dictionary<string, object?> { ["Count"] = 10 }, OccurredAt)
            .Concat(RuntimeWorkflowStateSeed.BuildIdentityChanges("workflow-a", true, "new", true, "Ready", OccurredAt)).ToArray();
        return Commit(id) with
        {
            StateChanges = new RuntimeCheckpointStateChangeSet(C.Change("workflow-a", workflow), null,
                [C.Change("body", C.Activity("body"))], [], values, [], []),
            PostCommitIntents = [C.Intent("successor")]
        };
    }

    private static async Task AssertStepState(Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.RuntimeDbContext context)
    {
        var access = new FixedAccessor("tenant-a");
        var codec = new NoopContinuationCodec();
        var workflow = await new EfWorkflowExecutionStateStore(context, access, codec).FindAsync("workflow-a");
        Assert.NotNull(workflow);
        Assert.Equal("new", workflow.CorrelationId);
        Assert.Equal("Ready", workflow.SystemMetadata[RuntimeMetadataKeys.InstanceName]);
        Assert.Equal(1, workflow.RootVariableFrame!.Values["count"].InlineValue!.Value.GetInt32());
        var values = await new EfDurableValueStateStore(context, access, codec).ListAllDurableValueStatesAsync("workflow-a");
        Assert.Equal(10, Assert.Single(values, value => value.ValueId == "output:Count").InlineValue!.Value.GetInt32());
        Assert.Equal("new", Assert.Single(values, value => value.ValueId == "identity:correlationId").InlineValue!.Value.GetString());
        Assert.Equal("Ready", Assert.Single(values, value => value.ValueId == "identity:instanceName").InlineValue!.Value.GetString());
    }

    private sealed class FailStepOutputInsert : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { Fail(command); return ValueTask.FromResult(result); }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Fail(command); return ValueTask.FromResult(result); }
        private static void Fail(DbCommand command)
        {
            if (command.CommandText.Contains("INSERT", StringComparison.OrdinalIgnoreCase) &&
                command.CommandText.Contains("elsa_runtime_durable_value_state", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Injected state-value participant failure.");
        }
    }
}
