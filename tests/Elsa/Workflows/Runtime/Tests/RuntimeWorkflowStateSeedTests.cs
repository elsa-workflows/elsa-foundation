using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// Covers persisting workflow inputs as durable runtime state (Seam C, #254) and projecting them back
/// into the name-keyed snapshot that the input-binding resolution context exposes to <c>input.*</c>
/// expressions. Workflow variables are NOT part of this channel (#972): they live exclusively in the
/// canonical root variable frame.
/// </summary>
public sealed class RuntimeWorkflowStateSeedTests
{
    private readonly DateTimeOffset _now = new(2026, 6, 12, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void BuildSeedChanges_TagsInputsWithTheInputMetadataAndValueId()
    {
        var changes = RuntimeWorkflowStateSeed.BuildSeedChanges(
            "wfexec-1",
            inputs: new Dictionary<string, object?> { ["name"] = "Ada" },
            capturedAt: _now);

        var input = Assert.Single(changes);
        Assert.Equal("name", input.State!.Metadata[RuntimeMetadataKeys.InputName]);
        Assert.Equal($"{RuntimeWorkflowStateSeed.InputValueIdPrefix}name", input.State.ValueId);
        Assert.Equal(RuntimeStateChangeOperation.Upsert, input.Operation);
        Assert.Equal(DurableValueLifecycle.Instance, input.State.Lifecycle);
        Assert.Equal(DurableValueStorage.Inline, input.State.Storage);
        Assert.Equal("Ada", input.State.InlineValue!.Value.GetString());
    }

    [Fact]
    public void BuildSeedChanges_TreatsNullCollectionsAsEmpty()
    {
        var changes = RuntimeWorkflowStateSeed.BuildSeedChanges("wfexec-1", inputs: null, capturedAt: _now);

        Assert.Empty(changes);
    }

    [Fact]
    public void BuildSeedChanges_SeedsTriggerNodeIdOnItsOwnReservedChannel_AndProjectsBack()
    {
        // Spec 089 D (T003): the trigger-node identity rides a reserved channel (trigger:*), never the input:*
        // namespace, and the projection unwraps the inline JSON string back to the node id.
        var changes = RuntimeWorkflowStateSeed.BuildSeedChanges(
            "wfexec-1",
            inputs: new Dictionary<string, object?> { ["name"] = "Ada" },
            capturedAt: _now,
            triggerNodeId: "node-http");

        var trigger = Assert.Single(changes, change => change.State!.Metadata.ContainsKey(RuntimeMetadataKeys.TriggerNodeId));
        Assert.Equal(RuntimeWorkflowStateSeed.TriggerNodeIdSlotName, trigger.State!.Metadata[RuntimeMetadataKeys.TriggerNodeId]);
        Assert.Equal($"{RuntimeWorkflowStateSeed.TriggerNodeValueIdPrefix}{RuntimeWorkflowStateSeed.TriggerNodeIdSlotName}", trigger.State.ValueId);
        // It never lands in the input:* namespace, so it cannot collide with author-declared inputs.
        Assert.DoesNotContain(changes, change => change.State!.Metadata.ContainsKey(RuntimeMetadataKeys.InputName) && change.State.ValueId.Contains("node-http"));

        Assert.Equal("node-http", RuntimeInputBindingStateProjection.ProjectTriggerNodeId(changes.Select(change => change.State!)));
    }

    [Fact]
    public void ProjectTriggerNodeId_IsNullWhenNotSeeded()
    {
        // A direct (non-trigger) run seeds no trigger-node channel, so the projection is null.
        var changes = RuntimeWorkflowStateSeed.BuildSeedChanges("wfexec-1", inputs: new Dictionary<string, object?> { ["name"] = "Ada" }, capturedAt: _now);

        Assert.Null(RuntimeInputBindingStateProjection.ProjectTriggerNodeId(changes.Select(change => change.State!)));
    }

    [Fact]
    public void ProjectWorkflowInputs_ReadsOnlyInputTaggedDurableValues()
    {
        var durableValues = new[]
        {
            LegacyVariableDurableValue("greeting", "Hi"),
            SeededInput("name", "Ada")
        };

        var inputs = RuntimeInputBindingStateProjection.ProjectWorkflowInputs(durableValues);

        var entry = Assert.Single(inputs);
        Assert.Equal("name", entry.Key);
        Assert.Equal("Ada", ((JsonElement)entry.Value!).GetString());
    }

    // A historical variable-tagged durable row (pre-#972 datastores) must not leak into any read surface.
    private DurableValueState LegacyVariableDurableValue(string name, string value) =>
        SeededDurableValue(RuntimeMetadataKeys.VariableName, RuntimeWorkflowStateSeed.VariableValueIdPrefix, name, value, null);

    private DurableValueState SeededInput(string name, string value, DateTimeOffset? capturedAt = null) =>
        SeededDurableValue(RuntimeMetadataKeys.InputName, RuntimeWorkflowStateSeed.InputValueIdPrefix, name, value, capturedAt);

    private DurableValueState SeededDurableValue(string metadataKey, string valueIdPrefix, string name, string value, DateTimeOffset? capturedAt) =>
        new(
            durableValueId: $"durable-{valueIdPrefix}{name}",
            workflowExecutionId: "wfexec-1",
            valueId: $"{valueIdPrefix}{name}",
            type: new RuntimeValueTypeDescriptor("clr", "System.String", null),
            lifecycle: DurableValueLifecycle.Instance,
            storage: DurableValueStorage.Inline,
            inlineValue: JsonSerializer.SerializeToElement(value),
            externalReference: null,
            sourceActivityExecutionId: null,
            capturedAt: capturedAt ?? _now,
            metadata: new Dictionary<string, string> { [metadataKey] = name });
}
