using System.Linq.Expressions;
using System.Text.Json;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Persistence.Core;
using Elsa.Primitives.Entities;
using Elsa.Primitives.Persistence;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText.Services;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Runtime.Configuration;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Publishing.Api.Tests;

/// <summary>Builds an <see cref="IWellKnownTypeRegistry"/> seeded with the primitives these tests rely on.</summary>
internal static class TestWellKnownTypeRegistry
{
    public static IWellKnownTypeRegistry Create()
    {
        var registry = new WellKnownTypeRegistry();
        registry.RegisterType(typeof(string), "String");
        registry.RegisterType(typeof(int), "Int32");
        registry.RegisterType(typeof(bool), "Boolean");
        registry.RegisterType(typeof(object), "Object");
        registry.RegisterType(typeof(double), "Double");
        registry.RegisterType(typeof(long), "Int64");
        registry.RegisterType(typeof(decimal), "Decimal");
        registry.RegisterType(typeof(DateTime), "DateTime");
        registry.RegisterType(typeof(Guid), "Guid");
        return registry;
    }
}

/// <summary>
/// Constructs the <see cref="WorkflowExecutableCompiler"/> and its decomposition collaborators (W30b, #418)
/// for tests that exercise the compiler directly, keeping the collaborator wiring in one place.
/// </summary>
internal static class TestCompiler
{
    public static Elsa.Workflows.Publishing.Api.Services.WorkflowExecutableCompiler Create(
        Elsa.Workflows.Design.Persistence.Core.Stores.IWorkflowDefinitionVersionStore workflowVersions,
        Elsa.Activities.Design.Persistence.Core.Stores.IActivityDefinitionVersionStore activityVersions,
        Elsa.Workflows.Design.Core.Contracts.IActivityStructureService activityStructureService,
        IWellKnownTypeRegistry wellKnownTypeRegistry,
        IExecutableNodeMetadataEnricher? metadataEnricher = null) =>
        new(
            workflowVersions,
            activityVersions,
            new Elsa.Workflows.Publishing.Api.Services.WorkflowExecutableHasher(),
            new Elsa.Workflows.Publishing.Api.Services.ActivityTreeProjector(activityStructureService),
            new Elsa.Workflows.Publishing.Api.Services.ExecutableNodeCompiler(
                activityStructureService,
                wellKnownTypeRegistry,
                new Elsa.Workflows.Publishing.Api.Services.RuntimeInputBindingCompiler(wellKnownTypeRegistry)),
            metadataEnricher);
}

internal static class TestRootWriteLeases
{
    public static IWorkflowExecutableRootWriteLeaseManager Create(
        IWorkflowExecutableStore executableStore,
        TimeProvider? timeProvider = null) =>
        new WorkflowExecutableRootWriteLeaseManager(
            executableStore,
            Options.Create(new WorkflowExecutableGarbageCollectionOptions()),
            timeProvider ?? TimeProvider.System);
}

/// <summary>Reusable immutable executable/dependency builders for compiler and publication tests.</summary>
internal static class TestExecutable
{
    public static WorkflowExecutableIdentity Identity(
        string artifactId,
        string artifactHash,
        string definitionId = "definition-child",
        string definitionVersionId = "version-child") =>
        new(artifactId, definitionId, definitionVersionId, "1.0.0", artifactHash);

    public static WorkflowExecutable Create(
        WorkflowExecutableIdentity identity,
        params WorkflowExecutableDependency[] dependencies) =>
        new(
            identity,
            new ExecutableNode(
                executableNodeId: "node-root",
                authoredActivityId: "stored-root",
                activityType: "Test.Stored",
                activityTypeVersion: "1.0.0",
                descriptorType: "Test",
                descriptorPayload: JsonSerializer.SerializeToElement(new { }),
                inputBindings: new Dictionary<string, RuntimeInputBinding>(),
                outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
                metadata: new Dictionary<string, string>()),
            new Dictionary<string, WorkflowExecutableResumeTarget>(),
            DateTimeOffset.UtcNow,
            new Dictionary<string, string>(),
            inputContract: new WorkflowExecutableInputContract(WorkflowExecutableInputContract.CurrentVersion, []),
            dependencies);
}

/// <summary>A bare <see cref="IActivity"/> with one concrete-declared property, for projection assertions.</summary>
internal sealed class StubActivity : IActivity
{
    public string Greeting { get; set; } = "hello";

    public string Id { get; set; } = "act-1";
    public string NodeId { get; set; } = "node-1";
    public string? Name { get; set; }
    public string Type { get; set; } = "Stub";
    public string Version { get; set; } = "1.0.0";
    public Dictionary<string, object> CustomProperties { get; set; } = new() { ["author"] = "joey" };
    public Dictionary<string, object> SyntheticProperties { get; set; } = new() { ["WorkflowIdentity"] = "wf-123" };
    public Dictionary<string, object> Metadata { get; set; } = new();

    public ValueTask<bool> CanExecuteAsync(IActivityExecutionContext context) => ValueTask.FromResult(true);
    public ValueTask ExecuteAsync(IActivityExecutionContext context) => ValueTask.CompletedTask;
}

/// <summary>Captures what the bridge passed across the seam and returns a preset activity.</summary>
internal sealed class FakeActivityFactory(IActivity result) : IActivityFactory
{
    public string? LastDescriptorType { get; private set; }
    public JsonElement LastPayload { get; private set; }
    public IDictionary<string, InputArgument>? LastInputs { get; private set; }
    public IDictionary<string, OutputArgument>? LastOutputs { get; private set; }

    public ValueTask<IActivity> Create(
        string descriptorType,
        JsonElement payload,
        IDictionary<string, InputArgument>? inputs,
        IDictionary<string, OutputArgument>? outputs,
        CancellationToken cancellationToken = default)
    {
        LastDescriptorType = descriptorType;
        LastPayload = payload;
        LastInputs = inputs;
        LastOutputs = outputs;
        return ValueTask.FromResult(result);
    }
}

/// <summary>Minimal in-memory activity version read port: only the routes the bridge uses are real.</summary>
internal sealed class FakeActivityVersionStore(List<ActivityDefinitionVersion> items) : IActivityDefinitionVersionStore
{
    public Task<ActivityDefinitionVersion> GetAsync(string versionId, CancellationToken cancellationToken = default) =>
        Task.FromResult(items.Single(x => x.Id == versionId));

    public Task<ActivityDefinitionVersion> GetWithDefinitionAsync(string versionId, CancellationToken cancellationToken = default) =>
        Task.FromResult(items.FirstOrDefault(x => x.Id == versionId)
            ?? throw new ArgumentException($"Activity definition version with id '{versionId}' does not exist"));

    public Task<ActivityDefinitionVersion?> FindByDefinitionAndSortKeyAsync(string definitionId, string semVerSortKey, CancellationToken cancellationToken = default) =>
        Task.FromResult(items.FirstOrDefault(x => x.DefinitionId == definitionId && x.SemVerSortKey == semVerSortKey));

    public Task<IReadOnlyList<ActivityDefinitionVersion>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ActivityDefinitionVersion>>(items.Where(x => x.DefinitionId == definitionId).ToList());

    public Task<IReadOnlyList<ActivityDefinitionVersion>> ListByDefinitionIdsAsync(IEnumerable<string> definitionIds, CancellationToken cancellationToken = default)
    {
        var idSet = definitionIds.ToHashSet();
        return Task.FromResult<IReadOnlyList<ActivityDefinitionVersion>>(items.Where(x => idSet.Contains(x.DefinitionId)).ToList());
    }

    public Task<IReadOnlyList<ActivityDefinitionVersion>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ActivityDefinitionVersion>>(items);
}
