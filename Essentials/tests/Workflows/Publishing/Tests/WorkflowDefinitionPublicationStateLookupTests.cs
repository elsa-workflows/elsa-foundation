using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executables;
using Microsoft.Extensions.DependencyInjection;
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
    public void Publishing_engine_registers_the_lookup()
    {
        var services = new ServiceCollection();
        new WorkflowsPublishingFeature().ConfigureServices(services);

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IWorkflowDefinitionPublicationStateLookup) &&
            descriptor.ImplementationType == typeof(WorkflowDefinitionPublicationStateLookup));
    }

    private sealed class Fixture
    {
        private readonly InMemoryWorkflowActivationAuthority _authority = new();
        private readonly InMemoryPublicationRecordStore _records = new();

        public IWorkflowDefinitionPublicationStateLookup Lookup { get; }

        private Fixture()
        {
            Lookup = new WorkflowDefinitionPublicationStateLookup(_authority, _records);
        }

        public static Task<Fixture> CreateAsync() => Task.FromResult(new Fixture());

        public async Task ActivateAsync(string definitionId)
        {
            var slotId = WorkflowActivationSlotIdentity.Create(definitionId, "default");
            var publicationId = $"publication-{definitionId}";
            var transition = await _authority.TryActivateAsync(new WorkflowActivationSlotRequest(
                definitionId,
                "default",
                publicationId,
                WorkflowActivationSource.Publishing,
                0,
                Now));
            Assert.True(transition.Succeeded);
            await _records.SaveAsync(Record(definitionId, slotId, publicationId, PublicationStatus.Active, Now));
        }

        public async Task DeactivateAsync(string definitionId)
        {
            var slot = await _authority.FindAsync(definitionId, "default");
            Assert.NotNull(slot);
            var transition = await _authority.TryDeactivateAsync(
                definitionId,
                "default",
                WorkflowActivationSource.Publishing,
                slot.Revision,
                Now.AddMinutes(1));
            Assert.True(transition.Succeeded);
            var publicationId = slot.ActiveActivationId ?? $"publication-{definitionId}";
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
                status == PublicationStatus.Active ? createdAt : createdAt,
                retiredAt,
                null);
    }
}
