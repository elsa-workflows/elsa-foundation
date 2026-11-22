using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Publishing.Core.Services;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Publishing.Releases.Runtime.Tests;

public sealed class ActivityTemplateHasherTests
{
    // Observed through the original Publishing helper before the algorithm moved to Runtime.
    // Original helper source SHA-256: 9934b25ad5ed04b700db83d56cbb634e327c428098a695c56aecb844bddde853.
    private const string MinimalHash = "sha256:82189d4ffd4a775a87743a10a428873bdcc6a27735d9b9d38b5c48457f20b4d0";
    private const string RichHash = "sha256:54c70f6834ebefa6b00199623344ebd4dddbb6d05bed588c99b9242096c67cee";
    private const string ValueHash = "sha256:913d823b7d53f28a0f2954f196284e8c3526e53ea31dafc74f4378c5b4459895";
    private const string CanonicalValue = "{\"z\":[3,1,2],\"é\":\"λ\",\"a\":{\"b\":null,\"a\":true}}";

    [Theory]
    [InlineData("minimal", MinimalHash)]
    [InlineData("rich", RichHash)]
    [InlineData("value", ValueHash)]
    public void Runtime_hash_matches_captured_publication_vectors(string vector, string expected)
    {
        var actual = vector switch
        {
            "minimal" => Hash(Minimal()),
            "rich" => Hash(Rich()),
            _ => ActivityTemplateHasher.ComputeCanonicalValueHash(Json(CanonicalValue))
        };
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Existing_publication_facade_keeps_the_observed_contract()
    {
        Assert.Equal(MinimalHash, Legacy(Minimal()));
        Assert.Equal(RichHash, Legacy(Rich()));
        Assert.Equal(ValueHash, ExecutableActivityTemplateBehaviorHasher.ComputeCanonicalValueHash(Json(CanonicalValue)));
    }

    [Theory]
    [InlineData("literal")]
    [InlineData("alias")]
    [InlineData("resume")]
    [InlineData("direct-version")]
    [InlineData("origin")]
    [InlineData("overlay")]
    [InlineData("closed")]
    [InlineData("runtime")]
    [InlineData("storage")]
    [InlineData("provider")]
    [InlineData("compatibility")]
    public void Behavioral_changes_cannot_keep_the_captured_identity(string change)
    {
        var original = Rich();
        var changed = change switch
        {
            "literal" => Rich(literalValue: "changed"),
            "alias" => Rich(typeAlias: "sample.OtherMoney"),
            "resume" => original with { Resume = new Dictionary<string, WorkflowExecutableResumeTarget>() },
            "direct-version" => original with { Direct = original.Direct.Select(x => x with { DefinitionVersionId = "another-version" }).ToArray() },
            "origin" => original with { Direct = original.Direct.Select(x => x with { NodeOrigin = ActivityInvocationOrigin.Empty }).ToArray() },
            "overlay" => original with { Direct = original.Direct.Select(x => x with { OccurrenceOverlay = null }).ToArray() },
            "closed" => original with { Closed = [new("child-template-a", "sha256:different")] },
            "runtime" => original with { Requirements = [new("sample.consumer", "3")] },
            "storage" => original with { Storage = [new("different.storage")] },
            "provider" => original with { Provider = "sample.provider/3" },
            _ => original with { Compatibility = new Dictionary<string, string> { ["a"] = "changed" } }
        };
        Assert.NotEqual(RichHash, Hash(changed));
    }

    [Fact]
    public void Set_order_and_dictionary_order_preserve_the_observed_identity()
    {
        var original = Rich();
        var reordered = original with
        {
            Resume = original.Resume.Reverse().ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal),
            Direct = original.Direct.Reverse().ToArray(),
            Closed = original.Closed.Reverse().ToArray(),
            Requirements = original.Requirements.Reverse().ToArray(),
            Storage = original.Storage.Reverse().ToArray(),
            Compatibility = original.Compatibility.Reverse().ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal)
        };
        Assert.Equal(RichHash, Hash(reordered));
    }

    [Fact]
    public void Canonical_values_sort_object_members_and_preserve_array_order()
    {
        Assert.Equal(ValueHash, ActivityTemplateHasher.ComputeCanonicalValueHash(Json(
            "{\"a\":{\"a\":true,\"b\":null},\"é\":\"λ\",\"z\":[3,1,2]}")));
        Assert.NotEqual(ValueHash, ActivityTemplateHasher.ComputeCanonicalValueHash(Json(
            "{\"a\":{\"a\":true,\"b\":null},\"é\":\"λ\",\"z\":[1,2,3]}")));
    }

    private static string Hash(Input value) => ActivityTemplateHasher.Compute(value.Root,
        value.Resume, value.Direct, value.Closed, value.Requirements, value.Storage, value.Provider, value.Compatibility);

    private static string Legacy(Input value) => ExecutableActivityTemplateBehaviorHasher.Compute(value.Root,
        value.Resume, value.Direct, value.Closed, value.Requirements, value.Storage, value.Provider, value.Compatibility);

    private static Input Minimal() => new(Node("root"), new Dictionary<string, WorkflowExecutableResumeTarget>(),
        [], [], [], [], "provider/1", new Dictionary<string, string>());

    private static Input Rich(string literalValue = "original", string typeAlias = "sample.Money")
    {
        var type = new ValueTypeDescriptor(typeAlias, CollectionKind.Single, Json("{\"type\":\"object\",\"required\":[\"amount\"]}"), 3);
        var policy = new ValueProtectionPolicy(DurableValueLifecycle.Audit, DurableValueStorage.Inline,
            isSensitive: true, redactionMode: "mask", retentionPolicy: "seven-days", metadata: new Dictionary<string, string> { ["zone"] = "eu" });
        var conversion = ValueConversionPlan.Identity(type, ValueRepresentation.TypedValue);
        var bindings = new Dictionary<string, RuntimeInputBinding>
        {
            ["literal"] = new("literal", type, policy, RuntimeInputBindingSource.Literal,
                literal: ValueEnvelope.Inline(type, JsonSerializer.SerializeToElement(new { amount = 12.50m, note = literalValue }), policy),
                conversionPlan: conversion),
            ["request"] = new("request", type, policy, RuntimeInputBindingSource.WorkflowRequest,
                workflowRequest: new("request-money", "amount")),
            ["variable"] = new("variable", type, policy, RuntimeInputBindingSource.VariableRead,
                variable: new("balance", "workflow")),
            ["result"] = new("result", type, policy, RuntimeInputBindingSource.ActivityResult,
                activityResult: new("previous", "Result", "workflow", true)),
            ["expression"] = new("expression", type, policy, RuntimeInputBindingSource.Expression,
                expression: new("JavaScript", "amount + 1", new("alias", typeAlias, null),
                    parameters: new Dictionary<string, ExpressionParameterBinding>
                    {
                        ["literal"] = new LiteralExpressionParameterBinding(Json("[1,2]")),
                        ["request"] = new WorkflowRequestExpressionParameterBinding("request-money", "amount"),
                        ["variable"] = new VariableExpressionParameterBinding("workflow", "balance"),
                        ["result"] = new ActivityResultExpressionParameterBinding("previous", "Result", true),
                        ["instance"] = new WorkflowInstanceExpressionParameterBinding("correlationId")
                    }, options: Json("{\"strict\":true}"), parameterConversionPlans: new Dictionary<string, ValueConversionPlan> { ["literal"] = conversion })),
            ["secret"] = new("secret", type, policy, RuntimeInputBindingSource.SecretRead,
                secret: new("payments-key", "api-key", "finance")),
            ["withheld"] = new("withheld", type, policy, RuntimeInputBindingSource.Literal,
                literal: ValueEnvelope.Withheld(type, WithheldValue.SecretReference(new("retained-key"), conversion), policy))
        };
        var root = new ExecutableNode("root", "authored-root", "sample.Activity", "2.1.0", "sample.consumer",
            Json("{\"z\":2,\"a\":1}"), bindings, new Dictionary<string, string> { ["origin"] = "authored" },
            childSlots: [new("body", [Node("child")])],
            structure: new("sample.structure", "2", Json("{\"child\":\"child\"}")),
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>
            {
                ["Result"] = new("Result", "money-result", new("alias", typeAlias, null),
                    DurableValueLifecycle.Result, DurableValueStorage.External, true, storageDriverKey: "sample.storage", conversionPlan: conversion)
            }, descriptorSchemaVersion: "2");
        var origin = new ActivityInvocationOrigin([new(ActivityInvocationOriginSegmentKind.WorkflowRoot, "workflow"),
            new(ActivityInvocationOriginSegmentKind.AuthoredNode, "call")]);
        var overlay = new ExecutableActivityTemplateOccurrenceOverlay("1", Json("[{\"key\":\"amount\",\"value\":1}]"),
            new("sample.overlay", "1", Json("{\"node\":\"child\"}")), Json("[{\"key\":\"Result\"}]"));
        return new(root, new Dictionary<string, WorkflowExecutableResumeTarget>
            {
                ["z"] = new("root:later", "root", "later", new Dictionary<string, string> { ["mode"] = "resume" }, "later"),
                ["a"] = new("child:done", "child", "done", new Dictionary<string, string>())
            },
            [new("activity-def", "activity-version-2", "2.0.0", "child-template-b", "sha256:bbb", "occurrence-b", origin,
                ChildSlotName: "body", ChildIndex: 1, OccurrenceOverlay: overlay),
             new("activity-def", "activity-version-1", "1.0.0", "child-template-a", "sha256:aaa", "occurrence-a", origin)],
            [new("child-template-b", "sha256:bbb"), new("child-template-a", "sha256:aaa")],
            [new("sample.z", "2"), new("sample.a", "1")], [new("sample.storage"), new("json")],
            "sample.provider/2.3.0+fingerprint", new Dictionary<string, string> { ["z"] = "last", ["a"] = "first" });
    }

    private static ExecutableNode Node(string id) => new(id, "authored-" + id, "sample.Activity", "1.0.0", "sample.consumer",
        Json("{}"), new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>());

    private static JsonElement Json(string value)
    {
        using var document = JsonDocument.Parse(value);
        return document.RootElement.Clone();
    }

    private sealed record Input(ExecutableNode Root, IReadOnlyDictionary<string, WorkflowExecutableResumeTarget> Resume,
        IReadOnlyCollection<ExecutableActivityTemplateDependency> Direct, IReadOnlyCollection<ExecutableActivityTemplateIdentity> Closed,
        IReadOnlyCollection<RuntimeRequirement> Requirements, IReadOnlyCollection<RuntimeStorageDriverRequirement> Storage,
        string Provider, IReadOnlyDictionary<string, string> Compatibility);
}
