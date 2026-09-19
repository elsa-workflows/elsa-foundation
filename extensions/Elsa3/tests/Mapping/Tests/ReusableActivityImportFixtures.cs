using System.Text.Json;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Serialization.SystemText.Services;
using Elsa.Serialization.Core;
using Elsa3.Activities.Design.Import.Models;
using Elsa3.Mapping.Mappings;
using Elsa3.Mapping.Services;
using Elsa3.Models;

namespace Elsa3.Mapping.Tests;

internal static class ReusableActivityImportFixtures
{
    public static Elsa3WorkflowDefinition Workflow(
        string definitionId,
        string versionId,
        int version,
        bool reusable,
        Elsa3Activity root,
        params Elsa3WorkflowArgumentDefinition[] inputs) => new()
        {
            Id = versionId,
            DefinitionId = definitionId,
            Name = $"Workflow {definitionId}",
            Description = $"Description {definitionId}",
            Version = version,
            CreatedAt = DateTimeOffset.UnixEpoch.AddDays(version),
            Options = new() { UsableAsActivity = reusable },
            Inputs = inputs,
            Outputs = [],
            Root = root
        };

    public static Elsa3Activity Leaf(string nodeId, string type = "Elsa.WriteLine", bool directStart = false) => new()
    {
        Id = $"activity-{nodeId}",
        NodeId = nodeId,
        Name = nodeId,
        Type = type,
        Version = 1,
        CustomProperties = new() { CanStartWorkflow = directStart }
    };

    public static Elsa3Activity Reference(
        string nodeId,
        string? targetVersionId = null,
        string? targetDefinitionId = null,
        int? targetVersion = null) => new()
    {
        Id = $"activity-{nodeId}",
        NodeId = nodeId,
        Name = nodeId,
        Type = targetDefinitionId ?? "Elsa.Workflow",
        Version = targetVersion,
        AdditionalProperties = new Dictionary<string, JsonElement>
        {
            [targetVersionId is null ? "workflowDefinitionId" : "workflowDefinitionVersionId"] =
                JsonSerializer.SerializeToElement(targetVersionId ?? targetDefinitionId)
        }
    };

    public static ReusableActivityImportCollection Collection(params Elsa3WorkflowDefinition[] definitions) =>
        new("fixture-collection", definitions);

    public static Elsa3ReusableActivityImportMaterializer Materializer()
    {
        var registry = new WellKnownTypeRegistry();
        registry.RegisterType(typeof(string), "String");
        registry.RegisterType(typeof(object), "Object");
        var argumentMapper = new Elsa3ArgumentDefinitionToInputOutput(registry);
        var activityMapper = new Elsa3ActivityToState(new BuiltInActivityLookup());
        var stateMapper = new Elsa3WorkflowDefinitionToState(registry, activityMapper, argumentMapper);
        return new(stateMapper, argumentMapper);
    }

    private sealed class BuiltInActivityLookup : IActivityDefinitionLookup
    {
        private readonly IActivityDefinition definition = new Definition();
        private readonly IActivityDefinitionVersion version = new VersionModel();

        public Task<IActivityDefinition> GetDefinition(string idOrActivityTypeKey, CancellationToken cancellationToken = default) => Task.FromResult(definition);
        public Task<IEnumerable<IActivityDefinition>> ListDefinitions(string? id = null, string? category = null, string? searchTerm = null, string? displayName = null, string? description = null, bool? tenantAgnostic = null, CancellationToken cancellationToken = default) => Task.FromResult<IEnumerable<IActivityDefinition>>([definition]);
        public Task<IActivityDefinitionVersion> GetVersion(string versionId, CancellationToken cancellationToken = default) => Task.FromResult(version);
        public Task<IActivityDefinitionVersion?> FindVersion(string versionId, CancellationToken cancellationToken = default) => Task.FromResult<IActivityDefinitionVersion?>(version);
        public Task<IEnumerable<ActivityDefinitionVersionSummary>> ListVersions(string definitionId, CancellationToken cancellationToken = default) => Task.FromResult<IEnumerable<ActivityDefinitionVersionSummary>>([new("builtin-version", "1.0.0", DateTimeOffset.UnixEpoch, ActivityExecutionType.Action)]);

        private sealed record Definition : IActivityDefinition
        {
            public string Id => "builtin-definition";
            public string ActivityTypeKey => "Elsa.WriteLine";
            public string Category => "Test";
            public string? DisplayName => "Built in";
            public string? Description => null;
        }

        private sealed record VersionModel : IActivityDefinitionVersion
        {
            public string Id => "builtin-version";
            public string Version => "1.0.0";
            public string DefinitionId => "builtin-definition";
            public string ProviderKey => "elsa.clr";
            public string ProviderSchemaVersion => "1";
            public string ConsumerKey => "elsa.clr";
            public string ConsumerSchemaVersion => "1";
            public JsonElement DescriptorPayload => JsonSerializer.SerializeToElement(new { });
            public string SourceKind => "CLR";
            public string SourceId => "Elsa.WriteLine";
            public IActivityDefinition Definition => new Definition();
            public IEnumerable<InputDefinition> Inputs => [];
            public IEnumerable<OutputDefinition> Outputs => [];
            public IEnumerable<ActivityDesignFacet> DesignFacets => [];
            public ActivityExecutionType ExecutionType => ActivityExecutionType.Action;
            public string? Hash => "builtin";
        }
    }
}
