using System.Text;
using System.Text.Json;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa3.Activities.Design.Import.Models;
using Elsa3.Activities.Design.Import.Services;
using Elsa3.Mapping.Mappings;
using Elsa3.Mapping.Services;
using Elsa3.Models;
using Microsoft.Extensions.Options;

namespace Elsa3.Activities.Design.Import.Persistence.EntityFrameworkCore.Tests.Support;

/// <summary>Provider-neutral Elsa 3 collection fixtures shared by the SQLite and native-provider suites.</summary>
internal static class ImportFixtures
{
    public static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-17T10:00:00Z");

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

    public static Elsa3Activity Leaf(string nodeId) => new()
    {
        Id = $"activity-{nodeId}",
        NodeId = nodeId,
        Name = nodeId,
        Type = "Elsa.WriteLine",
        Version = 1,
        CustomProperties = new() { CanStartWorkflow = false }
    };

    public static Elsa3Activity Reference(string nodeId, string targetVersionId) => new()
    {
        Id = $"activity-{nodeId}",
        NodeId = nodeId,
        Name = nodeId,
        Type = "Elsa.Workflow",
        AdditionalProperties = new Dictionary<string, JsonElement>
        {
            ["workflowDefinitionVersionId"] = JsonSerializer.SerializeToElement(targetVersionId)
        }
    };

    public static MemoryStream Json(params Elsa3WorkflowDefinition[] definitions) =>
        new(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(definitions, new JsonSerializerOptions(JsonSerializerDefaults.Web))));

    public static IPayloadSerializer Serializer() => new JsonPayloadSerializer(new JsonPayloadConverterRegistry());

    public static IOptions<ReusableActivityImportOptions> Options() => Microsoft.Extensions.Options.Options.Create(new ReusableActivityImportOptions
    {
        MaximumUploadBytes = 64 * 1024,
        MaximumSourceVersions = 100,
        DefaultPageSize = 10,
        MaximumPageSize = 50,
        CollectionLifetime = TimeSpan.FromHours(1)
    });

    public static Elsa3ReusableActivityImportMaterializer Materializer()
    {
        var registry = new WellKnownTypeRegistry();
        registry.RegisterType(typeof(string), "String");
        registry.RegisterType(typeof(object), "Object");
        var argumentMapper = new Elsa3ArgumentDefinitionToInputOutput(registry);
        var stateMapper = new Elsa3WorkflowDefinitionToState(registry, new Elsa3ActivityToState(new BuiltInActivityLookup()), argumentMapper);
        return new(stateMapper, argumentMapper);
    }

    /// <summary>Materializes a scoped, keyed mutation the way the importer hands it to the command.</summary>
    public static async Task<ReusableActivityImportMutation> MutationAsync(
        ReusableActivityImportAccessScope scope,
        string idempotencyKey,
        params Elsa3WorkflowDefinition[] definitions)
    {
        var collection = new ReusableActivityImportCollection("fixture-collection", definitions);
        var plan = await new ReusableActivityCollectionAnalyzer().AnalyzeAsync(collection);
        var mutation = await Materializer().MaterializeAsync(collection, plan, plan.Items);
        foreach (var activity in mutation.Activities)
        {
            activity.Definition.TenantId = scope.TenantId;
            activity.Version.TenantId = scope.TenantId;
            activity.AuthoringState.TenantId = scope.TenantId;
        }
        foreach (var workflow in mutation.Workflows)
        {
            workflow.Definition.TenantId = scope.TenantId;
            workflow.Version.TenantId = scope.TenantId;
        }
        return mutation with { AccessScope = scope, IdempotencyKey = idempotencyKey };
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

internal sealed class MutableAccess(PersistenceAccessContext current) : IPersistenceAccessContextAccessor
{
    public PersistenceAccessContext Current { get; set; } = current;

    public static MutableAccess Tenant(string tenantId) => new(PersistenceAccessContext.Scoped(new PersistenceScope(tenantId)));
}

internal sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset current = now;
    public override DateTimeOffset GetUtcNow() => current;
    public void Advance(TimeSpan duration) => current = current.Add(duration);
}
