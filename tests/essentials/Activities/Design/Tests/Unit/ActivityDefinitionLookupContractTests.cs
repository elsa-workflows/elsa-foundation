using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Filters;
using Elsa.Activities.Design.Persistence.Core.Services;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Xunit;

namespace Elsa.Activities.Design.Tests.Unit;

/// <summary>
/// Provider-neutral conversion (T070) of the privileged-access forwarding objective from the
/// EF-referencing <c>ActivityDefinitionLookupTests</c>. <see cref="ActivityDefinitionLookup"/> is a Core
/// service; this pins that <c>tenantAgnostic</c> reaches <see cref="ActivityDefinitionFilter.TenantAgnostic"/>
/// (issue #392) using a recording store — no persistence provider is involved. The search-term lookup
/// objective from the same EF file is covered by
/// <c>ActivityDesignQueryContractSuite.Search_term_spans_display_name_type_key_category_description_and_id</c>.
/// </summary>
public sealed class ActivityDefinitionLookupContractTests
{
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

    private sealed class RecordingActivityDefinitionStore : IActivityDefinitionStore
    {
        public ActivityDefinitionFilter? LastFilter { get; private set; }

        public Task<ActivityDefinition> GetAsync(string id, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("This test only supports ListAsync.");

        public Task<ActivityDefinition?> FindAsync(ActivityDefinitionFilter filter, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("This test only supports ListAsync.");

        public Task<IReadOnlyList<ActivityDefinition>> ListAsync(ActivityDefinitionFilter filter, CancellationToken cancellationToken = default)
        {
            LastFilter = filter;
            return Task.FromResult<IReadOnlyList<ActivityDefinition>>([]);
        }

        public Task<ActivityDefinition?> FindByIdOrActivityTypeKeyAsync(string id, string activityTypeKey, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("This test only supports ListAsync.");

        public Task<bool> ExistsByActivityTypeKeyAsync(string activityTypeKey, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("This test only supports ListAsync.");
    }

    private sealed class ThrowingActivityDefinitionVersionStore : IActivityDefinitionVersionStore
    {
        private const string Message = "This test never resolves a version.";

        public Task<ActivityDefinitionVersion> GetAsync(string versionId, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Message);
        public Task<ActivityDefinitionVersion> GetWithDefinitionAsync(string versionId, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Message);
        public Task<ActivityDefinitionVersion?> FindByDefinitionAndSortKeyAsync(string definitionId, string semVerSortKey, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Message);
        public Task<IReadOnlyList<ActivityDefinitionVersion>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Message);
        public Task<IReadOnlyList<ActivityDefinitionVersion>> ListByDefinitionIdsAsync(IEnumerable<string> definitionIds, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Message);
        public Task<IReadOnlyList<ActivityDefinitionVersion>> ListAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException(Message);
    }
}
