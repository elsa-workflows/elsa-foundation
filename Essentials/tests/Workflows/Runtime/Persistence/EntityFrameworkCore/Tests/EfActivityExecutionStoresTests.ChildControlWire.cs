using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class EfActivityExecutionStoresTests
{
    [Theory]
    [InlineData("missing-notifications")]
    [InlineData("null-notifications")]
    [InlineData("missing-failure")]
    [InlineData("missing-payload")]
    public async Task An_incomplete_persisted_root_control_refuses_read_and_replacement_without_changing_the_row(string mutation)
    {
        await using var fixture = await Fixture.CreateAsync("tenant-control");
        var control = new RuntimeChildControlBatch(1, "consumer", 1, "wf-control", "root", "root-work", 0, true,
            [new("escalation", JsonSerializer.SerializeToElement(new { code = "WARNING" }))], "ORDER");
        var original = State("wf-control", "root", 1) with { ProducedControl = control };
        await fixture.State.SaveAsync(original);
        var stored = await fixture.State.FindAsync("wf-control", "root");
        Assert.Equal(JsonSerializer.Serialize(control), JsonSerializer.Serialize(stored!.ProducedControl));
        var row = await fixture.Context.ActivityExecutionStates.SingleAsync();
        var payload = JsonNode.Parse(row.ContentJson)!.AsObject();
        var batch = payload["producedControl"]!.AsObject();
        switch (mutation)
        {
            case "missing-notifications": Assert.True(batch.Remove("notifications")); break;
            case "null-notifications": batch["notifications"] = null; break;
            case "missing-failure": Assert.True(batch.Remove("authoredFailureCode")); break;
            case "missing-payload": Assert.True(batch["notifications"]![0]!.AsObject().Remove("payload")); break;
        }
        row.ContentJson = payload.ToJsonString();
        fixture.Context.ChangeTracker.Clear();
        fixture.Context.ActivityExecutionStates.Update(row);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        await using var reopened = await fixture.ReopenAsync("tenant-control");

        await Assert.ThrowsAsync<InvalidDataException>(() => reopened.State.FindAsync("wf-control", "root").AsTask());
        await Assert.ThrowsAsync<InvalidDataException>(() => reopened.State.SaveAsync(original).AsTask());

        var retained = await reopened.Context.ActivityExecutionStates.AsNoTracking().SingleAsync();
        Assert.Equal(row.ContentJson, retained.ContentJson);
        Assert.Equal(row.Revision, retained.Revision);
    }
}
