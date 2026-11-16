using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executables;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Workflows.Publishing.Tests;

public sealed class WorkflowDefinitionPublicationStateLookupTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Live_slot_is_published_even_when_a_design_version_exists()
    {
        var fixture = await Fixture.CreateAsync();
        await fixture.ActivateAsync("orders");

        var states = await fixture.Lookup.ListByDefinitionIdsAsync(["orders"]);

        Assert.Equal(WorkflowDefinitionPublicationState.Published, states["orders"].State);
        Assert.Equal("version-orders-default", states["orders"].PublishedVersionId);
    }

    [Theory]
    [InlineData(PublicationStatus.Candidate)]
    [InlineData(PublicationStatus.Retired)]
    public async Task Lagging_record_named_by_the_active_slot_still_reports_its_published_version(PublicationStatus laggingStatus)
    {
        var fixture = await Fixture.CreateAsync();
        await fixture.ActivateAsync("orders");
        await fixture.SetPublicationStatusAsync("orders", "default", laggingStatus);

        var states = await fixture.Lookup.ListByDefinitionIdsAsync(["orders"]);

        Assert.Equal(WorkflowDefinitionPublicationState.Published, states["orders"].State);
        Assert.Equal("version-orders-default", states["orders"].PublishedVersionId);
    }

    [Fact]
    public async Task Failed_record_named_by_the_active_slot_does_not_supply_a_published_version()
    {
        var fixture = await Fixture.CreateAsync();
        await fixture.ActivateAsync("orders");
        await fixture.SetPublicationStatusAsync("orders", "default", PublicationStatus.Failed);

        var states = await fixture.Lookup.ListByDefinitionIdsAsync(["orders"]);

        Assert.Equal(WorkflowDefinitionPublicationState.Published, states["orders"].State);
        Assert.Null(states["orders"].PublishedVersionId);
    }

    [Theory]
    [InlineData("publication")]
    [InlineData("definition")]
    [InlineData("slot id")]
    [InlineData("slot name")]
    public async Task Record_with_mismatched_identity_does_not_supply_the_active_slots_version(string identityPart)
    {
        var fixture = await Fixture.CreateAsync();
        await fixture.ActivateAsync("orders");
        var publications = new CountingPublicationRecordStore(
            fixture.Publications,
            transformFoundPublication: publication => identityPart switch
            {
                "publication" => publication with { PublicationId = "other-publication" },
                "definition" => publication with { WorkflowDefinitionId = "other" },
                "slot id" => publication with { SlotId = "other-slot-id" },
                _ => publication with { SlotName = "other-slot-name" }
            });
        var lookup = new WorkflowDefinitionPublicationStateLookup(fixture.Authority, publications, fixture.Policies);

        var states = await lookup.ListByDefinitionIdsAsync(["orders"]);

        Assert.Equal(WorkflowDefinitionPublicationState.Published, states["orders"].State);
        Assert.Null(states["orders"].PublishedVersionId);
    }

    [Fact]
    public async Task Empty_slot_after_unpublish_is_retracted_not_published()
    {
        var fixture = await Fixture.CreateAsync();
        await fixture.ActivateAsync("orders");
        await fixture.DeactivateAsync("orders");

        var states = await fixture.Lookup.ListByDefinitionIdsAsync(["orders"]);

        Assert.Equal(WorkflowDefinitionPublicationState.Retracted, states["orders"].State);
        Assert.Null(states["orders"].PublishedVersionId);
    }

    [Fact]
    public async Task Live_slot_plus_an_empty_slot_on_the_same_definition_is_published()
    {
        var fixture = await Fixture.CreateAsync();
        await fixture.ActivateAsync("orders");
        await fixture.ActivateAsync("orders", "canary");
        await fixture.DeactivateAsync("orders", "canary");

        var states = await fixture.Lookup.ListByDefinitionIdsAsync(["orders"]);

        Assert.Equal(WorkflowDefinitionPublicationState.Published, states["orders"].State);
        Assert.Equal("version-orders-default", states["orders"].PublishedVersionId);
    }

    [Fact]
    public async Task Default_slot_is_the_primary_published_version_when_named_slots_are_also_active()
    {
        var fixture = await Fixture.CreateAsync();
        await fixture.ActivateAsync("orders");
        await fixture.ActivateAsync("orders", "canary");

        var summaries = await fixture.Lookup.ListByDefinitionIdsAsync(["orders"]);

        Assert.Equal(WorkflowDefinitionPublicationState.Published, summaries["orders"].State);
        Assert.Equal("version-orders-default", summaries["orders"].PublishedVersionId);
    }

    [Fact]
    public async Task Workflow_default_policy_selects_the_configured_primary_slot()
    {
        var fixture = await Fixture.CreateAsync();
        await fixture.SetWorkflowDefaultSlotAsync("orders", "production");
        await fixture.ActivateAsync("orders", "default");
        await fixture.ActivateAsync("orders", "production");
        await fixture.ActivateAsync("orders", "canary");

        var summaries = await fixture.Lookup.ListByDefinitionIdsAsync(["orders"]);

        Assert.Equal(WorkflowDefinitionPublicationState.Published, summaries["orders"].State);
        Assert.Equal("version-orders-production", summaries["orders"].PublishedVersionId);
        Assert.Equal(1, fixture.Policies.BatchCalls);
        Assert.Equal(1, fixture.Policies.FindCalls);
    }

    [Fact]
    public async Task A_unique_named_publication_is_reported_when_the_configured_primary_is_inactive()
    {
        var fixture = await Fixture.CreateAsync();
        await fixture.SetWorkflowDefaultSlotAsync("orders", "production");
        await fixture.ActivateAsync("orders", "canary");

        var summaries = await fixture.Lookup.ListByDefinitionIdsAsync(["orders"]);

        Assert.Equal(WorkflowDefinitionPublicationState.Published, summaries["orders"].State);
        Assert.Equal("version-orders-canary", summaries["orders"].PublishedVersionId);
    }

    [Fact]
    public async Task Multiple_named_active_versions_do_not_choose_an_arbitrary_summary_version()
    {
        var fixture = await Fixture.CreateAsync();
        await fixture.ActivateAsync("orders", "canary");
        await fixture.ActivateAsync("orders", "blue");

        var summaries = await fixture.Lookup.ListByDefinitionIdsAsync(["orders"]);

        Assert.Equal(WorkflowDefinitionPublicationState.Published, summaries["orders"].State);
        Assert.Null(summaries["orders"].PublishedVersionId);
    }

    [Fact]
    public async Task Retired_records_without_a_slot_are_retired()
    {
        var fixture = await Fixture.CreateAsync();
        await fixture.SaveRetiredAsync("legacy");

        var states = await fixture.Lookup.ListByDefinitionIdsAsync(["legacy"]);

        Assert.True(fixture.Publications.SupportsDefinitionIdLookup);
        Assert.Equal(WorkflowDefinitionPublicationState.Retired, states["legacy"].State);
        Assert.Null(states["legacy"].PublishedVersionId);
    }

    [Fact]
    public async Task No_definition_id_journal_lookup_leaves_no_slot_states_unavailable()
    {
        var fixture = await Fixture.CreateAsync();
        await fixture.SaveRetiredAsync("legacy");
        await fixture.ActivateAsync("live");
        var publicationStore = new CountingPublicationRecordStore(fixture.Publications, supportsDefinitionIdLookup: false);
        var lookup = new WorkflowDefinitionPublicationStateLookup(fixture.Authority, publicationStore, fixture.Policies);

        var states = await lookup.ListByDefinitionIdsAsync(["legacy", "never-published", "live"]);

        Assert.False(publicationStore.SupportsDefinitionIdLookup);
        Assert.Equal(0, publicationStore.DefinitionLookupCalls);
        Assert.DoesNotContain("legacy", states.Keys);
        Assert.DoesNotContain("never-published", states.Keys);
        Assert.Equal(WorkflowDefinitionPublicationState.Published, states["live"].State);
    }

    [Fact]
    public void Legacy_record_stores_default_to_no_definition_id_lookup_support()
    {
        IPublicationRecordStore store = new LegacyPublicationRecordStore();

        Assert.False(store.SupportsDefinitionIdLookup);
    }

    [Fact]
    public async Task Unknown_definition_is_draft()
    {
        var fixture = await Fixture.CreateAsync();

        var states = await fixture.Lookup.ListByDefinitionIdsAsync(["never-published"]);

        Assert.Equal(WorkflowDefinitionPublicationState.Draft, states["never-published"].State);
    }

    [Fact]
    public async Task Batch_resolves_each_id_and_skips_blank_values()
    {
        var fixture = await Fixture.CreateAsync();
        await fixture.ActivateAsync("live");
        await fixture.ActivateAsync("withdrawn");
        await fixture.DeactivateAsync("withdrawn");
        await fixture.SaveRetiredAsync("gone");

        var states = await fixture.Lookup.ListByDefinitionIdsAsync(["live", "withdrawn", "gone", "draft", " ", "live"]);

        Assert.Equal(4, states.Count);
        Assert.Equal(WorkflowDefinitionPublicationState.Published, states["live"].State);
        Assert.Equal(WorkflowDefinitionPublicationState.Retracted, states["withdrawn"].State);
        Assert.Null(states["withdrawn"].PublishedVersionId);
        Assert.Equal(WorkflowDefinitionPublicationState.Retired, states["gone"].State);
        Assert.Null(states["gone"].PublishedVersionId);
        Assert.Equal(WorkflowDefinitionPublicationState.Draft, states["draft"].State);
    }

    [Fact]
    public async Task Batch_lookup_makes_one_authority_call()
    {
        var fixture = await Fixture.CreateAsync();
        await fixture.ActivateAsync("live");
        await fixture.ActivateAsync("withdrawn");
        await fixture.DeactivateAsync("withdrawn");
        await fixture.SaveRetiredAsync("gone");

        var states = await fixture.Lookup.ListByDefinitionIdsAsync(["live", "withdrawn", "gone", "draft"]);

        Assert.Equal(1, fixture.Authority.BatchCalls);
        Assert.Equal([4], fixture.Authority.BatchSizes);
        Assert.Equal(0, fixture.Authority.SingleCalls);
        Assert.Equal(1, fixture.Publications.IdentityBatchCalls);
        Assert.Equal([1], fixture.Publications.IdentityBatchSizes);
        Assert.Equal(1, fixture.Policies.BatchCalls);
        Assert.Equal(1, fixture.Policies.FindCalls);
        Assert.Equal(WorkflowDefinitionPublicationState.Published, states["live"].State);
        Assert.Equal(WorkflowDefinitionPublicationState.Retracted, states["withdrawn"].State);
        Assert.Equal(WorkflowDefinitionPublicationState.Retired, states["gone"].State);
        Assert.Equal(WorkflowDefinitionPublicationState.Draft, states["draft"].State);
    }

    [Fact]
    public async Task Large_id_set_is_still_one_authority_call()
    {
        var fixture = await Fixture.CreateAsync();
        var ids = Enumerable.Range(0, 501).Select(index => $"definition-{index:D3}").ToArray();

        await fixture.Lookup.ListByDefinitionIdsAsync(ids);

        Assert.Equal(1, fixture.Authority.BatchCalls);
        Assert.Equal([501], fixture.Authority.BatchSizes);
        Assert.Equal(0, fixture.Authority.SingleCalls);
        Assert.Equal(0, fixture.Publications.IdentityBatchCalls);
        Assert.Equal(1, fixture.Policies.BatchCalls);
        Assert.Equal(1, fixture.Policies.FindCalls);
    }

    [Fact]
    public void Publishing_engine_registers_the_lookup()
    {
        var services = new ServiceCollection();
        new WorkflowsPublishingFeature().ConfigureServices(services);

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IWorkflowDefinitionPublicationStateLookup) &&
            descriptor.ImplementationType == typeof(WorkflowDefinitionPublicationStateLookup));
    }

    [Fact]
    public void Publishing_replaces_the_design_null_lookup()
    {
        var services = new ServiceCollection();
        services.TryAddScoped<IWorkflowDefinitionPublicationStateLookup, PriorLookup>();
        new WorkflowsPublishingFeature().ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.IsType<WorkflowDefinitionPublicationStateLookup>(
            scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionPublicationStateLookup>());
    }

    private sealed class PriorLookup : IWorkflowDefinitionPublicationStateLookup
    {
        public Task<IReadOnlyDictionary<string, WorkflowDefinitionPublicationSummary>> ListByDefinitionIdsAsync(
            IReadOnlyCollection<string> workflowDefinitionIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, WorkflowDefinitionPublicationSummary>>(
                new Dictionary<string, WorkflowDefinitionPublicationSummary>(StringComparer.Ordinal));
    }

    private sealed class Fixture
    {
        private readonly InMemoryWorkflowActivationAuthority _inner = new();
        private readonly InMemoryPublicationRecordStore _innerRecords = new();
        private readonly InMemoryPublicationPolicyStore _innerPolicies = new();

        public CountingAuthority Authority { get; }
        public CountingPublicationRecordStore Publications { get; }
        public CountingPublicationPolicyStore Policies { get; }
        public IWorkflowDefinitionPublicationStateLookup Lookup { get; }

        private Fixture()
        {
            Authority = new CountingAuthority(_inner);
            Publications = new CountingPublicationRecordStore(_innerRecords);
            Policies = new CountingPublicationPolicyStore(_innerPolicies);
            Lookup = new WorkflowDefinitionPublicationStateLookup(Authority, Publications, Policies);
        }

        public static Task<Fixture> CreateAsync() => Task.FromResult(new Fixture());

        public async Task SetWorkflowDefaultSlotAsync(string definitionId, string slotName)
        {
            var result = await Policies.TrySaveAsync(new PublicationPolicy(
                definitionId,
                PublicationPolicyDefaultAction.ReplaceDefaultSlot,
                slotName,
                0,
                Now), 0);
            Assert.True(result.Succeeded);
        }

        public async Task SetPublicationStatusAsync(string definitionId, string slotName, PublicationStatus status)
        {
            var publicationId = $"publication-{definitionId}-{slotName}";
            var current = await Publications.FindAsync(publicationId);
            Assert.NotNull(current);
            Assert.True(await Publications.TryTransitionAsync(
                current with
                {
                    Status = status,
                    ActivatedAt = status is PublicationStatus.Candidate or PublicationStatus.Failed ? null : current.ActivatedAt,
                    RetiredAt = status == PublicationStatus.Retired ? Now.AddMinutes(1) : null
                },
                current.Status));
        }

        public async Task ActivateAsync(string definitionId, string slotName = "default")
        {
            var slotId = WorkflowActivationSlotIdentity.Create(definitionId, slotName);
            var publicationId = $"publication-{definitionId}-{slotName}";
            var transition = await _inner.TryActivateAsync(new WorkflowActivationSlotRequest(
                definitionId,
                slotName,
                publicationId,
                WorkflowActivationSource.Publishing,
                0,
                Now));
            Assert.True(transition.Succeeded);
            await Publications.SaveAsync(Record(definitionId, slotId, publicationId, slotName, PublicationStatus.Active, Now));
        }

        public async Task DeactivateAsync(string definitionId, string slotName = "default")
        {
            var slot = await _inner.FindAsync(definitionId, slotName);
            Assert.NotNull(slot);
            var transition = await _inner.TryDeactivateAsync(
                definitionId,
                slotName,
                WorkflowActivationSource.Publishing,
                slot.Revision,
                Now.AddMinutes(1));
            Assert.True(transition.Succeeded);
            var publicationId = slot.ActiveActivationId ?? $"publication-{definitionId}-{slotName}";
            var current = await Publications.FindAsync(publicationId);
            if (current is not null)
                Assert.True(await Publications.TryTransitionAsync(
                    current with { Status = PublicationStatus.Retired, RetiredAt = Now.AddMinutes(1) },
                    current.Status));
        }

        public Task SaveRetiredAsync(string definitionId)
        {
            var slotId = WorkflowActivationSlotIdentity.Create(definitionId, "default");
            return Publications.SaveAsync(Record(
                definitionId,
                slotId,
                $"publication-{definitionId}",
                "default",
                PublicationStatus.Retired,
                Now,
                Now)).AsTask();
        }

        private static PublicationRecord Record(
            string definitionId,
            string slotId,
            string publicationId,
            string slotName,
            PublicationStatus status,
            DateTimeOffset createdAt,
            DateTimeOffset? retiredAt = null) =>
            new(
                publicationId,
                slotId,
                definitionId,
                $"version-{definitionId}-{slotName}",
                $"artifact-{definitionId}",
                $"reference-{definitionId}",
                0,
                status,
                createdAt,
                status == PublicationStatus.Active ? createdAt : null,
                retiredAt,
                null,
                slotName);
    }

    private sealed class CountingPublicationRecordStore(
        IPublicationRecordStore inner,
        bool? supportsDefinitionIdLookup = null,
        Func<PublicationRecord, PublicationRecord>? transformFoundPublication = null) : IPublicationRecordStore
    {
        public int IdentityBatchCalls { get; private set; }
        public List<int> IdentityBatchSizes { get; } = [];
        public int DefinitionLookupCalls { get; private set; }
        public bool SupportsDefinitionIdLookup => supportsDefinitionIdLookup ?? inner.SupportsDefinitionIdLookup;

        public ValueTask SaveAsync(PublicationRecord publication, CancellationToken cancellationToken = default) =>
            inner.SaveAsync(publication, cancellationToken);

        public ValueTask<PublicationRecord?> FindAsync(string publicationId, CancellationToken cancellationToken = default) =>
            inner.FindAsync(publicationId, cancellationToken);

        public async ValueTask<IReadOnlyCollection<PublicationRecord>> FindByIdsAsync(
            IReadOnlyCollection<string> publicationIds,
            CancellationToken cancellationToken = default)
        {
            IdentityBatchCalls++;
            IdentityBatchSizes.Add(publicationIds.Count);
            var publications = await inner.FindByIdsAsync(publicationIds, cancellationToken);
            return transformFoundPublication is null
                ? publications
                : publications.Select(transformFoundPublication).ToArray();
        }

        public ValueTask<IReadOnlyCollection<PublicationRecord>> ListBySlotAsync(string slotId, CancellationToken cancellationToken = default) =>
            inner.ListBySlotAsync(slotId, cancellationToken);

        public ValueTask<IReadOnlyCollection<PublicationRecord>> ListByDefinitionIdsAsync(
            IReadOnlyCollection<string> workflowDefinitionIds,
            CancellationToken cancellationToken = default)
        {
            DefinitionLookupCalls++;
            return inner.ListByDefinitionIdsAsync(workflowDefinitionIds, cancellationToken);
        }

        public ValueTask<bool> TryTransitionAsync(
            PublicationRecord publication,
            PublicationStatus expectedStatus,
            CancellationToken cancellationToken = default) =>
            inner.TryTransitionAsync(publication, expectedStatus, cancellationToken);
    }

    private sealed class LegacyPublicationRecordStore : IPublicationRecordStore
    {
        public ValueTask SaveAsync(PublicationRecord publication, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<PublicationRecord?> FindAsync(string publicationId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<PublicationRecord?>(null);

        public ValueTask<IReadOnlyCollection<PublicationRecord>> ListBySlotAsync(string slotId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyCollection<PublicationRecord>>([]);

        public ValueTask<bool> TryTransitionAsync(
            PublicationRecord publication,
            PublicationStatus expectedStatus,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(false);
    }

    private sealed class CountingPublicationPolicyStore(IPublicationPolicyStore inner) : IPublicationPolicyStore
    {
        public int BatchCalls { get; private set; }
        public int FindCalls { get; private set; }
        public bool SupportsBatchDefinitionLookup => inner.SupportsBatchDefinitionLookup;

        public ValueTask<PublicationPolicy?> FindAsync(string? workflowDefinitionId, CancellationToken cancellationToken = default)
        {
            FindCalls++;
            return inner.FindAsync(workflowDefinitionId, cancellationToken);
        }

        public ValueTask<IReadOnlyDictionary<string, PublicationPolicy>> FindByDefinitionIdsAsync(
            IReadOnlyCollection<string> workflowDefinitionIds,
            CancellationToken cancellationToken = default)
        {
            BatchCalls++;
            return inner.FindByDefinitionIdsAsync(workflowDefinitionIds, cancellationToken);
        }

        public ValueTask<PublicationPolicyWriteResult> TrySaveAsync(
            PublicationPolicy policy,
            long expectedRevision,
            CancellationToken cancellationToken = default) =>
            inner.TrySaveAsync(policy, expectedRevision, cancellationToken);
    }

    private sealed class CountingAuthority(IWorkflowActivationAuthority inner) : IWorkflowActivationAuthority
    {
        public int BatchCalls { get; private set; }
        public int SingleCalls { get; private set; }
        public List<int> BatchSizes { get; } = [];

        public ValueTask<WorkflowActivationSlot?> FindAsync(string workflowDefinitionId, string slotName, CancellationToken cancellationToken = default) =>
            inner.FindAsync(workflowDefinitionId, slotName, cancellationToken);

        public ValueTask<IReadOnlyCollection<WorkflowActivationSlot>> ListByDefinitionAsync(string workflowDefinitionId, CancellationToken cancellationToken = default)
        {
            SingleCalls++;
            return inner.ListByDefinitionAsync(workflowDefinitionId, cancellationToken);
        }

        public ValueTask<IReadOnlyCollection<WorkflowActivationSlot>> ListByDefinitionIdsAsync(
            IReadOnlyCollection<string> workflowDefinitionIds,
            CancellationToken cancellationToken = default)
        {
            BatchCalls++;
            BatchSizes.Add(workflowDefinitionIds.Count);
            return inner.ListByDefinitionIdsAsync(workflowDefinitionIds, cancellationToken);
        }

        public ValueTask<WorkflowActivationTransition> TryActivateAsync(WorkflowActivationSlotRequest request, CancellationToken cancellationToken = default) =>
            inner.TryActivateAsync(request, cancellationToken);

        public ValueTask<WorkflowActivationTransition> TryDeactivateAsync(
            string workflowDefinitionId,
            string slotName,
            WorkflowActivationSource source,
            long expectedRevision,
            DateTimeOffset updatedAt,
            CancellationToken cancellationToken = default) =>
            inner.TryDeactivateAsync(workflowDefinitionId, slotName, source, expectedRevision, updatedAt, cancellationToken);
    }
}
