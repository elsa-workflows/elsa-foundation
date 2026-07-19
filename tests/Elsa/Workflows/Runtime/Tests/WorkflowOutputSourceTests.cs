using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class WorkflowOutputSourceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ReadAsync_AppliesPendingTerminalChangesBeforeSafeProjection()
    {
        var store = new InMemoryDurableValueStateStore();
        await store.SaveAsync(NewOutput("Result", "stored", Now));
        var source = new WorkflowOutputSource(store, new CapturePayloadPolicy());
        var replacement = NewOutput("Result", "terminal", Now.AddSeconds(1));
        var added = NewOutput("Secret", "do-not-expose", Now.AddSeconds(1), isSensitive: true);

        var outputs = await source.ReadAsync("child-1",
        [
            new RuntimeStateChange<DurableValueState>(replacement.DurableValueId, RuntimeStateChangeOperation.Upsert, replacement, replacement.Metadata),
            new RuntimeStateChange<DurableValueState>(added.DurableValueId, RuntimeStateChangeOperation.Upsert, added, added.Metadata)
        ]);

        Assert.Equal(["Result", "Secret"], outputs.Select(output => output.Name));
        Assert.Equal("terminal", outputs.Single(output => output.Name == "Result").Value!.Value.GetString());
        var secret = outputs.Single(output => output.Name == "Secret");
        Assert.True(secret.IsRedacted);
        Assert.Null(secret.Value);
    }

    [Fact]
    public async Task ReadAsync_RejectsPendingChangesForAnotherExecution()
    {
        var source = new WorkflowOutputSource(new InMemoryDurableValueStateStore(), new CapturePayloadPolicy());
        var foreign = NewOutput("Result", "foreign", Now) with { };
        foreign = new DurableValueState(
            foreign.DurableValueId,
            "other-child",
            foreign.ValueId,
            foreign.Type,
            foreign.Lifecycle,
            foreign.Storage,
            foreign.InlineValue,
            foreign.ExternalReference,
            foreign.SourceActivityExecutionId,
            foreign.CapturedAt,
            foreign.Metadata);

        await Assert.ThrowsAsync<ArgumentException>(() => source.ReadAsync("child-1",
        [
            new RuntimeStateChange<DurableValueState>(foreign.DurableValueId, RuntimeStateChangeOperation.Upsert, foreign, foreign.Metadata)
        ]).AsTask());
    }

    [Fact]
    public async Task ReadAsync_WithNullPendingChanges_ProjectsStoredOutputs()
    {
        var store = new InMemoryDurableValueStateStore();
        await store.SaveAsync(NewOutput("Result", "stored", Now));
        var source = new WorkflowOutputSource(store, new CapturePayloadPolicy());

        var output = Assert.Single(await source.ReadAsync("child-1", pendingDurableValueChanges: null));

        Assert.Equal("Result", output.Name);
        Assert.Equal("stored", output.Value!.Value.GetString());
    }

    [Fact]
    public async Task ReadAsync_DeleteChange_RemovesStoredOutputBeforeProjection()
    {
        var store = new InMemoryDurableValueStateStore();
        var stored = NewOutput("Result", "stored", Now);
        await store.SaveAsync(stored);
        var source = new WorkflowOutputSource(store, new CapturePayloadPolicy());

        var outputs = await source.ReadAsync("child-1",
        [
            new RuntimeStateChange<DurableValueState>(
                stored.DurableValueId,
                RuntimeStateChangeOperation.Delete,
                stored,
                stored.Metadata)
        ]);

        Assert.Empty(outputs);
    }

    private static DurableValueState NewOutput(string name, string value, DateTimeOffset capturedAt, bool isSensitive = false)
    {
        var metadata = new Dictionary<string, string>
        {
            [RuntimeMetadataKeys.OutputName] = name
        };
        if (isSensitive)
            metadata[RuntimeMetadataKeys.IsSensitive] = "true";

        return new DurableValueState(
            $"durable-output:{name}",
            "child-1",
            $"output:{name}",
            new RuntimeValueTypeDescriptor("clr", "System.String", null),
            DurableValueLifecycle.Result,
            DurableValueStorage.Inline,
            System.Text.Json.JsonSerializer.SerializeToElement(value),
            null,
            null,
            capturedAt,
            metadata);
    }

    private sealed class CapturePayloadPolicy : Elsa.Workflows.Runtime.Core.Contracts.IRuntimePayloadCapturePolicy
    {
        public RuntimePayloadCaptureDecision Decide(RuntimePayloadCaptureRequest request) =>
            request.IsSensitive
                ? new RuntimePayloadCaptureDecision(RuntimePayloadCaptureMode.None, "Sensitive")
                : new RuntimePayloadCaptureDecision(RuntimePayloadCaptureMode.Payload, "Captured");
    }
}
