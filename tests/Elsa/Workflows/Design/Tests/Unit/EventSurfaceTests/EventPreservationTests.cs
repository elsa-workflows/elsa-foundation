using Elsa.Events.Core.Contracts;
using Elsa.Workflows.Design.Core.Events;
using Elsa.Workflows.Design.Tests.Infrastructure;
using Xunit;
using static Elsa.Workflows.Design.Tests.Infrastructure.UpdateDraftTestKit;

namespace Elsa.Workflows.Design.Tests.Unit.EventSurfaceTests;

/// <summary>
/// T029 (SC-004, SC-008 partial) — the collapse of the 20 granular mutation commands into the
/// single <see cref="Persistence.Core.Contracts.IUpdateDraftCommand"/> preserves the event
/// surface one-for-one, minus graph connection events now owned by a future Flowchart module.
/// All 18 core mutation event types still exist in
/// <c>Elsa.Workflows.Design.Core/Events</c>, each is an <see cref="IEvent"/>, and none was
/// deleted alongside its former command. The 2 lifecycle events
/// (<see cref="OnDraftCreated"/>, <see cref="OnDraftDiscarded"/>) are NOT among the events
/// <c>IUpdateDraftCommand</c> emits — they remain bound to their lifecycle commands (FR-003).
/// </summary>
public sealed class EventPreservationTests
{
    // The 18 core per-diff mutation events re-homed onto IUpdateDraftCommand (Unit 2). Referenced by
    // typeof so this list fails to compile — not merely fails at runtime — if any type is deleted.
    private static readonly Type[] MutationEventTypes =
    [
        // Activities (graph)
        typeof(OnActivityAddedToDraft),
        typeof(OnActivityRemovedFromDraft),
        typeof(OnActivityMovedInDraft),
        // Per-activity inputs (full CRUD)
        typeof(OnActivityInputAddedToDraft),
        typeof(OnActivityInputUpdatedInDraft),
        typeof(OnActivityInputRemovedFromDraft),
        // Per-activity outputs (full CRUD)
        typeof(OnActivityOutputAddedToDraft),
        typeof(OnActivityOutputUpdatedInDraft),
        typeof(OnActivityOutputRemovedFromDraft),
        // Variables (full CRUD)
        typeof(OnVariableDeclaredInDraft),
        typeof(OnVariableUpdatedInDraft),
        typeof(OnVariableRemovedFromDraft),
        // Workflow inputs (full CRUD)
        typeof(OnWorkflowInputAddedToDraft),
        typeof(OnWorkflowInputUpdatedInDraft),
        typeof(OnWorkflowInputRemovedFromDraft),
        // Workflow outputs (full CRUD)
        typeof(OnWorkflowOutputAddedToDraft),
        typeof(OnWorkflowOutputUpdatedInDraft),
        typeof(OnWorkflowOutputRemovedFromDraft),
    ];

    private static readonly Type[] LifecycleEventTypes =
    [
        typeof(OnDraftCreated),
        typeof(OnDraftDiscarded),
    ];

    [Fact]
    public void All_18_core_mutation_event_types_still_exist_and_are_events()
    {
        Assert.Equal(18, MutationEventTypes.Length);

        foreach (var type in MutationEventTypes)
            Assert.True(typeof(IEvent).IsAssignableFrom(type), $"{type.Name} must implement IEvent");
    }

    [Fact]
    public void No_mutation_event_type_was_deleted_from_the_Core_events_assembly()
    {
        var declared = typeof(OnDraftCreated).Assembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, IsPublic: true })
            .Where(t => typeof(IEvent).IsAssignableFrom(t))
            .ToHashSet();

        foreach (var type in MutationEventTypes)
            Assert.Contains(type, declared);
    }

    [Fact]
    public async Task IUpdateDraftCommand_emits_no_lifecycle_events()
    {
        // SC-008 (partial): the lifecycle events stay bound to Create/Clone/Discard — an Update
        // that produces several diffs must publish none of them.
        using var host = await WorkflowsDesignTestHost.CreateAsync();
        var draftId = await SeedEmptyDraft(host); // emits OnDraftCreated (a lifecycle event) — pre-skip.

        var skip = host.EventPublisher.CapturedEvents.Count;
        await Update(host, draftId, State(
            activities: [Node("node-1")],
            variables: [Variable("v1", "MyVar")]));

        var afterUpdate = host.EventPublisher.CapturedEvents.Skip(skip);
        foreach (var lifecycleType in LifecycleEventTypes)
            Assert.DoesNotContain(afterUpdate, e => e.GetType() == lifecycleType);
    }
}
