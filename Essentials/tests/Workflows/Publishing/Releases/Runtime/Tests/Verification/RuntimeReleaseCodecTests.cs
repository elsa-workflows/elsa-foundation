using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Publishing.Releases.Runtime.Core;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Publishing.Releases.Runtime.Tests.Verification;

public sealed class RuntimeReleaseCodecTests
{
    private readonly ReleaseRuntimeArtifactCodec _codec = new();

    [Theory]
    [InlineData(70, 80)]
    [InlineData(110, 128)]
    [InlineData(170, 256)]
    public void Opaque_json_above_serializer_default_round_trips_with_the_selected_codec_depth(int depth, int maximum)
    {
        var json = string.Concat(Enumerable.Repeat("{\"nested\":", depth)) + "1" + new string('}', depth);
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = maximum });
        var activity = RuntimeReleaseFixture.ActivityVersion(RuntimeReleaseFixture.Template(), providerPayload: document.RootElement);
        var limits = new Elsa.Workflows.Publishing.Releases.Core.ReleaseBundleLimits { MaxJsonDepth = maximum };
        var encoded = _codec.EncodeActivityVersion(activity, limits);
        Assert.Equal(json, _codec.DecodeActivityVersion(encoded.GetPayloadBytes(), limits).ProviderPayload.GetRawText());
        Assert.Throws<ReleaseRuntimeArtifactException>(() => _codec.EncodeActivityVersion(activity,
            new() { MaxJsonDepth = 64 }));
    }

    [Fact]
    public void New_wire_envelope_preserves_original_pin_opaque_json_aliases_and_copied_bytes()
    {
        var node = RuntimeReleaseFixture.Node("注文:/root", metadata: new Dictionary<string, string> { ["opaque:/é"] = "λ" },
            consumer: "fixture.consumer", payload: RuntimeReleaseFixture.Json("{\"z\":1,\"a\":2}"), intrinsic: null);
        var workflow = RuntimeReleaseFixture.Workflow(node);
        var encoded = _codec.EncodeWorkflow(new(RuntimeReleaseFixture.Scope, workflow));
        Assert.Equal(workflow.Identity.ArtifactId, encoded.Descriptor.Identity);
        Assert.Equal(workflow.Identity.ArtifactVersion, encoded.OwnerVersion!.TextValue);
        var bytes = encoded.GetPayloadBytes(); var first = bytes[0]; bytes[0] = 0;
        Assert.Equal(first, encoded.GetPayloadBytes()[0]);
        var decoded = _codec.DecodeWorkflow(encoded.GetPayloadBytes());
        Assert.True(WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(workflow.Identity, decoded.Executable.Identity));
        Assert.Equal("{\"z\":1,\"a\":2}", decoded.Executable.RootActivity.DescriptorPayload.GetRawText());
        Assert.Equal(node.Metadata, decoded.Executable.RootActivity.Metadata);
        using var document = JsonDocument.Parse(encoded.GetPayloadBytes());
        Assert.Equal(1, document.RootElement.GetProperty("formatVersion").GetInt32());
        Assert.False(document.RootElement.GetProperty("content").GetProperty("executable").TryGetProperty("nodes", out _));
    }

    [Fact]
    public void Opaque_json_retains_interior_spacing_property_order_and_original_escapes()
    {
        const string raw = "{ \"z\" : [ 1, {\"é\" : \"\\u03bb\", \"escaped\" : \"\\/\\u0041\"} ], \"a\" : 2 }";
        var workflow = RuntimeReleaseFixture.Workflow(RuntimeReleaseFixture.Node(consumer: "fixture.consumer", payload: RuntimeReleaseFixture.Json(raw), intrinsic: null));
        var encoded = _codec.EncodeWorkflow(new(RuntimeReleaseFixture.Scope, workflow));
        using var document = JsonDocument.Parse(encoded.GetPayloadBytes());
        Assert.Equal(raw, document.RootElement.GetProperty("content").GetProperty("executable").GetProperty("rootActivity").GetProperty("descriptorPayload").GetRawText());
        Assert.Equal(raw, _codec.DecodeWorkflow(encoded.GetPayloadBytes()).Executable.RootActivity.DescriptorPayload.GetRawText());
    }

    [Fact]
    public void Large_opaque_whitespace_is_charged_without_copying_or_compacting_the_original_fragment()
    {
        var raw = "{\"data\":" + new string(' ', 4 * 1024 * 1024) + "1}";
        var workflow = RuntimeReleaseFixture.Workflow(RuntimeReleaseFixture.Node(consumer: "fixture.consumer", payload: RuntimeReleaseFixture.Json(raw), intrinsic: null));
        var before = GC.GetAllocatedBytesForCurrentThread();
        Assert.Throws<ReleaseRuntimeArtifactException>(() => _codec.EncodeWorkflow(new(RuntimeReleaseFixture.Scope, workflow), new() { MaxBlobBytes = 1024 }));
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < 64 * 1024, $"Opaque whitespace allocated {allocated} bytes before refusal.");
    }

    [Fact]
    public void Expression_parameter_subtypes_and_nested_contracts_round_trip_without_type_loading()
    {
        var type = new ValueTypeDescriptor("String");
        var binding = new RuntimeInputBinding("value", type, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Expression,
            expression: new("JavaScript", "input", parameters: new Dictionary<string, ExpressionParameterBinding>
            {
                ["input"] = new LiteralExpressionParameterBinding(RuntimeReleaseFixture.Json("{\"z\":1,\"a\":2}")),
                ["request"] = new WorkflowRequestExpressionParameterBinding("source", "path"),
                ["variable"] = new VariableExpressionParameterBinding("workflow", "v"),
                ["result"] = new ActivityResultExpressionParameterBinding("prior", "Result", true),
                ["instance"] = new WorkflowInstanceExpressionParameterBinding("correlationId")
            }));
        var encoded = _codec.EncodeWorkflow(new(RuntimeReleaseFixture.Scope, RuntimeReleaseFixture.Workflow(RuntimeReleaseFixture.Node(bindings: new Dictionary<string, RuntimeInputBinding> { ["value"] = binding }))));
        var decoded = _codec.DecodeWorkflow(encoded.GetPayloadBytes()).Executable.RootActivity.InputBindings["value"].Expression!;
        Assert.IsType<LiteralExpressionParameterBinding>(decoded.Parameters["input"]);
        Assert.IsType<WorkflowRequestExpressionParameterBinding>(decoded.Parameters["request"]);
        Assert.IsType<VariableExpressionParameterBinding>(decoded.Parameters["variable"]);
        Assert.IsType<ActivityResultExpressionParameterBinding>(decoded.Parameters["result"]);
        Assert.IsType<WorkflowInstanceExpressionParameterBinding>(decoded.Parameters["instance"]);
        Assert.Equal("{\"z\":1,\"a\":2}", ((LiteralExpressionParameterBinding)decoded.Parameters["input"]).Value.GetRawText());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Encoding_refuses_unpaired_surrogates_before_metadata_or_keys_can_be_repaired(bool key)
    {
        var metadata = new Dictionary<string, string> { [key ? "\uD800" : "safe"] = key ? "safe" : "\uD800" };
        var workflow = RuntimeReleaseFixture.Workflow(RuntimeReleaseFixture.Node(metadata: metadata));
        Assert.Equal("invalid_runtime_unicode", Assert.Throws<ReleaseRuntimeArtifactException>(() => _codec.EncodeWorkflow(new(RuntimeReleaseFixture.Scope, workflow))).Code);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("case-alias")]
    [InlineData("unknown")]
    [InlineData("computed-alias")]
    [InlineData("bad-version")]
    [InlineData("bad-utf8")]
    [InlineData("bad-surrogate")]
    public void Unknown_or_lossy_wire_shapes_refuse_without_hydrating_arbitrary_types(string change)
    {
        var original = _codec.EncodeWorkflow(new(RuntimeReleaseFixture.Scope, RuntimeReleaseFixture.Workflow())).GetPayloadBytes();
        var root = JsonNode.Parse(original)!.AsObject();
        switch (change)
        {
            case "case-alias": root["content"]!["executable"]!["Identity"] = root["content"]!["executable"]!["identity"]!.DeepClone(); break;
            case "unknown": root["content"]!["executable"]!["$type"] = "private-token.Assembly"; break;
            case "computed-alias": root["content"]!["executable"]!["rootActivity"]!["inputBindings"]!["value"]!["literalValue"] = "different"; break;
            case "bad-version": root["formatVersion"] = 2; break;
        }
        var bytes = change switch
        {
            "duplicate" => Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(original).Replace("\"formatVersion\":1", "\"formatVersion\":1,\"formatVersion\":1", StringComparison.Ordinal)),
            "bad-utf8" => new byte[] { 255 },
            "bad-surrogate" => Encoding.UTF8.GetBytes("{\"formatVersion\":1,\"content\":\"\\uD800\"}"),
            _ => Encoding.UTF8.GetBytes(root.ToJsonString())
        };
        var failure = Assert.Throws<ReleaseRuntimeArtifactException>(() => _codec.DecodeWorkflow(bytes));
        Assert.DoesNotContain("private-token", failure.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Codecs_enforce_byte_and_depth_bounds_and_keep_template_overlay_payloads()
    {
        var child = RuntimeReleaseFixture.Template(); var parent = RuntimeReleaseFixture.Template(dependencies:
            [new("activity", "version", "1.0.0", child.TemplateId, child.TemplateHash, "call", ActivityInvocationOrigin.Empty,
                OccurrenceOverlay: new("1", RuntimeReleaseFixture.Json("[{\"z\":1,\"a\":2}]"), null, RuntimeReleaseFixture.Json("[]")))]);
        var encoded = _codec.EncodeTemplate(new(RuntimeReleaseFixture.Scope, parent));
        var decoded = _codec.DecodeTemplate(encoded.GetPayloadBytes());
        Assert.Equal(parent.TemplateHash, decoded.Template.TemplateHash);
        Assert.Equal("[{\"z\":1,\"a\":2}]", decoded.Template.DirectDependencies.Single().OccurrenceOverlay!.InputBindings.GetRawText());
        Assert.Throws<ReleaseRuntimeArtifactException>(() => _codec.DecodeTemplate(encoded.GetPayloadBytes(), new() { MaxBlobBytes = 10 }));
        Assert.Throws<ReleaseRuntimeArtifactException>(() => _codec.EncodeTemplate(new(RuntimeReleaseFixture.Scope, parent), new() { MaxJsonDepth = 2 }));
    }

    [Fact]
    public void Oversized_decode_refuses_before_copying_the_public_memory_input()
    {
        var payload = new byte[8 * 1024 * 1024];
        var before = GC.GetAllocatedBytesForCurrentThread();
        Assert.Throws<ReleaseRuntimeArtifactException>(() => _codec.DecodeWorkflow(payload, new() { MaxBlobBytes = 1024 }));
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < 64 * 1024, $"The oversized input allocated {allocated} bytes before refusal.");
    }

    [Fact]
    public void Repeated_large_opaque_values_refuse_without_materializing_the_complete_encoded_output()
    {
        _codec.EncodeWorkflow(new(RuntimeReleaseFixture.Scope, RuntimeReleaseFixture.Workflow())); // Warm reflection caches outside the allocation subject.
        var opaque = RuntimeReleaseFixture.Json("{\"data\":\"" + new string('x', 32 * 1024) + "\"}");
        var children = Enumerable.Range(0, 256).Select(index => RuntimeReleaseFixture.Node("child-" + index, consumer: "fixture.consumer", payload: opaque, intrinsic: null)).ToArray();
        var workflow = RuntimeReleaseFixture.Workflow(RuntimeReleaseFixture.Node(children: [new("children", children)]));
        var before = GC.GetAllocatedBytesForCurrentThread();
        Assert.Throws<ReleaseRuntimeArtifactException>(() => _codec.EncodeWorkflow(new(RuntimeReleaseFixture.Scope, workflow), new() { MaxBlobBytes = 64 * 1024 }));
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < 2 * 1024 * 1024, $"The bounded encode allocated {allocated} bytes for repeated opaque values.");
    }

    [Fact]
    public void Escaping_expansion_refuses_before_allocating_a_large_serializer_buffer()
    {
        _codec.EncodeWorkflow(new(RuntimeReleaseFixture.Scope, RuntimeReleaseFixture.Workflow()));
        var workflow = RuntimeReleaseFixture.Workflow(RuntimeReleaseFixture.Node(metadata: new Dictionary<string, string> { ["escaped"] = new('<', 512 * 1024) }));
        var before = GC.GetAllocatedBytesForCurrentThread();
        Assert.Throws<ReleaseRuntimeArtifactException>(() => _codec.EncodeWorkflow(new(RuntimeReleaseFixture.Scope, workflow), new() { MaxBlobBytes = 1024 * 1024 }));
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < 2 * 1024 * 1024, $"Escaping expansion allocated {allocated} bytes before refusal.");
    }

    [Theory]
    [InlineData("ordinary")]
    [InlineData("<é/注文:\"")]
    public void Exact_encoded_byte_budget_is_accepted_and_one_byte_less_refuses(string value)
    {
        var workflow = new ReleaseRuntimeWorkflow(RuntimeReleaseFixture.Scope, RuntimeReleaseFixture.Workflow(RuntimeReleaseFixture.Node(metadata: new Dictionary<string, string> { ["value"] = string.Concat(Enumerable.Repeat(value, 1024)) })));
        var expected = _codec.EncodeWorkflow(workflow).GetPayloadBytes();
        var bounded = _codec.EncodeWorkflow(workflow, new() { MaxBlobBytes = expected.Length }).GetPayloadBytes();
        Assert.Equal(expected, bounded);
        Assert.Throws<ReleaseRuntimeArtifactException>(() => _codec.EncodeWorkflow(workflow, new() { MaxBlobBytes = expected.Length - 1 }));
    }

    [Fact]
    public void Template_wire_excludes_recomputed_nodes_by_id_and_refuses_that_unsupported_alias()
    {
        var encoded = _codec.EncodeTemplate(new(RuntimeReleaseFixture.Scope, RuntimeReleaseFixture.Template()));
        var root = JsonNode.Parse(encoded.GetPayloadBytes())!.AsObject();
        Assert.False(root["content"]!["template"]!.AsObject().ContainsKey("nodesById"));
        root["content"]!["template"]!["nodesById"] = new JsonObject();
        Assert.Throws<ReleaseRuntimeArtifactException>(() => _codec.DecodeTemplate(Encoding.UTF8.GetBytes(root.ToJsonString())));
    }
}
