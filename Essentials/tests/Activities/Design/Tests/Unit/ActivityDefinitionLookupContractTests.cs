using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Filters;
using Elsa.Activities.Design.Persistence.Core.Services;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Xunit;

namespace Elsa.Activities.Design.Tests.Unit;

/// <summary>
/// Provider-neutral conversion of the privileged-access forwarding objective from the
/// EF-referencing <c>ActivityDefinitionLookupTests</c>. <see cref="ActivityDefinitionLookup"/> is a Core
/// service; this pins that <c>tenantAgnostic</c> reaches <see cref="ActivityDefinitionFilter.TenantAgnostic"/>
/// (issue #392) using a recording store — no persistence provider is involved. The search-term lookup
/// objective from the same EF file is covered by
/// <c>ActivityDesignQueryContractSuite.Search_term_spans_display_name_type_key_category_description_and_id</c>.
/// </summary>
public sealed class ActivityDefinitionLookupContractTests
{
    [Fact]
    public void Existing_compiled_consumers_keep_the_two_argument_constructor() =>
        Assert.NotNull(typeof(ActivityDefinitionLookup).GetConstructor([typeof(IActivityDefinitionVersionStore), typeof(IActivityDefinitionStore)]));

    [Theory]
    [InlineData(true)]
    [InlineData(null)]
    public async Task ListDefinitions_ForwardsTenantAgnosticToTheFilter(bool? tenantAgnostic)
    {
        var definitionStore = new RecordingActivityDefinitionStore();
        var lookup = new ActivityDefinitionLookup(new ThrowingActivityDefinitionVersionStore(), definitionStore);

        await lookup.ListDefinitions(tenantAgnostic: tenantAgnostic);

        Assert.NotNull(definitionStore.LastFilter);
        Assert.Equal(tenantAgnostic, definitionStore.LastFilter!.TenantAgnostic);
    }

    [Theory]
    [InlineData("active", true)]
    [InlineData("retired", false)]
    [InlineData("revoked", false)]
    [InlineData("hidden", false)]
    [InlineData("unrecommended", false)]
    [InlineData("wrong-definition", false)]
    [InlineData("missing-publication", false)]
    [InlineData("missing-version", false)]
    [InlineData("version-mismatch", false)]
    public async Task Recommended_version_uses_explicit_active_visible_pointer(string state, bool available)
    {
        var definitionStore = new RecordingActivityDefinitionStore { Visible = state != "hidden" };
        var stores = new RecommendationStores(state);
        var lookup = new ActivityDefinitionLookup(new ThrowingActivityDefinitionVersionStore
        {
            Version = state == "missing-version" ? null : new ActivityDefinitionVersion("1.0.0+current", "definition")
                { Id = "recommended", CreatedAt = DateTimeOffset.UnixEpoch }
        }, definitionStore, stores, stores);
        var result = await lookup.FindRecommendedVersion("definition");
        Assert.Equal(available, result is not null);
        if (result is not null)
        {
            Assert.Equal("recommended", result.Id);
            Assert.Equal("1.0.0+current", result.Version);
        }
    }

    private sealed class RecommendationStores(string state) : IActivityDefinitionAuthoringStore, IActivityDefinitionVersionPublicationStore
    {
        Task<ActivityDefinitionAuthoringState?> IActivityDefinitionAuthoringStore.FindAsync(string definitionId, CancellationToken cancellationToken) =>
            Task.FromResult<ActivityDefinitionAuthoringState?>(new()
            {
                DefinitionId = definitionId, RecommendedVersionId = state == "unrecommended" ? null : "recommended"
            });
        Task<ActivityDefinitionVersionPublication?> IActivityDefinitionVersionPublicationStore.FindAsync(string definitionVersionId, CancellationToken cancellationToken) =>
            Task.FromResult<ActivityDefinitionVersionPublication?>(state == "missing-publication" ? null : new()
            {
                DefinitionVersionId = definitionVersionId,
                DefinitionId = state == "wrong-definition" ? "another" : "definition",
                Version = state == "version-mismatch" ? "1.0.0+previous" : "1.0.0+current",
                Lifecycle = state switch
                {
                    "retired" => ActivityDefinitionVersionLifecycle.Retired,
                    "revoked" => ActivityDefinitionVersionLifecycle.Revoked,
                    _ => ActivityDefinitionVersionLifecycle.Active
                },
                DirectDependencyCount = 0, ClosedTemplateCount = 0, RuntimeRequirements = []
            });
        public Task<IReadOnlyList<ActivityDefinitionAuthoringState>> ListAsync(IEnumerable<string> definitionIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ActivityDefinitionVersionPublication>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class RecordingActivityDefinitionStore : IActivityDefinitionStore
    {
        public bool Visible { get; init; }
        public ActivityDefinitionFilter? LastFilter { get; private set; }

        public Task<ActivityDefinition> GetAsync(string id, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("This test only supports ListAsync.");

        public Task<ActivityDefinition?> FindAsync(ActivityDefinitionFilter filter, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("This test only supports ListAsync.");

        public Task<IReadOnlyList<ActivityDefinition>> ListAsync(ActivityDefinitionFilter filter, CancellationToken cancellationToken = default)
        {
            LastFilter = filter;
            return Task.FromResult<IReadOnlyList<ActivityDefinition>>(Visible ? [new() { Id = "definition" }] : []);
        }

        public Task<ActivityDefinition?> FindByIdOrActivityTypeKeyAsync(string id, string activityTypeKey, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("This test only supports ListAsync.");

        public Task<bool> ExistsByActivityTypeKeyAsync(string activityTypeKey, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("This test only supports ListAsync.");
    }

    private sealed class ThrowingActivityDefinitionVersionStore : IActivityDefinitionVersionStore
    {
        private const string Message = "This test never resolves a version.";

        public ActivityDefinitionVersion? Version { get; init; }
        public Task<ActivityDefinitionVersion> GetAsync(string versionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Version ?? throw new Elsa.Primitives.Exceptions.EntityNotFoundException(versionId));
        public Task<ActivityDefinitionVersion> GetWithDefinitionAsync(string versionId, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Message);
        public Task<ActivityDefinitionVersion?> FindByDefinitionAndSortKeyAsync(string definitionId, string semVerSortKey, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Message);
        public Task<IReadOnlyList<ActivityDefinitionVersion>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Message);
        public Task<IReadOnlyList<ActivityDefinitionVersion>> ListByDefinitionIdsAsync(IEnumerable<string> definitionIds, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Message);
        public Task<IReadOnlyList<ActivityDefinitionVersion>> ListAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException(Message);
    }
}
