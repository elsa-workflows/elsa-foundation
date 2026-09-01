using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class WorkflowExecutableModelTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void LegacyExecutableConstructor_RepresentsMissingContractAndNoDependencies()
    {
        var executable = NewExecutable("root");

        Assert.Null(executable.InputContract);
        Assert.Empty(executable.Dependencies);
    }

    [Fact]
    public void BehavioralSnapshots_AreCanonicalAndDetachedFromCallerCollections()
    {
        var defaultDocument = JsonDocument.Parse("""{"value":"original"}""");
        var dispatchNodeIds = new List<string> { "dispatch-b", "dispatch-a", "dispatch-b" };
        var dependencies = new List<WorkflowExecutableDependency>
        {
            new("child-b", "hash-b", ["dispatch-b"]),
            new("child-a", "hash-a", dispatchNodeIds)
        };
        var inputs = new List<WorkflowDeclaredInput>
        {
            new("second", new TypeReference("String"), false),
            new("first", new TypeReference("Object"), true, defaultDocument.RootElement)
        };

        var executable = NewExecutable(
            "root",
            children: [NewNode("dispatch-a"), NewNode("dispatch-b")],
            inputContract: new WorkflowExecutableInputContract(WorkflowExecutableInputContract.CurrentVersion, inputs),
            dependencies: dependencies);

        dispatchNodeIds.Add("later-node");
        dependencies.Clear();
        inputs.Clear();
        defaultDocument.Dispose();

        var roundTripped = JsonSerializer.Deserialize<WorkflowExecutable>(
            JsonSerializer.Serialize(executable, SerializerOptions),
            SerializerOptions)!;

        Assert.Equal(["child-a", "child-b"], executable.Dependencies.Select(x => x.ArtifactId));
        Assert.Equal(["dispatch-a", "dispatch-b"], executable.Dependencies.First().DispatchNodeIds);
        Assert.Equal(["first", "second"], executable.InputContract!.Inputs.Select(x => x.Name));
        Assert.Equal("original", executable.InputContract.Inputs.First().DefaultValue!.Value.GetProperty("value").GetString());
        Assert.Equal(["child-a", "child-b"], roundTripped.Dependencies.Select(x => x.ArtifactId));
        Assert.Equal(["first", "second"], roundTripped.InputContract!.Inputs.Select(x => x.Name));
    }

    [Fact]
    public void Dependencies_RejectDuplicateArtifactsAndUnknownNodeBindings()
    {
        var duplicate = new[]
        {
            new WorkflowExecutableDependency("child", "hash", ["root"]),
            new WorkflowExecutableDependency("child", "hash", ["root"])
        };

        Assert.Throws<ArgumentException>(() => NewExecutable("root", dependencies: duplicate));
        Assert.Throws<ArgumentException>(() => NewExecutable(
            "root",
            dependencies: [new WorkflowExecutableDependency("child", "hash", ["missing"])]));
    }

    [Fact]
    public void InputContracts_RejectMalformedVersionsDeclarationsAndDefaults()
    {
        var input = new WorkflowDeclaredInput("message", new TypeReference("String"), false);

        Assert.Throws<ArgumentOutOfRangeException>(() => new WorkflowExecutableInputContract(0, [input]));
        Assert.Throws<ArgumentException>(() => new WorkflowExecutableInputContract(1, [input, input]));
        Assert.Throws<ArgumentException>(() => new WorkflowExecutableInputContract(1, [null!]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorkflowDeclaredInput(
            "message",
            new TypeReference("String", (CollectionKind)999),
            false));
        Assert.Throws<ArgumentException>(() => new WorkflowDeclaredInput(
            "message",
            new TypeReference("String"),
            false,
            new JsonElement()));
    }

    [Fact]
    public void BehavioralContracts_RejectBlankAndEmptyIdentityParts()
    {
        Assert.Throws<ArgumentException>(() => new WorkflowExecutableDependency(" ", "hash", ["node"]));
        Assert.Throws<ArgumentException>(() => new WorkflowExecutableDependency("artifact", " ", ["node"]));
        Assert.Throws<ArgumentException>(() => new WorkflowExecutableDependency("artifact", "hash", []));
        Assert.Throws<ArgumentException>(() => new WorkflowExecutableDependency("artifact", "hash", [" "]));
        Assert.Throws<ArgumentException>(() => new WorkflowDeclaredInput(" ", new TypeReference("String"), false));
        Assert.Throws<ArgumentException>(() => new WorkflowDeclaredInput("name", new TypeReference(" "), false));
    }

    [Fact]
    public void ExplicitEmptyVersionOneContract_RemainsDistinctFromLegacyNull()
    {
        var executable = NewExecutable(
            "root",
            inputContract: new WorkflowExecutableInputContract(WorkflowExecutableInputContract.CurrentVersion, []),
            dependencies: []);

        var restored = JsonSerializer.Deserialize<WorkflowExecutable>(
            JsonSerializer.Serialize(executable, SerializerOptions),
            SerializerOptions)!;

        Assert.NotNull(restored.InputContract);
        Assert.Equal(WorkflowExecutableInputContract.CurrentVersion, restored.InputContract.Version);
        Assert.Empty(restored.InputContract.Inputs);
    }

    [Fact]
    public void LegacyExecutableJson_DefaultsAdditiveMembers()
    {
        var json = JsonNode.Parse(JsonSerializer.Serialize(NewExecutable("root"), SerializerOptions))!.AsObject();
        json.Remove("inputContract");
        json.Remove("dependencies");

        var executable = JsonSerializer.Deserialize<WorkflowExecutable>(json, SerializerOptions)!;

        Assert.Null(executable.InputContract);
        Assert.Empty(executable.Dependencies);
    }

    [Fact]
    public void SourceReference_PreservesLegacyConstructorAndDefaultsMissingTenant()
    {
        Assert.NotNull(typeof(WorkflowExecutableSourceReference).GetConstructor(
        [
            typeof(string), typeof(string), typeof(string), typeof(string), typeof(string), typeof(string),
            typeof(string), typeof(string), typeof(DateTimeOffset), typeof(DateTimeOffset?),
            typeof(WorkflowExecutableReferenceScope), typeof(DateTimeOffset?), typeof(DateTimeOffset?),
            typeof(string), typeof(IReadOnlyList<WorkflowExecutableLayoutRecord>), typeof(string), typeof(string)
        ]));
        var reference = NewSourceReference("tenant-a");
        var json = JsonNode.Parse(JsonSerializer.Serialize(reference, SerializerOptions))!.AsObject();
        json.Remove("tenantId");

        var legacyReference = JsonSerializer.Deserialize<WorkflowExecutableSourceReference>(json, SerializerOptions)!;

        Assert.Equal("tenant-a", reference.TenantId);
        Assert.Null(legacyReference.TenantId);
    }

    [Fact]
    public void SourceReference_RoundTripsAndRetiresWithoutLosingTenant()
    {
        var reference = NewSourceReference("tenant-a") with { ActivationId = "activation-a" };
        var serialized = JsonSerializer.Serialize(reference, SerializerOptions);
        var json = JsonNode.Parse(serialized)!.AsObject();
        Assert.Equal("activation-a", json["activationId"]!.GetValue<string>());
        Assert.Null(json["publicationId"]);

        var restored = JsonSerializer.Deserialize<WorkflowExecutableSourceReference>(
            serialized,
            SerializerOptions)!;
        var retired = restored.Retire(DateTimeOffset.UnixEpoch.AddMinutes(1), "retired");

        Assert.Equal("tenant-a", restored.TenantId);
        Assert.Equal("activation-a", restored.ActivationId);
        Assert.Equal("tenant-a", retired.TenantId);
    }

    private static WorkflowExecutable NewExecutable(
        string rootNodeId,
        IReadOnlyCollection<ExecutableNode>? children = null,
        WorkflowExecutableInputContract? inputContract = null,
        IReadOnlyCollection<WorkflowExecutableDependency>? dependencies = null)
    {
        var root = NewNode(rootNodeId, children);
        return inputContract is null && dependencies is null
            ? new WorkflowExecutable(
                NewIdentity(),
                root,
                new Dictionary<string, WorkflowExecutableResumeTarget>(),
                DateTimeOffset.UnixEpoch,
                new Dictionary<string, string>(),
                IncidentStrategyBuiltIns.FaultReference)
            : new WorkflowExecutable(
                NewIdentity(),
                root,
                new Dictionary<string, WorkflowExecutableResumeTarget>(),
                DateTimeOffset.UnixEpoch,
                new Dictionary<string, string>(),
                inputContract,
                dependencies,
                IncidentStrategyBuiltIns.FaultReference);
    }

    private static ExecutableNode NewNode(string nodeId, IReadOnlyCollection<ExecutableNode>? children = null) =>
        new(
            nodeId,
            $"authored-{nodeId}",
            "test/activity",
            "1.0.0",
            new RuntimeActivityDescriptor(
                "test/descriptor",
                RuntimeActivityDescriptor.InitialSchemaVersion,
                JsonSerializer.SerializeToElement(new { })),
            new Dictionary<string, RuntimeInputBinding>(),
            new Dictionary<string, RuntimeOutputCapture>(),
            new Dictionary<string, string>(),
            children is null ? null : [new ExecutableChildSlot("Body", children)]);

    private static WorkflowExecutableIdentity NewIdentity() =>
        new("artifact", "definition", "version", "1", "hash");

    private static WorkflowExecutableSourceReference NewSourceReference(string tenantId) =>
        new(
            "reference",
            "artifact",
            "WorkflowDefinitionVersion",
            "version",
            "1",
            "definition",
            "version",
            "1",
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            WorkflowExecutableReferenceScope.Published,
            TenantId: tenantId);
}
