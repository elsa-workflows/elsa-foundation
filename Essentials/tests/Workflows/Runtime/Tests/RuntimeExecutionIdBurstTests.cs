using System.Collections.Concurrent;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Contracts;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.Recovery;
using Elsa.Workflows.Runtime.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeExecutionIdBurstTests
{
    [Theory]
    [InlineData("workflow")]
    [InlineData("command")]
    [InlineData("envelope")]
    [InlineData("activity")]
    public void Default_runtime_identity_remains_distinct_when_the_clock_does_not_advance(string kind)
    {
        var clock = new FrozenClock();
        using var provider = new ServiceCollection().AddWorkflowRuntime().AddSingleton<TimeProvider>(clock).BuildServiceProvider();
        var generator = provider.GetRequiredService<IRuntimeExecutionIdGenerator>();
        Func<string> make = kind switch
        {
            "workflow" => generator.NewWorkflowExecutionId,
            "command" => generator.NewWorkflowExecutionCommandId,
            "envelope" => generator.NewWorkflowExecutionCommandEnvelopeId,
            _ => generator.NewActivityExecutionId
        };
        const int count = 65_536;
        var ids = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
        Parallel.For(0, count, _ => ids.TryAdd(make(), 0));
        // Check distinctness first: the old 22-bit generator fails this control, independently of textual format.
        Assert.Equal(count, ids.Count);
        Assert.All(ids.Keys, AssertVersion7);
    }

    [Fact]
    public void Default_identity_keeps_entropy_when_the_clock_moves_backwards()
    {
        var clock = new FrozenClock();
        using var provider = new ServiceCollection().AddWorkflowRuntime().AddSingleton<TimeProvider>(clock).BuildServiceProvider();
        var generator = provider.GetRequiredService<IRuntimeExecutionIdGenerator>();
        var first = generator.NewWorkflowExecutionId();
        clock.Now = clock.Now.AddMilliseconds(-1);
        var back = generator.NewWorkflowExecutionId();
        Assert.NotEqual(first, back);
        AssertVersion7(first);
        AssertVersion7(back);
        Assert.Equal(clock.Now.ToUnixTimeMilliseconds(), Convert.ToInt64(back[..12], 16));
    }

    [Fact]
    public async Task Ownership_lease_and_heartbeat_ids_use_the_high_entropy_source()
    {
        var state = new InMemoryExecutionLivenessStateStore();
        var ownership = new RuntimeExecutionOwnershipService(state, new FrozenClock(), new RuntimeExecutionOwnershipOptions());
        var lease = await ownership.AcquireAsync("identity-test");
        await ownership.HeartbeatAsync(lease);
        var saved = (await state.FindAsync("identity-test", "ownership:identity-test"))!;
        AssertVersion7(lease.LeaseId);
        AssertVersion7(saved.Heartbeat!.HeartbeatId);
        Assert.NotEqual(lease.LeaseId, saved.Heartbeat.HeartbeatId);
    }

    [Fact]
    public async Task Folded_checkpoint_and_commit_ids_use_the_high_entropy_source()
    {
        var services = new ServiceCollection().AddWorkflowRuntime().AddSingleton<TimeProvider>(new FrozenClock());
        services.AddPersistenceModes();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.GetRequiredService<IRuntimeCoalescingDrainScopeFactory>()
            .Begin("identity-test", new(PersistenceMode.Balanced, RunHasRecord: true));
        scope.Session.BufferDeferred(new("logical", new("logical-checkpoint", "Checkpoint", "identity-test", new FrozenClock().Now, [],
            new Dictionary<string, string>()), new(null, new("identity-test", RuntimeStateChangeOperation.Upsert,
                new SchedulerState("identity-test", 1), new Dictionary<string, string>()), [], [], [], [], []), [], new Dictionary<string, string>()));
        await scope.FlushAtQuiescenceAsync();
        var recorded = Assert.Single(provider.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>().ListCommits());
        AssertVersion7(recorded.Commit.CommitId);
        AssertVersion7(recorded.Commit.Checkpoint.CheckpointId);
    }

    private static void AssertVersion7(string id)
    {
        Assert.Equal(32, id.Length);
        Assert.True(Guid.TryParseExact(id, "N", out _));
        Assert.Equal('7', id[12]);
    }

    private sealed class FrozenClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
