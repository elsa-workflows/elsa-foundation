using System.Text.Json;
using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Handlers;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Filters;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Xunit;

namespace Elsa.Activities.Design.Tests.Unit;

public sealed class AddVersionCommandHandlerTests
{
    private readonly StubDefinitionStore _definitionStore = new();
    private readonly RecordingAddCommand _addCommand = new();

    private static AddVersion Command(string version) =>
        new("operation-key", "def-1", version, "test.provider", "1", "test.consumer", "1", default, null, null, null, null);

    private AddVersionCommandHandler CreateHandler(StubVersionStore versionStore) =>
        new(new StubVersionFactory(), _addCommand, versionStore, _definitionStore);

    [Fact]
    public async Task The_handler_delegates_collision_and_replay_semantics_to_the_literal_command()
    {
        var versionStore = new StubVersionStore();
        var handler = CreateHandler(versionStore);

        await handler.Handle(Command("1.0.0"), CancellationToken.None);

        var added = Assert.Single(_addCommand.Added);
        Assert.Equal("operation-key", added.OperationKey.Value);
    }

    [Fact]
    public async Task Adding_a_version_whose_sortkey_is_free_proceeds()
    {
        var versionStore = new StubVersionStore();
        var handler = CreateHandler(versionStore);

        var result = await handler.Handle(Command("1.0.0"), CancellationToken.None);

        Assert.Equal("1.0.0", Assert.Single(_addCommand.Added).Version.Version);
        Assert.Equal("1.0.0", result.Version);
    }

    private sealed class StubDefinitionStore : IActivityDefinitionStore
    {
        public Task<ActivityDefinition> GetAsync(string id, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ActivityDefinition { Id = id, ActivityTypeKey = $"type-{id}", Category = "General" });

        public Task<ActivityDefinition?> FindAsync(ActivityDefinitionFilter filter, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ActivityDefinition>> ListAsync(ActivityDefinitionFilter filter, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ActivityDefinition?> FindByIdOrActivityTypeKeyAsync(string id, string activityTypeKey, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> ExistsByActivityTypeKeyAsync(string activityTypeKey, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class StubVersionStore : IActivityDefinitionVersionStore
    {
        public HashSet<string> ExistingSortKeys { get; } = [];

        public Task<ActivityDefinitionVersion> GetAsync(string versionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ActivityDefinitionVersion> GetWithDefinitionAsync(string versionId, CancellationToken cancellationToken = default)
        {
            // The stub factory generates ids as "v-{version}"; mirror the added version back with an
            // attached definition so the details-view projection can render it.
            var version = versionId.StartsWith("v-") ? versionId[2..] : "1.0.0";
            var entity = new ActivityDefinitionVersion(version, "def-1")
            {
                Id = versionId,
                ProviderKey = "test.provider",
                ProviderSchemaVersion = "1",
                ConsumerKey = "test.consumer",
                ConsumerSchemaVersion = "1",
                Definition = new ActivityDefinition { Id = "def-1", ActivityTypeKey = "type-def-1", Category = "General" },
            };
            return Task.FromResult(entity);
        }

        public Task<ActivityDefinitionVersion?> FindByDefinitionAndSortKeyAsync(string definitionId, string semVerSortKey, CancellationToken cancellationToken = default)
        {
            if (!ExistingSortKeys.Contains(semVerSortKey))
                return Task.FromResult<ActivityDefinitionVersion?>(null);

            var existing = new ActivityDefinitionVersion("1.0.0", definitionId) { Id = "existing" };
            return Task.FromResult<ActivityDefinitionVersion?>(existing);
        }

        public Task<IReadOnlyList<ActivityDefinitionVersion>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ActivityDefinitionVersion>> ListByDefinitionIdsAsync(IEnumerable<string> definitionIds, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ActivityDefinitionVersion>> ListAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingAddCommand : IAddActivityDefinitionVersionCommand
    {
        public List<(DesignOperationKey OperationKey, ActivityDefinitionVersion Version)> Added { get; } = [];

        public Task<ActivityDefinitionVersionAdded> Execute(
            DesignOperationKey operationKey,
            ActivityDefinitionVersion version,
            CancellationToken cancellationToken = default)
        {
            Added.Add((operationKey, version));
            return Task.FromResult(new ActivityDefinitionVersionAdded(
                version.DefinitionId,
                version.Id,
                version.Version,
                version.Hash));
        }
    }

    private sealed class StubVersionFactory : IActivityDefinitionVersionFactory
    {
        public IActivityDefinitionVersion Create(
            IActivityDefinition definition,
            string version,
            string providerKey,
            string providerSchemaVersion,
            string consumerKey,
            string consumerSchemaVersion,
            JsonElement descriptorPayload,
            string sourceKind,
            string sourceId,
            IEnumerable<InputDefinition> inputs,
            IEnumerable<OutputDefinition> outputs,
            IEnumerable<ActivityDesignFacet> designFacets,
            ActivityExecutionType executionType = ActivityExecutionType.Action,
            string? id = null) =>
            new FakeVersion(id ?? $"v-{version}", version, definition, providerKey, providerSchemaVersion, consumerKey, consumerSchemaVersion, descriptorPayload, sourceKind, sourceId, executionType);
    }

    private sealed class FakeVersion(
        string id,
        string version,
        IActivityDefinition definition,
        string providerKey,
        string providerSchemaVersion,
        string consumerKey,
        string consumerSchemaVersion,
        JsonElement descriptorPayload,
        string sourceKind,
        string sourceId,
        ActivityExecutionType executionType) : IActivityDefinitionVersion
    {
        public string Id => id;
        public string Version => version;
        public string DefinitionId => definition.Id;
        public string ProviderKey => providerKey;
        public string ProviderSchemaVersion => providerSchemaVersion;
        public string ConsumerKey => consumerKey;
        public string ConsumerSchemaVersion => consumerSchemaVersion;
        public JsonElement DescriptorPayload => descriptorPayload;
        public string SourceKind => sourceKind;
        public string SourceId => sourceId;
        public IActivityDefinition Definition => definition;
        public IEnumerable<InputDefinition> Inputs => [];
        public IEnumerable<OutputDefinition> Outputs => [];
        public IEnumerable<ActivityDesignFacet> DesignFacets => [];
        public ActivityExecutionType ExecutionType => executionType;
        public string? Hash => "hash";
    }
}
