using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Primitives.Exceptions;

namespace Elsa.Workflows.Design.Tests.Unit.BaselineValidatorTests;

/// <summary>
/// In-memory <see cref="IActivityDefinitionLookup"/> for the catalog-consulting baseline
/// validators. <see cref="GetVersion"/> resolves only ids registered via <see cref="Add"/>;
/// everything else throws <see cref="EntityNotFoundException"/>, matching the production
/// version-store Get contract (both EFCore and Groundwork stores throw on a missing id —
/// they never return null). <see cref="ValidatorTestHelpers.RootActivityVersionId"/> is
/// pre-seeded (as an empty version) so tests exercise their real nodes, not the synthetic
/// root the test helpers fabricate for multi-activity graphs.
/// </summary>
internal sealed class StubActivityCatalog : IActivityDefinitionLookup
{
    private readonly Dictionary<string, IActivityDefinitionVersion> _versions = new();

    public StubActivityCatalog() => Add(ValidatorTestHelpers.RootActivityVersionId);

    public StubActivityCatalog Add(string versionId, IEnumerable<InputDefinition>? inputs = null, IEnumerable<OutputDefinition>? outputs = null)
    {
        _versions[versionId] = new StubVersion(versionId, inputs ?? [], outputs ?? []);
        return this;
    }

    public Task<IActivityDefinitionVersion> GetVersion(string versionId, CancellationToken cancellationToken = default)
        => _versions.TryGetValue(versionId, out var version)
            ? Task.FromResult(version)
            : throw EntityNotFoundException.ForEntity(typeof(IActivityDefinitionVersion), versionId);

    public Task<IActivityDefinitionVersion?> FindVersion(string versionId, CancellationToken cancellationToken = default)
        => Task.FromResult(_versions.GetValueOrDefault(versionId));

    public Task<IActivityDefinition> GetDefinition(string idOrActivityTypeKey, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<IEnumerable<IActivityDefinition>> ListDefinitions(string? id = null, string? category = null, string? searchTerm = null, string? displayName = null, string? description = null, bool? tenantAgnostic = null, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<IEnumerable<ActivityDefinitionVersionSummary>> ListVersions(string definitionId, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    private sealed class StubVersion(string id, IEnumerable<InputDefinition> inputs, IEnumerable<OutputDefinition> outputs) : IActivityDefinitionVersion
    {
        public string Id { get; } = id;
        public string Version => "1.0.0";
        public string DefinitionId => "def-1";
        public string ProviderKey => "test.provider";
        public string ProviderSchemaVersion => "1";
        public string ConsumerKey => "test.consumer";
        public string ConsumerSchemaVersion => "1";
        public System.Text.Json.JsonElement DescriptorPayload => default;
        public string SourceKind => "Test";
        public string SourceId => "Test";
        public IActivityDefinition Definition => null!;
        public IEnumerable<InputDefinition> Inputs { get; } = inputs;
        public IEnumerable<OutputDefinition> Outputs { get; } = outputs;
        public IEnumerable<ActivityDesignFacet> DesignFacets => [];
        public ActivityExecutionType ExecutionType => default;
        public string Hash => "";
    }
}
