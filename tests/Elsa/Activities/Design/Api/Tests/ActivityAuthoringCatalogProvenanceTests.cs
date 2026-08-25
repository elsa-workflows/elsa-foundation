using Elsa.Activities.Design.Api.Contracts;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Api.Requests;
using Elsa.Activities.Design.Api.Services;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Stores;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Filters;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Xunit;

namespace Elsa.Activities.Design.Api.Tests;

/// <summary>
/// #1164: the authoring catalog exposes provenance per persisted entry — the version's persisted
/// SourceKind/SourceId plus, for CLR-provided activities only, the shell feature id that provides the
/// activity type. Non-CLR consumers keep source provenance with a null feature id; built-in
/// engine-intrinsic entries carry no provenance at all.
/// </summary>
public sealed class ActivityAuthoringCatalogProvenanceTests
{
    private const string ClrConsumerKey = "elsa.clr-activity";
    private const string ClrActivityTypeKey = "Acme.Activities.SendRequest";
    private const string GraphActivityTypeKey = "acme.approvals.request";

    [Fact]
    public async Task Persisted_clr_row_carries_source_provenance_and_the_providing_feature_id()
    {
        var view = await ListAsync(new RecordingResolver("ActivitiesAcme"));

        var descriptor = Assert.Single(view.Activities, activity => activity.ActivityTypeKey == ClrActivityTypeKey);
        Assert.NotNull(descriptor.Provenance);
        Assert.Equal("CLR", descriptor.Provenance!.SourceKind);
        Assert.Equal("Acme.Activities.dll", descriptor.Provenance.SourceId);
        Assert.Equal("ActivitiesAcme", descriptor.Provenance.FeatureId);
    }

    [Fact]
    public async Task Persisted_non_clr_row_keeps_source_provenance_with_a_null_feature_id()
    {
        var resolver = new RecordingResolver("ActivitiesAcme");

        var view = await ListAsync(resolver);

        var descriptor = Assert.Single(view.Activities, activity => activity.ActivityTypeKey == GraphActivityTypeKey);
        Assert.NotNull(descriptor.Provenance);
        Assert.Equal("Json", descriptor.Provenance!.SourceKind);
        Assert.Equal("catalog/approvals.json", descriptor.Provenance.SourceId);
        Assert.Null(descriptor.Provenance.FeatureId);
        // The handler must gate resolution on the CLR consumer key rather than asking for every row.
        Assert.Equal([ClrActivityTypeKey], resolver.ResolvedKeys);
    }

    [Fact]
    public async Task Built_in_intrinsic_rows_carry_no_provenance()
    {
        var view = await ListAsync(new RecordingResolver("ActivitiesAcme"));

        var intrinsics = view.Activities.Where(activity => activity.Intrinsic is not null).ToArray();
        Assert.NotEmpty(intrinsics);
        Assert.All(intrinsics, activity => Assert.Null(activity.Provenance));
    }

    private static async Task<ActivityAuthoringCatalogView> ListAsync(IActivityFeatureAttributionResolver resolver)
    {
        var clrDefinition = new ActivityDefinition
        {
            Id = "def-clr",
            ActivityTypeKey = ClrActivityTypeKey,
            Category = "Acme",
            DisplayName = "Send Request",
        };
        var graphDefinition = new ActivityDefinition
        {
            Id = "def-graph",
            ActivityTypeKey = GraphActivityTypeKey,
            Category = "Acme",
            DisplayName = "Approval Request",
        };
        var clrVersion = new ActivityDefinitionVersion("1.0.0", clrDefinition.Id)
        {
            Id = "ver-clr",
            ConsumerKey = ClrConsumerKey,
            SourceKind = "CLR",
            SourceId = "Acme.Activities.dll",
        };
        var graphVersion = new ActivityDefinitionVersion("1.0.0", graphDefinition.Id)
        {
            Id = "ver-graph",
            ConsumerKey = "elsa.graph-activity",
            SourceKind = "Json",
            SourceId = "catalog/approvals.json",
        };

        var handler = new ActivityAuthoringCatalogReader(
            new InMemoryDefinitionStore([clrDefinition, graphDefinition]),
            new InMemoryVersionStore([clrVersion, graphVersion]),
            new AllAddableEvaluator(),
            new NullSettingsStore(),
            [new IntrinsicAuthoringDescriptorProvider()],
            resolver);

        return await handler.ListAsync(new ListActivityAuthoringCatalog(), CancellationToken.None);
    }

    private sealed class RecordingResolver(string featureId) : IActivityFeatureAttributionResolver
    {
        public List<string> ResolvedKeys { get; } = [];

        public ValueTask<string?> ResolveProvidingFeatureAsync(string activityTypeKey, CancellationToken cancellationToken)
        {
            ResolvedKeys.Add(activityTypeKey);
            return ValueTask.FromResult<string?>(featureId);
        }
    }

    private sealed class InMemoryDefinitionStore(IReadOnlyList<ActivityDefinition> definitions) : IActivityDefinitionStore
    {
        public Task<ActivityDefinition> GetAsync(string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ActivityDefinition?> FindAsync(ActivityDefinitionFilter filter, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ActivityDefinition>> ListAsync(ActivityDefinitionFilter filter, CancellationToken cancellationToken = default) => Task.FromResult(definitions);
        public Task<ActivityDefinition?> FindByIdOrActivityTypeKeyAsync(string id, string activityTypeKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsByActivityTypeKeyAsync(string activityTypeKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class InMemoryVersionStore(IReadOnlyList<ActivityDefinitionVersion> versions) : IActivityDefinitionVersionStore
    {
        public Task<ActivityDefinitionVersion> GetAsync(string versionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ActivityDefinitionVersion> GetWithDefinitionAsync(string versionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ActivityDefinitionVersion?> FindByDefinitionAndSortKeyAsync(string definitionId, string semVerSortKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ActivityDefinitionVersion>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ActivityDefinitionVersion>> ListByDefinitionIdsAsync(IEnumerable<string> definitionIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ActivityDefinitionVersion>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult(versions);
    }

    private sealed class AllAddableEvaluator : IActivityAvailabilityEvaluator
    {
        public IReadOnlyCollection<IActivityDefinition> FilterAddable(IEnumerable<IActivityDefinition> activities, ActivityAvailabilitySettings? managementSettings = null) =>
            activities.ToArray();
    }

    private sealed class NullSettingsStore : IActivityAvailabilitySettingsStore
    {
        public Task<ActivityAvailabilitySettings?> LoadAsync(string scope, CancellationToken cancellationToken = default) => Task.FromResult<ActivityAvailabilitySettings?>(null);
        public Task SaveAsync(ActivityAvailabilitySettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
