using System.Text.Json;
using Elsa.Activities.Runtime.Services;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// Covers the workflow-variable output-capture write-back onto the canonical root variable frame (#972):
/// a capture behaves like a committed Set, so the frame — the single runtime truth for workflow-scope
/// variables — carries the captured value for later reads.
/// </summary>
public sealed class RuntimeWorkflowVariableCaptureWriteBackTests
{
    private const string WorkflowExecutionId = "wfexec-1";
    private static readonly ValueTypeDescriptor StringType = new("String");

    [Fact]
    public void Apply_SetsTheCapturedInlineValueOnTheDeclaredRootFrameKey()
    {
        var state = WorkflowState(RootFrame(("var-body", "initial")));

        var changed = RuntimeWorkflowVariableCaptureWriteBack.Apply(
            state,
            "node-endpoint",
            Writes(("var-body", InlineEncoding("captured"))));

        var envelope = changed.RootVariableFrame!.Values["var-body"];
        Assert.Equal("captured", envelope.InlineValue!.Value.GetString());
        Assert.Equal(StringType.Alias, envelope.Type.Alias);
        Assert.Equal(1, changed.RootVariableFrame.Revision);
    }

    [Fact]
    public void Apply_WritesAnExplicitNullForANullEncoding()
    {
        var state = WorkflowState(RootFrame(("var-body", "initial")));

        var changed = RuntimeWorkflowVariableCaptureWriteBack.Apply(
            state,
            "node-endpoint",
            Writes(("var-body", new RuntimeDurableValueEncoding(DurableValueStorage.Inline, JsonSerializer.SerializeToElement<object?>(null), null))));

        Assert.Equal(ValuePresence.ExplicitNull, changed.RootVariableFrame!.Values["var-body"].Presence);
    }

    [Fact]
    public void Apply_RepresentsAnExternalizedCaptureAsAnExternalEnvelope()
    {
        var state = WorkflowState(RootFrame(("var-body", "initial")));
        var reference = new DurableValueExternalReference("test.opaque", "locator-1", new Dictionary<string, string>());

        var changed = RuntimeWorkflowVariableCaptureWriteBack.Apply(
            state,
            "node-endpoint",
            Writes(("var-body", new RuntimeDurableValueEncoding(DurableValueStorage.External, null, reference))));

        var envelope = changed.RootVariableFrame!.Values["var-body"];
        Assert.Same(reference, envelope.ExternalReference);
        Assert.Equal(DurableValueStorage.External, envelope.Policy.Storage);
    }

    [Fact]
    public void Apply_ThrowsForAnUndeclaredWorkflowVariable()
    {
        var state = WorkflowState(RootFrame(("var-body", "initial")));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            RuntimeWorkflowVariableCaptureWriteBack.Apply(
                state,
                "node-endpoint",
                Writes(("var-unknown", InlineEncoding("captured")))));

        Assert.Contains("undeclared workflow variable 'var-unknown'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_ThrowsWhenTheWorkflowHasNoRootFrame()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            RuntimeWorkflowVariableCaptureWriteBack.Apply(
                WorkflowState(null),
                "node-endpoint",
                Writes(("var-body", InlineEncoding("captured")))));

        Assert.Contains("no canonical root variable frame", exception.Message, StringComparison.Ordinal);
    }

    private static RuntimeDurableValueEncoding InlineEncoding(string value) =>
        new(DurableValueStorage.Inline, JsonSerializer.SerializeToElement(value), null);

    private static IReadOnlyDictionary<string, RuntimeDurableValueEncoding> Writes(
        params (string Key, RuntimeDurableValueEncoding Encoding)[] writes) =>
        writes.ToDictionary(item => item.Key, item => item.Encoding, StringComparer.Ordinal);

    private static VariableFrameState RootFrame(params (string Key, string Value)[] values) =>
        new VariableFrameFactory().CreateRoot(
            WorkflowExecutionId,
            Elsa.Expressions.Core.Models.VariableReference.WorkflowScopeId,
            values.ToDictionary(
                item => item.Key,
                item => ValueEnvelope.Inline(StringType, JsonSerializer.SerializeToElement(item.Value), ValueProtectionPolicy.InstanceInline),
                StringComparer.Ordinal));

    private static WorkflowExecutionState WorkflowState(VariableFrameState? root) =>
        new(
            WorkflowExecutionId,
            new WorkflowExecutableIdentity("artifact", "definition", "version", "1.0.0", "hash"),
            WorkflowExecutionStatus.Running,
            null,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            null,
            null,
            null,
            null,
            new Dictionary<string, string>())
        { RootVariableFrame = root };
}
