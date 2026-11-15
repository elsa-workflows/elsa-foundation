using System.Text.Json;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;

namespace Elsa.Activities.Graph.Tests;

/// <summary>
/// In-memory <see cref="IActivityDefinitionLookup"/> that maps catalog version ids to activity type keys, which is
/// all the graph provider and the Complete draft validator read from the catalog.
/// </summary>
internal sealed class TypeKeyActivityCatalog : IActivityDefinitionLookup
{
    private readonly Dictionary<string, IActivityDefinitionVersion> _versions = new(StringComparer.Ordinal);

    public TypeKeyActivityCatalog Add(string versionId, string activityTypeKey)
    {
        _versions[versionId] = new CatalogVersion(versionId, new Definition(activityTypeKey));
        return this;
    }

    public Task<IActivityDefinitionVersion?> FindVersion(string versionId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_versions.GetValueOrDefault(versionId));

    public Task<IActivityDefinitionVersion> GetVersion(string versionId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_versions[versionId]);

    public Task<IActivityDefinition> GetDefinition(string idOrActivityTypeKey, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<IEnumerable<IActivityDefinition>> ListDefinitions(string? id = null, string? category = null, string? searchTerm = null, string? displayName = null, string? description = null, bool? tenantAgnostic = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<IEnumerable<ActivityDefinitionVersionSummary>> ListVersions(string definitionId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    private sealed record Definition(string ActivityTypeKey) : IActivityDefinition
    {
        public string Id => $"definition-{ActivityTypeKey}";
        public string Category => "Test";
        public string? DisplayName => null;
        public string? Description => null;
    }

    private sealed class CatalogVersion(string id, IActivityDefinition definition) : IActivityDefinitionVersion
    {
        public string Id { get; } = id;
        public string Version => "1.0.0";
        public string DefinitionId => Elsa.Activities.Design.Core.Reconciliation.ActivityCatalogStableIds.DefinitionId(Definition.ActivityTypeKey)!;
        public string ProviderKey => "test.provider";
        public string ProviderSchemaVersion => "1";
        public string ConsumerKey => "test.consumer";
        public string ConsumerSchemaVersion => "1";
        public JsonElement DescriptorPayload => default;
        public string SourceKind => "Test";
        public string SourceId => "Test";
        public IActivityDefinition Definition { get; } = definition;
        public IEnumerable<InputDefinition> Inputs => [];
        public IEnumerable<OutputDefinition> Outputs => [];
        public IEnumerable<ActivityDesignFacet> DesignFacets => [];
        public ActivityExecutionType ExecutionType => default;
        public string? Hash => null;
    }
}
