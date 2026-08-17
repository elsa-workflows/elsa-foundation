using System.Text.Json;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Reconciliation.Contracts;
using Elsa.Workflows.Runtime.Reconciliation.Core.Exceptions;
using Elsa.Workflows.Runtime.Reconciliation.Core.Models;
using Elsa.Workflows.Runtime.Reconciliation.Services;
using Elsa.Workflows.Runtime.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Workflows.Runtime.Reconciliation.Tests;

/// <summary>
/// The closure envelope's wire contract (T049/T051): what the codec writes, what it refuses to write, and what the
/// reader refuses to accept.
/// </summary>
/// <remarks>
/// The envelope is the only artifact shape that crosses engines, so its bytes are a contract rather than an
/// implementation detail. Two properties are pinned here: an artifact survives a full encode/decode with its
/// behavior intact, and the encoded form carries no projection the receiving constructor is going to recompute
/// anyway — the discipline the durable runtime document serializer already applies, so that the same artifact has
/// the same shape whether it came out of a store or off a wire.
/// </remarks>
public sealed class WorkflowArtifactClosureEnvelopeTests : IDisposable
{
    private readonly IWorkflowArtifactClosureSerializer _codec =
        new WorkflowArtifactClosureSerializer(new JsonPayloadSerializer(new JsonPayloadConverterRegistry()));

    private readonly IPayloadSerializer _plainSerializer = new JsonPayloadSerializer(new JsonPayloadConverterRegistry());

    private readonly string _scratch = Path.Combine(
        Path.GetTempPath(),
        "elsa-closure-envelope",
        Guid.NewGuid().ToString("N"));

    public WorkflowArtifactClosureEnvelopeTests() => Directory.CreateDirectory(_scratch);

    public void Dispose()
    {
        if (Directory.Exists(_scratch))
            Directory.Delete(_scratch, true);
    }

    [Fact]
    public void A_closure_round_trips_with_its_artifacts_identities_and_dependency_edges_intact()
    {
        var child = ArtifactClosureFixture.Executable(ArtifactClosureFixture.ProbeNode("node-child"), "definition-child");
        var parent = ArtifactClosureFixture.Executable(
            ArtifactClosureFixture.ProbeNode("node-parent"),
            "definition-parent",
            "2.1.0",
            ArtifactClosureFixture.DependencyOn(child, "node-parent"));
        var closure = ArtifactClosureFixture.Closure(parent, child);

        var decoded = _codec.Deserialize(_codec.Serialize(closure));

        Assert.NotNull(decoded);
        Assert.Equal(WorkflowArtifactClosureFormat.CurrentVersion, decoded!.FormatVersion);
        Assert.Equal(parent.Identity.ArtifactId, decoded.RootArtifactId);
        Assert.Equal(
            [parent.Identity.ArtifactId, child.Identity.ArtifactId],
            decoded.Artifacts.Select(artifact => artifact.Identity.ArtifactId).ToArray());

        var decodedParent = decoded.Artifacts[0];
        Assert.Equal(parent.Identity.ArtifactHash, decodedParent.Identity.ArtifactHash);
        Assert.Equal(parent.Identity.ArtifactVersion, decodedParent.Identity.ArtifactVersion);
        Assert.Equal(parent.RootActivity.ExecutableNodeId, decodedParent.RootActivity.ExecutableNodeId);
        Assert.Equal(parent.RootActivity.DescriptorType, decodedParent.RootActivity.DescriptorType);
        Assert.NotNull(decodedParent.RootActivity.ActivityContract);
        var edge = Assert.Single(decodedParent.Dependencies);
        Assert.Equal(child.Identity.ArtifactId, edge.ArtifactId);
        Assert.Equal(child.Identity.ArtifactHash, edge.ArtifactHash);
        Assert.Equal("node-parent", Assert.Single(edge.DispatchNodeIds));
    }

    [Fact]
    public void A_round_tripped_closure_re_encodes_to_the_same_bytes()
    {
        // Content addressing only means anything if encoding is a function of content. A decode that lost or
        // reordered anything the encoder writes would show up here before it showed up as a hash mismatch.
        var closure = ArtifactClosureFixture.Closure(
            ArtifactClosureFixture.Executable(ArtifactClosureFixture.ProbeNode("node-root"), "definition-invoice"));

        var first = _codec.Serialize(closure);
        var second = _codec.Serialize(_codec.Deserialize(first)!);

        Assert.Equal(first, second);
    }

    [Fact]
    public void The_carried_provenance_collections_round_trip_as_written()
    {
        var executable = ArtifactClosureFixture.Executable(
            ArtifactClosureFixture.AsStartTrigger(ArtifactClosureFixture.ProbeNode("node-trigger")),
            "definition-onboarding");
        var closure = ArtifactClosureFixture.ClosureWithCarriedBindings(
            executable,
            "node-trigger",
            ArtifactClosureFixture.TriggerStimulusHash("node-trigger"));

        var decoded = _codec.Deserialize(_codec.Serialize(closure))!;

        var binding = Assert.Single(decoded.TriggerBindings);
        Assert.Equal("exporter-activation", binding.ActivationId);
        Assert.Equal("exporter-slot", binding.SlotId);
        Assert.Equal(ArtifactClosureFixture.TriggerStimulusType, binding.StimulusType);
        Assert.Empty(decoded.SourceReferences);
    }

    [Fact]
    public void The_encoded_artifact_carries_no_recomputed_node_projections()
    {
        // The projection drop: WorkflowExecutable rebuilds Nodes/NodesById by flattening RootActivity, so shipping
        // them would duplicate the whole graph AND make an exported artifact differ, byte for byte, from the same
        // artifact serialized into the durable store — which drops them through the same discipline.
        var closure = ArtifactClosureFixture.Closure(
            ArtifactClosureFixture.Executable(ArtifactClosureFixture.ProbeNode("node-root"), "definition-invoice"));

        using var document = JsonDocument.Parse(_codec.Serialize(closure));
        var artifact = document.RootElement.GetProperty("artifacts")[0];

        Assert.False(artifact.TryGetProperty("nodes", out _));
        Assert.False(artifact.TryGetProperty("nodesById", out _));

        // The content the projections were derived FROM is still there — this is a drop, not a truncation.
        Assert.Equal("node-root", artifact.GetProperty("rootActivity").GetProperty("executableNodeId").GetString());
        Assert.True(artifact.TryGetProperty("identity", out _));
    }

    [Fact]
    public void The_drop_is_the_codecs_doing_and_not_an_accident_of_the_model()
    {
        // Guards the test above against passing vacuously: encoded through the bare payload serializer the same
        // closure DOES carry both projections, so the assertions above are measuring the added modifier.
        var closure = ArtifactClosureFixture.Closure(
            ArtifactClosureFixture.Executable(ArtifactClosureFixture.ProbeNode("node-root"), "definition-invoice"));

        using var document = JsonDocument.Parse(_plainSerializer.Serialize(closure));
        var artifact = document.RootElement.GetProperty("artifacts")[0];

        Assert.True(artifact.TryGetProperty("nodes", out _));
        Assert.True(artifact.TryGetProperty("nodesById", out _));
    }

    [Fact]
    public void A_decoded_artifact_has_its_node_projections_rebuilt()
    {
        // The other half of the drop's correctness: nothing downstream loses the index, because the constructor
        // is what owns it.
        var executable = ArtifactClosureFixture.Executable(ArtifactClosureFixture.ProbeNode("node-root"), "definition-invoice");

        var decoded = _codec.Deserialize(_codec.Serialize(ArtifactClosureFixture.Closure(executable)))!;

        var artifact = Assert.Single(decoded.Artifacts);
        Assert.Equal(executable.Nodes.Count, artifact.Nodes.Count);
        Assert.True(artifact.NodesById.ContainsKey("node-root"));
    }

    [Fact]
    public void An_artifact_survives_the_round_trip_with_the_same_content_hash()
    {
        // The invariant the importer's step-2a gate enforces, asserted at the codec level so a shape regression
        // surfaces here rather than as an unexplained "broken source" three layers away.
        var executable = ArtifactClosureFixture.Executable(ArtifactClosureFixture.ProbeNode("node-root"), "definition-invoice");
        var hasher = new WorkflowExecutableHasher();

        var decoded = Assert.Single(_codec.Deserialize(_codec.Serialize(ArtifactClosureFixture.Closure(executable)))!.Artifacts);

        Assert.Equal(
            executable.Identity.ArtifactHash,
            hasher.ComputeHash(
                decoded.RootActivity,
                decoded.InputContract!,
                decoded.Dependencies,
                checkpointCadence: decoded.CheckpointCadence,
                workflowVariables: decoded.WorkflowVariables,
                incidentStrategy: decoded.IncidentStrategy));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(99)]
    [InlineData(0)]
    [InlineData(-1)]
    public void An_unsupported_format_version_is_rejected_loudly(int formatVersion)
    {
        // No silent upcast and no partial import: for a content-addressed store that is create-only, a wrong guess
        // about a producer this build has never seen becomes that id's content permanently.
        var path = WriteEnvelope("unsupported.json", formatVersion);

        var exception = Assert.Throws<InvalidWorkflowArtifactClosureException>(() => NewReader().Read(path));

        Assert.Equal(path, exception.Origin);
        Assert.Contains($"format version {formatVersion}", exception.Message, StringComparison.Ordinal);
        Assert.Contains("not supported", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_current_format_version_is_accepted()
    {
        var path = WriteEnvelope("current.json", WorkflowArtifactClosureFormat.CurrentVersion);

        var closure = NewReader().Read(path);

        Assert.Equal(WorkflowArtifactClosureFormat.CurrentVersion, closure.FormatVersion);
        Assert.True(WorkflowArtifactClosureFormat.IsSupported(closure.FormatVersion));
    }

    [Fact]
    public void A_truncated_envelope_surfaces_as_a_named_closure_failure_preserving_the_json_error()
    {
        // §2.23.5: the reader owns the path, so it is the boundary that wraps — no raw JsonException escapes.
        var path = Path.Combine(_scratch, "truncated.json");
        File.WriteAllText(path, """{"formatVersion": 1, "rootArtifactId": "artifact-1", "artifacts": [""");

        var exception = Assert.Throws<InvalidWorkflowArtifactClosureException>(() => NewReader().Read(path));

        Assert.Equal(path, exception.Origin);
        Assert.IsAssignableFrom<JsonException>(exception.InnerException);
    }

    [Fact]
    public void An_envelope_that_declares_no_root_artifact_is_rejected()
    {
        var executable = ArtifactClosureFixture.Executable(ArtifactClosureFixture.ProbeNode("node-root"), "definition-invoice");
        var closure = new WorkflowArtifactClosure(
            WorkflowArtifactClosureFormat.CurrentVersion,
            RootArtifactId: null!,
            [executable],
            [],
            []);

        var plan = WorkflowArtifactClosurePlanner.Plan(closure);

        Assert.False(plan.IsValid);
        Assert.Equal(WorkflowArtifactRejectionKind.MalformedClosure, plan.RejectionKind);
        Assert.Contains("no root artifact id", plan.Diagnostic!, StringComparison.Ordinal);
    }

    [Fact]
    public void An_envelope_whose_declared_root_is_not_among_its_artifacts_is_rejected()
    {
        var executable = ArtifactClosureFixture.Executable(ArtifactClosureFixture.ProbeNode("node-root"), "definition-invoice");
        var closure = new WorkflowArtifactClosure(
            WorkflowArtifactClosureFormat.CurrentVersion,
            RootArtifactId: "artifact-that-was-never-shipped",
            [executable],
            [],
            []);

        var plan = WorkflowArtifactClosurePlanner.Plan(closure);

        Assert.False(plan.IsValid);
        Assert.Equal(WorkflowArtifactRejectionKind.MalformedClosure, plan.RejectionKind);
        Assert.Contains("artifact-that-was-never-shipped", plan.Diagnostic!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_root_artifact_id_omitted_from_the_wire_decodes_to_the_empty_string_rather_than_null()
    {
        // The normalization exists so the refusal above is a named diagnostic instead of a null dereference
        // somewhere further down the pipeline.
        var decoded = _codec.Deserialize("""{"formatVersion":1,"artifacts":[]}""");

        Assert.NotNull(decoded);
        Assert.Equal(string.Empty, decoded!.RootArtifactId);
        Assert.Empty(decoded.Artifacts);
        Assert.Empty(decoded.SourceReferences);
        Assert.Empty(decoded.TriggerBindings);
    }

    private JsonWorkflowArtifactClosureReader NewReader() =>
        new(_codec, NullLogger<JsonWorkflowArtifactClosureReader>.Instance);

    private string WriteEnvelope(string fileName, int formatVersion)
    {
        var executable = ArtifactClosureFixture.Executable(ArtifactClosureFixture.ProbeNode("node-root"), "definition-invoice");
        var path = Path.Combine(_scratch, fileName);
        File.WriteAllText(
            path,
            _codec.Serialize(new WorkflowArtifactClosure(
                formatVersion,
                executable.Identity.ArtifactId,
                [executable],
                [],
                [])));
        return path;
    }
}
