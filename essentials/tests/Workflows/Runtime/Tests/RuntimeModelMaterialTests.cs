using System.Text.Json;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executables;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeModelMaterialTests
{
    private static readonly WorkflowExecutableIdentity Pin = new("artifact-model", "definition", "version", "1.0.0", "sha256:" + new string('a', 64));
    private static readonly RuntimeExecutionProtocol Protocol = new("test.semantic-model", 1);

    [Fact]
    public void Model_material_detaches_definition_and_binding_collections_without_a_native_root()
    {
        var dependencies = new List<RuntimeModelDependency> { new("service-b", Pin) };
        var outputs = new List<RuntimeModelDeclaredOutput> { new("result", new TypeReference("String")) };
        RuntimeModelMaterial material;
        using (var definition = JsonDocument.Parse("{\"process\":\"held\"}"))
            material = NewMaterial(definition.RootElement, dependencies, outputs);
        dependencies.Clear(); outputs.Clear();
        Assert.Equal("held", material.Definition.GetProperty("process").GetString());
        Assert.Equal("service-b", Assert.Single(material.Dependencies).BindingId);
        Assert.Equal("result", Assert.Single(material.Outputs).Name);
        var snapshot = new RuntimeExecutionMaterial(material, "physical-incarnation");
        Assert.Null(snapshot.NativeWorkflow);
        Assert.Same(material, snapshot.Model);
        Assert.Equal(Pin, snapshot.Identity);
        Assert.Equal("physical-incarnation", snapshot.IncarnationId);
    }

    [Fact]
    public void Ambiguous_dependency_bindings_and_unknown_material_formats_refuse_before_persistence()
    {
        using var definition = JsonDocument.Parse("{}");
        Assert.Throws<ArgumentException>(() => NewMaterial(definition.RootElement, [new("same", Pin), new("same", Pin)]));
        Assert.Throws<NotSupportedException>(() => new RuntimeModelMaterial(2, Pin, Protocol, "test/1", definition.RootElement,
            new WorkflowExecutableInputContract(1, []), [], [], []));
        Assert.Throws<ArgumentException>(() => new RuntimeExecutionMaterial(NewMaterial(definition.RootElement), " "));
    }

    [Fact]
    public async Task A_native_only_provider_reads_native_material_but_refuses_model_writes_and_preserves_cancellation()
    {
        IWorkflowExecutableStore store = new InMemoryWorkflowExecutableStore();
        using var definition = JsonDocument.Parse("{}");
        Assert.False(store.SupportsModelMaterial);
        await Assert.ThrowsAsync<NotSupportedException>(() => store.SaveModelAsync(NewMaterial(definition.RootElement)).AsTask());
        // Native material reads now expose an actual stored-row incarnation for activation. This
        // does not enable typed model writes or manufacture native material for a missing/model row.
        Assert.Null(await store.FindMaterialAsync(Pin.ArtifactId));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveModelAsync(NewMaterial(definition.RootElement), cancellation.Token).AsTask());
        var readCancellation = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.FindMaterialAsync(Pin.ArtifactId, cancellation.Token).AsTask());
        Assert.Equal(cancellation.Token, readCancellation.CancellationToken);
        Assert.Null(await store.FindAsync(Pin.ArtifactId));
    }

    [Fact]
    public void Model_hash_canonicalizes_definition_keys_and_tracks_protocol_without_source_attribution()
    {
        using var first = JsonDocument.Parse("{\"start\":\"human\",\"count\":2}");
        using var reordered = JsonDocument.Parse("{\"count\":2,\"start\":\"human\"}");
        var material = NewMaterial(first.RootElement);
        Assert.Equal(material.ComputeContentHash(), NewMaterial(reordered.RootElement).ComputeContentHash());
        var otherSource = new RuntimeModelMaterial(1, Pin with { DefinitionId = "other-source" }, Protocol, "test/1", first.RootElement,
            new WorkflowExecutableInputContract(1, []), [], [], []);
        Assert.Equal(material.ComputeContentHash(), otherSource.ComputeContentHash());
        var otherProtocol = new RuntimeModelMaterial(1, Pin, new(Protocol.Key, 2), "test/1", first.RootElement,
            new WorkflowExecutableInputContract(1, []), [], [], []);
        Assert.NotEqual(material.ComputeContentHash(), otherProtocol.ComputeContentHash());
    }

    [Fact]
    public void Model_hash_preserves_opaque_protocol_code_units()
    {
        using var definition = JsonDocument.Parse("{}");
        var first = new RuntimeModelMaterial(1, Pin, new("test-\ud800", 1), "test/1", definition.RootElement,
            new WorkflowExecutableInputContract(1, []), [], [], []);
        var second = new RuntimeModelMaterial(1, Pin, new("test-\ud801", 1), "test/1", definition.RootElement,
            new WorkflowExecutableInputContract(1, []), [], [], []);
        Assert.NotEqual(first.ComputeContentHash(), second.ComputeContentHash());
    }

    private static RuntimeModelMaterial NewMaterial(JsonElement definition, IReadOnlyList<RuntimeModelDependency>? dependencies = null,
        IReadOnlyList<RuntimeModelDeclaredOutput>? outputs = null) =>
        new(1, Pin, Protocol, "test/1", definition, new WorkflowExecutableInputContract(1, []), outputs ?? [], dependencies ?? [], []);
}
