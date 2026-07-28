using System.Text.Json;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeLoopIterationScopeFactoryTests
{
    private static readonly ValueTypeDescriptor StringType = new("String");

    [Fact]
    public void Iterations_are_distinct_durable_frames_with_a_shared_lexical_scope()
    {
        var frames = new VariableFrameFactory();
        var root = frames.CreateRoot("wfexec", "workflow", Values(("root", "value")));
        var factory = new RuntimeLoopIterationFrameFactory();

        var first = factory.Create(Request("iteration-0", "apple"), "body-0", root);
        var second = factory.Create(Request("iteration-1", "banana"), "body-1", root);

        Assert.Equal(VariableFrameKind.Iteration, first.Kind);
        Assert.Equal("loop", first.ScopeId);
        Assert.Equal(root.FrameId, first.ParentFrameId);
        Assert.NotEqual(first.FrameId, second.FrameId);
        Assert.Equal("apple", first.Values["item"].InlineValue!.Value.GetString());
        Assert.Equal("banana", second.Values["item"].InlineValue!.Value.GetString());
    }

    [Fact]
    public void Request_defensively_snapshots_envelopes_and_rejects_empty_values()
    {
        var values = Values(("item", "apple")).ToDictionary(item => item.Key, item => item.Value);
        var request = new LoopIterationScopeRequest("loop", "iteration-0", values);
        values["item"] = Envelope("changed");

        Assert.Equal("apple", request.Values["item"].InlineValue!.Value.GetString());
        Assert.Throws<ArgumentException>(() => new LoopIterationScopeRequest("loop", "iteration-0", new Dictionary<string, ValueEnvelope>()));
    }

    private static LoopIterationScopeRequest Request(string iterationId, string value) =>
        new("loop", iterationId, Values(("item", value)));

    private static IReadOnlyDictionary<string, ValueEnvelope> Values(params (string Key, string Value)[] values) =>
        values.ToDictionary(item => item.Key, item => Envelope(item.Value), StringComparer.Ordinal);

    private static ValueEnvelope Envelope(string value) =>
        ValueEnvelope.Inline(StringType, JsonSerializer.SerializeToElement(value), ValueProtectionPolicy.InstanceInline);
}
