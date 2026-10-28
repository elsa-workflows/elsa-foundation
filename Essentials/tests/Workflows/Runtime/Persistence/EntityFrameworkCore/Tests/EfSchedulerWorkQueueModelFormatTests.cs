using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Elsa.Workflows.Runtime.Services.WorkHandlers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class EfSchedulerWorkQueueStoreTests
{
    [Fact]
    public async Task Model_input_uses_a_predecessor_unreadable_stamp_and_survives_missing_driver_without_a_claim()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        var now = DateTimeOffset.UtcNow;
        var input = new RuntimeSchedulerWorkItem("input", "model", "command", WorkflowExecutionCommandKind.ExecuteModel,
            "envelope", "key", now, now);
        await fixture.Store.EnqueueAsync(input);
        var before = await fixture.Context.SchedulerWorkItems.AsNoTracking().SingleAsync();
        Assert.Equal("2.0.0", before.SchemaVersion);
        Assert.Throws<EfSchemaVersionSkewException>(() => RuntimeOperationalStateEfModule.Chain.EnsureReadable(before.SchemaVersion));

        await using var restarted = database.Open("tenant-a");
        var drainer = new WorkflowSchedulerDrainer(restarted.Store, [new NoopWorkflowSchedulerWorkHandler()],
            new InMemoryWorkflowExecutionStateStore(), new InMemoryWorkflowSchedulerPoisonStore(),
            new WorkflowSchedulerPauseGate(new RuntimePauseDecisionProvider(new InMemoryWorkflowHoldStateStore())));
        await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => drainer.DrainAsync(new("model")).AsTask());
        var after = await restarted.Context.SchedulerWorkItems.AsNoTracking().SingleAsync();
        Assert.Equal(before.ContentJson, after.ContentJson);
        Assert.Equal(before.Revision, after.Revision);
        Assert.Equal(0, after.ClaimToken);
        Assert.Null(after.ClaimOwnerId);
        Assert.Single((await restarted.Store.ListAsync(new("model"))).Items);
    }

    [Fact]
    public async Task Native_v1_queue_content_is_read_without_rewriting_and_claimed_using_the_current_format()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        var now = DateTimeOffset.UtcNow;
        await fixture.Store.EnqueueAsync(new("native", "workflow", "command", WorkflowExecutionCommandKind.RunSchedulerWork,
            "envelope", "key", now, now));
        await fixture.Context.SchedulerWorkItems.ExecuteUpdateAsync(set => set.SetProperty(row => row.SchemaVersion, "1.0.0"));
        var before = await fixture.Context.SchedulerWorkItems.AsNoTracking().SingleAsync();
        Assert.Equal(WorkflowExecutionCommandKind.RunSchedulerWork, Assert.Single((await fixture.Store.ListAsync(new("workflow"))).Items).CommandKind);
        Assert.Equal("1.0.0", (await fixture.Context.SchedulerWorkItems.AsNoTracking().SingleAsync()).SchemaVersion);
        var claim = await fixture.Store.ClaimAsync(new("workflow", "owner", now, TimeSpan.FromMinutes(1)));
        Assert.NotNull(claim);
        var after = await fixture.Context.SchedulerWorkItems.AsNoTracking().SingleAsync();
        Assert.Equal(before.ContentJson, after.ContentJson);
        Assert.Equal("2.0.0", after.SchemaVersion);
        Assert.True((await fixture.Store.CompleteClaimAsync(claim)).Succeeded);
    }

    [Fact]
    public async Task Model_input_with_a_forged_v1_stamp_is_refused_before_claim_or_delete()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        var now = DateTimeOffset.UtcNow;
        await fixture.Store.EnqueueAsync(new("input", "model", "command", WorkflowExecutionCommandKind.ExecuteModel,
            "envelope", "key", now, now));
        await fixture.Context.SchedulerWorkItems.ExecuteUpdateAsync(set => set.SetProperty(row => row.SchemaVersion, "1.0.0"));
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Store.ListAsync(new("model")).AsTask());
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Store.ClaimAsync(new("model", "owner", now, TimeSpan.FromMinutes(1))).AsTask());
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Store.DequeueAsync("model").AsTask());
        var row = await fixture.Context.SchedulerWorkItems.AsNoTracking().SingleAsync();
        Assert.Equal(0, row.ClaimToken);
        Assert.Equal(1, row.Revision);
    }
}
