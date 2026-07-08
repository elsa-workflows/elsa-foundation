using System.Text.Json;
using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Handlers;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Exceptions;
using Elsa.Activities.Design.Persistence.Core.Filters;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Persistence.Core;
using Elsa.Primitives.Versioning;
using Xunit;

namespace Elsa.Activities.Design.Tests.Unit;

/// <summary>
/// The Activities <see cref="AddVersionCommandHandler"/> accepts an author-supplied
/// <c>command.Version</c>. Unlike the reconciler path — which guards an existing
/// <c>(DefinitionId, sortKey)</c> before adding — the API path historically called
/// <c>addCommand.Add</c> with no collision check, so a caller could silently duplicate/collide a
/// version. This covers the added pre-write guard: an add whose <c>(DefinitionId, SemVer sort key)</c>
/// already exists must be rejected with <see cref="ActivityDefinitionVersionConflictException"/> and
/// never reach the store, matching the Workflows sibling's conflict-exception discipline.
/// </summary>
public sealed class AddVersionCommandHandlerTests
{
    private readonly StubDefinitionStore _definitionStore = new();
    private readonly RecordingAddCommand _addCommand = new();

    private static AddVersion Command(string version) =>
        new("def-1", version, "Descriptor", default, null, null, null, null);

    private AddVersionCommandHandler CreateHandler(StubVersionStore versionStore) =>
        new(new StubVersionFactory(), _addCommand, versionStore, _definitionStore);

    [Fact]
    public async Task Adding_a_version_whose_definition_and_sortkey_already_exist_is_rejected()
    {
        var versionStore = new StubVersionStore
        {
            ExistingSortKeys = { SemVer.ToSortKey("1.0.0") },
        };
        var handler = CreateHandler(versionStore);

        await Assert.ThrowsAsync<ActivityDefinitionVersionConflictException>(
            () => handler.Handle(Command("1.0.0"), CancellationToken.None));

        Assert.Empty(_addCommand.Added);
    }

    [Fact]
    public async Task Adding_a_version_whose_sortkey_is_free_proceeds()
    {
        var versionStore = new StubVersionStore();
        var handler = CreateHandler(versionStore);

        var result = await handler.Handle(Command("1.0.0"), CancellationToken.None);

        Assert.Equal("1.0.0", Assert.Single(_addCommand.Added).Version);
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
                DescriptorType = "Descriptor",
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

    private sealed class RecordingAddCommand : IAddCommand<ActivityDefinitionVersion>
    {
        public List<ActivityDefinitionVersion> Added { get; } = [];

        public Task Add(ActivityDefinitionVersion entity, CancellationToken cancellationToken = default)
        {
            Added.Add(entity);
            return Task.CompletedTask;
        }
    }

    private sealed class StubVersionFactory : IActivityDefinitionVersionFactory
    {
        public IActivityDefinitionVersion Create(
            IActivityDefinition definition,
            string version,
            string descriptorType,
            JsonElement descriptorPayload,
            string sourceKind,
            string sourceId,
            IEnumerable<InputDefinition> inputs,
            IEnumerable<OutputDefinition> outputs,
            IEnumerable<ActivityDesignFacet> designFacets,
            ActivityExecutionType executionType = ActivityExecutionType.Action,
            string? id = null) =>
            new FakeVersion(id ?? $"v-{version}", version, definition, descriptorType, descriptorPayload, sourceKind, sourceId, executionType);
    }

    private sealed class FakeVersion(
        string id,
        string version,
        IActivityDefinition definition,
        string descriptorType,
        JsonElement descriptorPayload,
        string sourceKind,
        string sourceId,
        ActivityExecutionType executionType) : IActivityDefinitionVersion
    {
        public string Id => id;
        public string Version => version;
        public string DefinitionId => definition.Id;
        public string DescriptorType => descriptorType;
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
