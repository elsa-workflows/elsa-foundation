using System.Text.Json;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

public sealed class VariableFrameRuntimeTests
{
    private static readonly ValueTypeDescriptor StringType = new("String");

    [Fact]
    public void Root_container_and_iteration_frames_have_stable_activation_identity_and_isolated_values()
    {
        var factory = new VariableFrameFactory();
        var root = factory.CreateRoot(
            "wfexec-1",
            "workflow",
            Values(("message", "root")));
        var container = factory.CreateContainer(
            "scope-sequence",
            "actexec-sequence",
            root,
            Values(("message", "container")));
        var firstIteration = factory.CreateIteration(
            "scope-loop",
            "actexec-loop",
            "iteration-0",
            container,
            Values(("item", "first")));
        var secondIteration = factory.CreateIteration(
            "scope-loop",
            "actexec-loop",
            "iteration-1",
            container,
            Values(("item", "second")));

        Assert.Equal(VariableFrameKind.Root, root.Kind);
        Assert.Null(root.ParentFrameId);
        Assert.Equal("wfexec-1", root.ActivationId);
        Assert.Equal(root.FrameId, container.ParentFrameId);
        Assert.Equal("actexec-sequence", container.ActivationId);
        Assert.Equal(container.FrameId, firstIteration.ParentFrameId);
        Assert.NotEqual(firstIteration.FrameId, secondIteration.FrameId);
        Assert.Equal("actexec-loop:iteration:iteration-0", firstIteration.ActivationId);
        Assert.Equal("first", firstIteration.Values["item"].InlineValue!.Value.GetString());
        Assert.Equal("second", secondIteration.Values["item"].InlineValue!.Value.GetString());

        var updatedFirst = firstIteration.Set("item", Envelope("changed"), expectedRevision: 0);

        Assert.Equal(1, updatedFirst.Revision);
        Assert.Equal("changed", updatedFirst.Values["item"].InlineValue!.Value.GetString());
        Assert.Equal("second", secondIteration.Values["item"].InlineValue!.Value.GetString());
        Assert.Equal("container", container.Values["message"].InlineValue!.Value.GetString());
    }

    [Fact]
    public void Frame_rejects_stale_writes_and_writes_after_close()
    {
        var frame = new VariableFrameFactory().CreateRoot(
            "wfexec-1",
            "workflow",
            Values(("counter", "zero")));
        var updated = frame.Set("counter", Envelope("one"), expectedRevision: 0);

        Assert.Throws<VariableFrameRevisionException>(() => updated.Set("counter", Envelope("two"), expectedRevision: 0));

        var closed = updated.Close(expectedRevision: 1);
        Assert.Equal(VariableFrameStatus.Closed, closed.Status);
        Assert.Throws<InvalidOperationException>(() => closed.Set("counter", Envelope("two"), expectedRevision: 2));
    }

    private static IReadOnlyDictionary<string, ValueEnvelope> Values(params (string Key, string Value)[] values) =>
        values.ToDictionary(item => item.Key, item => Envelope(item.Value), StringComparer.Ordinal);

    private static ValueEnvelope Envelope(string value) =>
        ValueEnvelope.Inline(StringType, JsonSerializer.SerializeToElement(value), ValueProtectionPolicy.InstanceInline);
}
