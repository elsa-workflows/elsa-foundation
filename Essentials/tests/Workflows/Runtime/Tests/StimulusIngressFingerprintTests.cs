using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class StimulusIngressFingerprintTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 0, 0, 0, TimeSpan.Zero);
    private static readonly PersistenceScope Scope = new("tenant");
    private static readonly WorkflowExecutionPartition Partition = new("tenant");
    private static readonly WorkflowExecutableIdentity Pin = new("artifact", "definition", "version", "1", "hash");

    [Fact]
    public void An_absent_input_and_explicit_json_null_cannot_share_a_request_identity()
    {
        var absent = Request(null);
        var explicitNull = Request(JsonSerializer.SerializeToElement<object?>(null));

        Assert.False(absent.InputPresent);
        Assert.True(explicitNull.InputPresent);
        Assert.NotEqual(RuntimeCheckpointCommitFingerprint.ComputeIngressRequest(absent),
            RuntimeCheckpointCommitFingerprint.ComputeIngressRequest(explicitNull));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Input_presence_survives_the_normal_typed_json_round_trip(bool present)
    {
        var original = Request(present ? JsonSerializer.SerializeToElement<object?>(null) : null);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var restored = JsonSerializer.Deserialize<StimulusIngressRequest>(JsonSerializer.Serialize(original, options), options)!;

        Assert.Equal(present, restored.InputPresent);
        if (present) Assert.Equal(JsonValueKind.Null, restored.Input!.Value.ValueKind);
        else Assert.Null(restored.Input);
        Assert.Equal(RuntimeCheckpointCommitFingerprint.ComputeIngressRequest(original),
            RuntimeCheckpointCommitFingerprint.ComputeIngressRequest(restored));
    }

    [Fact]
    public void A_presence_marker_cannot_discard_a_supplied_nonnull_input()
    {
        Assert.Throws<ArgumentException>(() => new StimulusIngressRequest(1, "event", "hash", JsonSerializer.SerializeToElement(1),
            null, StimulusRoutingMode.StartAndResume, "test", new Dictionary<string, string>(), null, null, null,
            StimulusStartKeyScope.Artifact, false, null, inputPresent: false));
    }

    [Fact]
    public void Json_property_order_numeric_spelling_and_metadata_enumeration_do_not_change_semantic_identity()
    {
        using var firstInput = JsonDocument.Parse("{\"a\":1.0,\"b\":2}");
        using var secondInput = JsonDocument.Parse("{\"b\":2e0,\"a\":1}");
        var first = Request(firstInput.RootElement, new Dictionary<string, string> { ["a"] = "1", ["b"] = "2" });
        var second = Request(secondInput.RootElement, new Dictionary<string, string> { ["b"] = "2", ["a"] = "1" });

        Assert.Equal(RuntimeCheckpointCommitFingerprint.ComputeIngressRequest(first),
            RuntimeCheckpointCommitFingerprint.ComputeIngressRequest(second));
    }

    [Fact]
    public void Opaque_sender_keys_preserve_utf16_code_units_instead_of_replacing_lone_surrogates()
    {
        var origin = Origin();
        Assert.NotEqual(RuntimeCheckpointCommitFingerprint.ComputeIngressKey(origin, "key-\ud800"),
            RuntimeCheckpointCommitFingerprint.ComputeIngressKey(origin, "key-\ud801"));
    }

    [Fact]
    public void Equal_instants_in_full_origin_have_the_same_key_but_a_different_test_scope_does_not()
    {
        var first = Origin("test", Now.AddHours(1));
        var sameInstant = Origin("test", Now.AddHours(1).ToOffset(TimeSpan.FromHours(2)));
        var other = Origin("other-test", Now.AddHours(1));

        Assert.Equal(RuntimeCheckpointCommitFingerprint.ComputeIngressKey(first, "key"),
            RuntimeCheckpointCommitFingerprint.ComputeIngressKey(sameInstant, "key"));
        Assert.NotEqual(RuntimeCheckpointCommitFingerprint.ComputeIngressKey(first, "key"),
            RuntimeCheckpointCommitFingerprint.ComputeIngressKey(other, "key"));
    }

    [Fact]
    public void Published_start_keys_keep_the_normal_derivation_while_test_runs_do_not_alias_each_other()
    {
        var binding = new WorkflowTriggerBinding(WorkflowTriggerBinding.BuildId("artifact", "node", "hash"),
            "artifact", "definition", "1", "hash", "node", "event", "hash", null, new Dictionary<string, string>(), Now);
        var start = new StimulusStartRecipient(binding, Pin, sourceProvenance: null);
        var cohort = new StimulusIngressCohort(Scope, Now, [start], [], [], []);
        var published = new StimulusIngressSource(1, "source", Origin(), "key", Request(null), Now, cohort);
        var firstTest = new StimulusIngressSource(1, "test-source-1", Origin("first", Now.AddHours(1)), "key", Request(null), Now, cohort);
        var secondTest = new StimulusIngressSource(1, "test-source-2", Origin("second", Now.AddHours(1)), "key", Request(null), Now, cohort);

        Assert.Equal(KeyedWorkflowStartIdentity.For("key", "artifact").WorkflowExecutionId,
            published.GetStartIdentity(binding.TriggerBindingId).WorkflowExecutionId);
        Assert.NotEqual(firstTest.GetStartIdentity(binding.TriggerBindingId).WorkflowExecutionId,
            secondTest.GetStartIdentity(binding.TriggerBindingId).WorkflowExecutionId);
    }

    [Fact]
    public void Source_fingerprints_bind_the_original_selection_and_source_id_but_request_identity_does_not_bind_selection_time()
    {
        var first = new StimulusIngressSource(1, "source", Origin(), "key", Request(null), Now,
            new(Scope, Now, [], [], [], []));
        var later = new StimulusIngressSource(1, "source", Origin(), "key", Request(null), Now.AddSeconds(1),
            new(Scope, Now.AddSeconds(1), [], [], [], []));
        var other = new StimulusIngressSource(1, "other", Origin(), "key", Request(null), Now, first.Cohort);

        Assert.Equal(first.RequestFingerprint, later.RequestFingerprint);
        Assert.NotEqual(first.Cohort.Fingerprint, later.Cohort.Fingerprint);
        Assert.NotEqual(RuntimeCheckpointCommitFingerprint.ComputeIngressSource(first), RuntimeCheckpointCommitFingerprint.ComputeIngressSource(later));
        Assert.NotEqual(RuntimeCheckpointCommitFingerprint.ComputeIngressSource(first), RuntimeCheckpointCommitFingerprint.ComputeIngressSource(other));
    }

    [Fact]
    public void Generated_source_keys_cannot_alias_caller_keys_in_any_start_identity()
    {
        var binding = new WorkflowTriggerBinding(WorkflowTriggerBinding.BuildId("artifact", "node", "hash"),
            "artifact", "definition", "1", "hash", "node", "event", "hash", null, new Dictionary<string, string>(), Now);
        var cohort = new StimulusIngressCohort(Scope, Now, [new(binding, Pin, sourceProvenance: null)], [], [], []);
        var generated = new StimulusIngressSource(1, "source", Origin(), null, Request(null), Now, cohort)
            .GetStartIdentity(binding.TriggerBindingId);
        var caller = new StimulusIngressSource(1, "caller", Origin(), "runtime.ingress.source:source", Request(null), Now, cohort)
            .GetStartIdentity(binding.TriggerBindingId);

        Assert.NotEqual(generated.WorkflowExecutionId, caller.WorkflowExecutionId);
        Assert.NotEqual(generated.CommandId, caller.CommandId);
        Assert.NotEqual(generated.EnvelopeId, caller.EnvelopeId);
        Assert.NotEqual(generated.WorkflowExecutionId,
            KeyedWorkflowStartIdentity.ForOccurrence(generated.StartKey).WorkflowExecutionId);
        Assert.True(generated.IsArtifactScoped);
        Assert.Equal(KeyedWorkflowStartIdentity.IngressVersion, generated.DerivationVersion);
    }

    [Fact]
    public void Generated_and_scoped_caller_keys_have_separate_domains_within_v3()
    {
        var generated = KeyedWorkflowStartIdentity.ForIngressSource(Origin(), "key", "binding", "artifact");
        var caller = KeyedWorkflowStartIdentity.ForScopedIngressKey(Origin(), "key", "artifact");

        Assert.NotEqual(generated.StartKey, caller.StartKey);
        Assert.NotEqual(generated.WorkflowExecutionId, caller.WorkflowExecutionId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Ingress_start_identity_is_recognized_on_an_ordinary_dispatch_request(bool occurrence)
    {
        var original = KeyedWorkflowStartIdentity.ForScopedIngressKey(Origin("test", Now.AddHours(1)), "key",
            occurrence ? null : "artifact");
        var request = new WorkflowExecutionStartDispatchRequest("artifact", "test", original.WorkflowExecutionId, original.StartKey);
        var restored = KeyedWorkflowStartIdentity.TryGet(request);

        Assert.NotNull(restored);
        Assert.Equal(original.WorkflowExecutionId, restored.WorkflowExecutionId);
        Assert.Equal(original.CommandId, restored.CommandId);
        Assert.Equal(original.EnvelopeId, restored.EnvelopeId);
        Assert.Equal(!occurrence, restored.IsArtifactScoped);
    }

    [Fact]
    public void Scoped_occurrence_keys_are_stable_for_an_origin_and_distinct_between_origins()
    {
        var origin = Origin("test", Now.AddHours(1));
        var first = KeyedWorkflowStartIdentity.ForScopedIngressKey(origin, "key", null);
        var same = KeyedWorkflowStartIdentity.ForScopedIngressKey(Origin("test", Now.AddHours(1).ToOffset(TimeSpan.FromHours(2))), "key", null);
        var other = KeyedWorkflowStartIdentity.ForScopedIngressKey(Origin("other", Now.AddHours(1)), "key", null);

        Assert.Equal(first.WorkflowExecutionId, same.WorkflowExecutionId);
        Assert.NotEqual(first.WorkflowExecutionId, other.WorkflowExecutionId);
        Assert.False(first.IsArtifactScoped);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unkeyed_sources_start_each_binding_while_published_caller_keys_keep_per_artifact_identity(bool keyed)
    {
        var firstBinding = Binding("first");
        var secondBinding = Binding("second");
        var source = new StimulusIngressSource(1, "source", Origin(), keyed ? "key" : null, Request(null), Now,
            new(Scope, Now, [new(firstBinding, Pin, sourceProvenance: null), new(secondBinding, Pin, sourceProvenance: null)], [], [], []));
        var first = source.GetStartIdentity(firstBinding.TriggerBindingId);
        var second = source.GetStartIdentity(secondBinding.TriggerBindingId);
        if (keyed)
        {
            Assert.Equal(KeyedWorkflowStartIdentity.For("key", Pin.ArtifactId).WorkflowExecutionId, first.WorkflowExecutionId);
            Assert.Equal(first.WorkflowExecutionId, second.WorkflowExecutionId);
            Assert.Equal(first.CommandId, second.CommandId);
            Assert.Equal(first.EnvelopeId, second.EnvelopeId);
            Assert.Single(source.GetExecutionDependencies());
        }
        else
        {
            Assert.NotEqual(first.WorkflowExecutionId, second.WorkflowExecutionId);
            Assert.NotEqual(first.CommandId, second.CommandId);
            Assert.NotEqual(first.EnvelopeId, second.EnvelopeId);
            Assert.Equal(2, source.GetExecutionDependencies().Count);
            var restored = StimulusIngressSourceCodec.Decode(StimulusIngressSourceCodec.Encode(source));
            Assert.Equal(first.WorkflowExecutionId, restored.GetStartIdentity(firstBinding.TriggerBindingId).WorkflowExecutionId);
            Assert.Equal(second.WorkflowExecutionId, restored.GetStartIdentity(secondBinding.TriggerBindingId).WorkflowExecutionId);
        }

        static WorkflowTriggerBinding Binding(string node) => new(WorkflowTriggerBinding.BuildId(Pin.ArtifactId, node, "hash"),
            Pin.ArtifactId, Pin.DefinitionId, Pin.ArtifactVersion, Pin.ArtifactHash, node, "event", "hash", null,
            new Dictionary<string, string>(), Now);
    }

    [Fact]
    public void Binding_start_identity_preserves_opaque_utf16_and_structural_source_binding_boundaries()
    {
        var first = KeyedWorkflowStartIdentity.ForIngressSource(Origin(), "source", "binding-\ud800", "artifact");
        var second = KeyedWorkflowStartIdentity.ForIngressSource(Origin(), "source", "binding-\ud801", "artifact");
        var structured = KeyedWorkflowStartIdentity.ForIngressSource(Origin(), "source", "binding:\ud800", "artifact");
        var repartitioned = KeyedWorkflowStartIdentity.ForIngressSource(Origin(), "source:binding", "\ud800", "artifact");
        Assert.NotEqual(first.WorkflowExecutionId, second.WorkflowExecutionId);
        Assert.NotEqual(structured.WorkflowExecutionId, repartitioned.WorkflowExecutionId);
        Assert.Equal(first.WorkflowExecutionId,
            KeyedWorkflowStartIdentity.ForIngressSource(Origin(), "source", "binding-\ud800", "artifact").WorkflowExecutionId);
    }

    private static StimulusIngressRequest Request(JsonElement? input, IReadOnlyDictionary<string, string>? metadata = null) =>
        new(1, "event", "hash", input, null, StimulusRoutingMode.StartAndResume, "test", metadata ?? new Dictionary<string, string>(),
            null, null, null, StimulusStartKeyScope.Artifact, false, null);

    private static StimulusDeliveryScope Origin(string? test = null, DateTimeOffset? expires = null) =>
        new(Scope, "tenant", Partition, test is null ? WorkflowRunKind.PublishedRun : WorkflowRunKind.TestRun,
            test is null ? null : new(test, expires!.Value, "tenant", Partition));
}
