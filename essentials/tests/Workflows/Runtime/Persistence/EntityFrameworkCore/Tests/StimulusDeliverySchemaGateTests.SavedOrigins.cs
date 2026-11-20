using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class StimulusDeliverySchemaGateTests
{
    [Theory]
    [InlineData("1.0.0", WorkflowExecutionCommandKind.Start)]
    [InlineData("2.0.0", WorkflowExecutionCommandKind.Checkpoint)]
    [InlineData("3.0.0", WorkflowExecutionCommandKind.Start)]
    public async Task Saved_origin_never_writes_a_predecessor_scheduler_profile(string profile, WorkflowExecutionCommandKind kind)
    {
        await using var fixture = await Fixture.Create(RuntimeSchedulerWorkEfModule.SchemaFamily, profile);
        await using var context = fixture.Open();
        var store = new EfSchedulerWorkQueueStore(context, new Accessor(), new NoTokens());
        var payload = JsonSerializer.SerializeToElement(new { StImUlUsDeLiVeRyOrIgIn = new { Version = 1 } });
        var item = new RuntimeSchedulerWorkItem("protected", "execution", "command", kind, "envelope", "key", Now, Now, payload: payload);
        await Assert.ThrowsAsync<EfSchemaWriteRefusedException>(() => store.EnqueueAsync(item).AsTask());
        await using var verify = fixture.Open();
        Assert.Empty(await verify.SchedulerWorkItems.ToArrayAsync());
        Assert.Empty(await verify.ExecutionLivenessStates.ToArrayAsync());
    }

    [Theory]
    [InlineData("stimulusDeliveryOrigin")]
    [InlineData("StImUlUsDeLiVeRyOrIgIn")]
    public void Workflow_predecessor_upcast_refuses_reserved_saved_origin_even_null(string field)
    {
        var row = new EfSchemaRowContent(typeof(WorkflowExecutionStateEntity),
            (nameof(WorkflowExecutionStateEntity.ContentJson), "{\"" + field + "\":null}"));
        Assert.Throws<InvalidDataException>(() => new RuntimeWorkflowIngressV2Upcaster().Upcast(row));
        var legacy = new EfSchemaRowContent(typeof(WorkflowExecutionStateEntity), (nameof(WorkflowExecutionStateEntity.ContentJson), "{}"));
        Assert.Equal("{}", new RuntimeWorkflowIngressV2Upcaster().Upcast(legacy)[nameof(WorkflowExecutionStateEntity.ContentJson)]);
    }

    [Fact]
    public void Scheduler_predecessor_upcast_refuses_saved_origin()
    {
        var item = new RuntimeSchedulerWorkItem("protected", "execution", "command", WorkflowExecutionCommandKind.Checkpoint,
            "envelope", "key", Now, Now, payload: JsonSerializer.SerializeToElement(new { StImUlUsDeLiVeRyOrIgIn = new { Version = 1 } }));
        var row = new EfSchemaRowContent(typeof(SchedulerWorkItemEntity), (nameof(SchedulerWorkItemEntity.ContentJson), RuntimeArtifactJson.Serialize(item)));
        Assert.Throws<InvalidDataException>(() => new RuntimeSchedulerIngressV3Upcaster().Upcast(row));
    }
}
