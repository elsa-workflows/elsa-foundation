using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// Covers persisting workflow variables and inputs as durable runtime state (Seam C, #254) and projecting
/// them back into the name-keyed snapshots that the input-binding resolution context exposes to
/// <c>variables.*</c> / <c>input.*</c> expressions.
/// </summary>
public sealed class RuntimeWorkflowStateSeedTests
{
    private readonly DateTimeOffset _now = new(2026, 6, 12, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void BuildSeedChanges_TagsVariablesAndInputsWithDistinctMetadataAndValueIds()
    {
        var changes = RuntimeWorkflowStateSeed.BuildSeedChanges(
            "wfexec-1",
            variables: new Dictionary<string, object?> { ["greeting"] = "Hi" },
            inputs: new Dictionary<string, object?> { ["name"] = "Ada" },
            capturedAt: _now);

        Assert.Equal(2, changes.Count);

        var variable = Assert.Single(changes, change => change.State!.Metadata.ContainsKey(RuntimeMetadataKeys.VariableName));
        Assert.Equal("greeting", variable.State!.Metadata[RuntimeMetadataKeys.VariableName]);
        Assert.Equal($"{RuntimeWorkflowStateSeed.VariableValueIdPrefix}greeting", variable.State.ValueId);
        Assert.Equal(RuntimeStateChangeOperation.Upsert, variable.Operation);
        Assert.Equal(DurableValueLifecycle.Instance, variable.State.Lifecycle);
        Assert.Equal(DurableValueStorage.Inline, variable.State.Storage);
        Assert.Equal("Hi", variable.State.InlineValue!.Value.GetString());

        var input = Assert.Single(changes, change => change.State!.Metadata.ContainsKey(RuntimeMetadataKeys.InputName));
        Assert.Equal("name", input.State!.Metadata[RuntimeMetadataKeys.InputName]);
        Assert.Equal($"{RuntimeWorkflowStateSeed.InputValueIdPrefix}name", input.State.ValueId);
        Assert.Equal("Ada", input.State.InlineValue!.Value.GetString());
    }

    [Fact]
    public void BuildSeedChanges_TreatsNullCollectionsAsEmpty()
    {
        var changes = RuntimeWorkflowStateSeed.BuildSeedChanges("wfexec-1", variables: null, inputs: null, capturedAt: _now);

        Assert.Empty(changes);
    }

    [Fact]
    public void ProjectWorkflowVariables_ReadsOnlyVariableTaggedDurableValues()
    {
        var durableValues = new[]
        {
            SeededVariable("greeting", "Hi"),
            SeededInput("name", "Ada"),
            OutputDurableValue("delivery", "shipped")
        };

        var variables = RuntimeInputBindingStateProjection.ProjectWorkflowVariables(durableValues);

        var entry = Assert.Single(variables);
        Assert.Equal("greeting", entry.Key);
        Assert.Equal("Hi", ((JsonElement)entry.Value!).GetString());
    }

    [Fact]
    public void ProjectWorkflowInputs_ReadsOnlyInputTaggedDurableValues()
    {
        var durableValues = new[]
        {
            SeededVariable("greeting", "Hi"),
            SeededInput("name", "Ada")
        };

        var inputs = RuntimeInputBindingStateProjection.ProjectWorkflowInputs(durableValues);

        var entry = Assert.Single(inputs);
        Assert.Equal("name", entry.Key);
        Assert.Equal("Ada", ((JsonElement)entry.Value!).GetString());
    }

    [Fact]
    public void ProjectWorkflowVariables_LetsMostRecentCaptureWinForSameName()
    {
        var durableValues = new[]
        {
            SeededVariable("greeting", "old", _now),
            SeededVariable("greeting", "new", _now.AddSeconds(1))
        };

        var variables = RuntimeInputBindingStateProjection.ProjectWorkflowVariables(durableValues);

        Assert.Equal("new", ((JsonElement)Assert.Single(variables).Value!).GetString());
    }

    private DurableValueState SeededVariable(string name, string value, DateTimeOffset? capturedAt = null) =>
        SeededDurableValue(RuntimeMetadataKeys.VariableName, RuntimeWorkflowStateSeed.VariableValueIdPrefix, name, value, capturedAt);

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

    private DurableValueState OutputDurableValue(string outputName, string value) =>
        new(
            durableValueId: $"durable-{outputName}",
            workflowExecutionId: "wfexec-1",
            valueId: outputName,
            type: new RuntimeValueTypeDescriptor("clr", "System.String", null),
            lifecycle: DurableValueLifecycle.Instance,
            storage: DurableValueStorage.Inline,
            inlineValue: JsonSerializer.SerializeToElement(value),
            externalReference: null,
            sourceActivityExecutionId: "actexec-1",
            capturedAt: _now,
            metadata: new Dictionary<string, string> { [RuntimeMetadataKeys.OutputName] = outputName });
}
