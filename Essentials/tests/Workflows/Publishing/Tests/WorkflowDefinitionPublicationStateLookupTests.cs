using Elsa.Workflows.Design.Persistence.Core.Contracts;
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

        Assert.Equal(WorkflowDefinitionPublicationState.Published, states["orders"]);
    }

    [Fact]
    public async Task Empty_slot_after_unpublish_is_retracted_not_published()
    {
        var fixture = await Fixture.CreateAsync();
        await fixture.ActivateAsync("orders");
        await fixture.DeactivateAsync("orders");

        var states = await fixture.Lookup.ListByDefinitionIdsAsync(["orders"]);

        Assert.Equal(WorkflowDefinitionPublicationState.Retracted, states["orders"]);
    }

    [Fact]
    public async Task Live_slot_plus_an_empty_slot_on_the_same_definition_is_published()
    {
        var fixture = await Fixture.CreateAsync();
        await fixture.ActivateAsync("orders");
        await fixture.ActivateAsync("orders", "canary");
        await fixture.DeactivateAsync("orders", "canary");

        var states = await fixture.Lookup.ListByDefinitionIdsAsync(["orders"]);

        Assert.Equal(WorkflowDefinitionPublicationState.Published, states["orders"]);
    }

    [Fact]
    public async Task Retired_records_without_a_slot_are_retired()
    {
        var fixture = await Fixture.CreateAsync();
        await fixture.SaveRetiredAsync("legacy");

        var states = await fixture.Lookup.ListByDefinitionIdsAsync(["legacy"]);

        Assert.Equal(WorkflowDefinitionPublicationState.Retired, states["legacy"]);
    }

    [Fact]
    public async Task Unknown_definition_is_draft()
    {
        var fixture = await Fixture.CreateAsync();

        var states = await fixture.Lookup.ListByDefinitionIdsAsync(["never-published"]);

        Assert.Equal(WorkflowDefinitionPublicationState.Draft, states["never-published"]);
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
        Assert.Equal(WorkflowDefinitionPublicationState.Published, states["live"]);
        Assert.Equal(WorkflowDefinitionPublicationState.Retracted, states["withdrawn"]);
        Assert.Equal(WorkflowDefinitionPublicationState.Retired, states["gone"]);
        Assert.Equal(WorkflowDefinitionPublicationState.Draft, states["draft"]);
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
        Assert.Equal(WorkflowDefinitionPublicationState.Published, states["live"]);
        Assert.Equal(WorkflowDefinitionPublicationState.Retracted, states["withdrawn"]);
        Assert.Equal(WorkflowDefinitionPublicationState.Retired, states["gone"]);
        Assert.Equal(WorkflowDefinitionPublicationState.Draft, states["draft"]);
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
        services.TryAddScoped<IWorkflowDefinitionPublicationStateLookup, NullWorkflowDefinitionPublicationStateLookup>();
        new WorkflowsPublishingFeature().ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.IsType<WorkflowDefinitionPublicationStateLookup>(
            scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionPublicationStateLookup>());
    }

    private sealed class Fixture
    {
        private readonly InMemoryWorkflowActivationAuthority _inner = new();
        private readonly InMemoryPublicationRecordStore _records = new();

        public CountingAuthority Authority { get; }
        public IWorkflowDefinitionPublicationStateLookup Lookup { get; }

        private Fixture()
        {
            Authority = new CountingAuthority(_inner);
            Lookup = new WorkflowDefinitionPublicationStateLookup(Authority, _records);
        }

        public static Task<Fixture> CreateAsync() => Task.FromResult(new Fixture());

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
            await _records.SaveAsync(Record(definitionId, slotId, publicationId, PublicationStatus.Active, Now));
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
            var current = await _records.FindAsync(publicationId);
            if (current is not null)
                Assert.True(await _records.TryTransitionAsync(
                    current with { Status = PublicationStatus.Retired, RetiredAt = Now.AddMinutes(1) },
                    current.Status));
        }

        public Task SaveRetiredAsync(string definitionId)
        {
            var slotId = WorkflowActivationSlotIdentity.Create(definitionId, "default");
            return _records.SaveAsync(Record(
                definitionId,
                slotId,
                $"publication-{definitionId}",
                PublicationStatus.Retired,
                Now,
                Now)).AsTask();
        }

        private static PublicationRecord Record(
            string definitionId,
            string slotId,
            string publicationId,
            PublicationStatus status,
            DateTimeOffset createdAt,
            DateTimeOffset? retiredAt = null) =>
            new(
                publicationId,
                slotId,
                definitionId,
                $"version-{definitionId}",
                $"artifact-{definitionId}",
                $"reference-{definitionId}",
                0,
                status,
                createdAt,
                status == PublicationStatus.Active ? createdAt : null,
                retiredAt,
                null);
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
