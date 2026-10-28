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
    public async Task A_native_only_provider_refuses_new_material_and_preserves_caller_cancellation()
    {
        IWorkflowExecutableStore store = new InMemoryWorkflowExecutableStore();
        using var definition = JsonDocument.Parse("{}");
        Assert.False(store.SupportsModelMaterial);
        await Assert.ThrowsAsync<NotSupportedException>(() => store.SaveModelAsync(NewMaterial(definition.RootElement)).AsTask());
        await Assert.ThrowsAsync<NotSupportedException>(() => store.FindMaterialAsync(Pin.ArtifactId).AsTask());
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveModelAsync(NewMaterial(definition.RootElement), cancellation.Token).AsTask());
        Assert.Null(await store.FindAsync(Pin.ArtifactId));
    }

    private static RuntimeModelMaterial NewMaterial(JsonElement definition, IReadOnlyList<RuntimeModelDependency>? dependencies = null,
        IReadOnlyList<RuntimeModelDeclaredOutput>? outputs = null) =>
        new(1, Pin, Protocol, "test/1", definition, new WorkflowExecutableInputContract(1, []), outputs ?? [], dependencies ?? [], []);
}
