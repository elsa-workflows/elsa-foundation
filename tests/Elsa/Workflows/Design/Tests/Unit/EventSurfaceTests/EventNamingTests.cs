using Elsa.Events.Core.Contracts;
using Elsa.Workflows.Design.Core.Events;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit.EventSurfaceTests;

/// <summary>
/// SC-011 + Unit C FR-018 + FR-018a: all core events declared in <c>Elsa.Workflows.Design.Core</c>
/// exist with verbatim names. Plus the discipline assertion that no event uses the bare
/// <c>Input</c>/<c>Output</c> names — the <c>WorkflowInput</c>/<c>WorkflowOutput</c> prefix
/// is mandatory because workflow-level inputs/outputs are distinct from per-activity
/// inputs/outputs (which mutate via <c>ActivityPropertyChangedInDraft</c>).
/// </summary>
public sealed class EventNamingTests
{
    private static readonly string[] ExpectedMutationEventNames =
    [
        // Lifecycle origination
        nameof(DraftCreated),
        // Activities (graph)
        nameof(ActivityAddedToDraft),
        nameof(ActivityRemovedFromDraft),
        nameof(ActivityMovedInDraft),
        // Per-activity inputs (full CRUD, symmetric with workflow-input CRUD)
        nameof(ActivityInputAddedToDraft),
        nameof(ActivityInputUpdatedInDraft),
        nameof(ActivityInputRemovedFromDraft),
        // Per-activity outputs (full CRUD, symmetric with workflow-output CRUD)
        nameof(ActivityOutputAddedToDraft),
        nameof(ActivityOutputUpdatedInDraft),
        nameof(ActivityOutputRemovedFromDraft),
        // Variables (definition-bag, full CRUD)
        nameof(VariableDeclaredInDraft),
        nameof(VariableUpdatedInDraft),
        nameof(VariableRemovedFromDraft),
        // Workflow inputs (definition-bag, full CRUD)
        nameof(WorkflowInputAddedToDraft),
        nameof(WorkflowInputUpdatedInDraft),
        nameof(WorkflowInputRemovedFromDraft),
        // Workflow outputs (definition-bag, full CRUD)
        nameof(WorkflowOutputAddedToDraft),
        nameof(WorkflowOutputUpdatedInDraft),
        nameof(WorkflowOutputRemovedFromDraft),
    ];

    private static readonly string[] ExpectedLifecycleEventNames =
    [
        // DraftClonedFromVersion was dropped: both creation routes share the provider's
        // origination lifecycle path, so a cloned Draft emits the single DraftCreated event.
        nameof(DraftDiscarded),
    ];

    [Fact]
    public void All_FR_018_mutation_events_exist()
    {
        var actual = GetWorkflowDesignCoreEventNames();

        foreach (var expected in ExpectedMutationEventNames)
            Assert.Contains(expected, actual);
    }

    [Fact]
    public void The_FR_018a_lifecycle_events_exist()
    {
        var actual = GetWorkflowDesignCoreEventNames();

        foreach (var expected in ExpectedLifecycleEventNames)
            Assert.Contains(expected, actual);
    }

    [Fact]
    public void No_event_uses_the_bare_Input_or_Output_name()
    {
        var actual = GetWorkflowDesignCoreEventNames();

        // Bare names that Sipke item 11's earlier draft used (InputChangedInDraft / OutputChangedInDraft)
        // would conflate workflow-level with per-activity per FR-018 naming discipline.
        Assert.DoesNotContain("InputAddedToDraft", actual);
        Assert.DoesNotContain("InputChangedInDraft", actual);
        Assert.DoesNotContain("InputUpdatedInDraft", actual);
        Assert.DoesNotContain("InputRemovedFromDraft", actual);
        Assert.DoesNotContain("OutputAddedToDraft", actual);
        Assert.DoesNotContain("OutputChangedInDraft", actual);
        Assert.DoesNotContain("OutputUpdatedInDraft", actual);
        Assert.DoesNotContain("OutputRemovedFromDraft", actual);
    }

    [Fact]
    public void Workflows_Design_Core_publishes_exactly_20_events()
    {
        // 19 FR-018 core mutation events (1 origination + 3 activity general [Added/Removed/Moved] +
        // 3 activity input CRUD + 3 activity output CRUD + 3 variable CRUD +
        // 3 wf-input CRUD + 3 wf-output CRUD = 19) plus 1 FR-018a lifecycle (DraftDiscarded) = 20.
        // Graph connection events are Flowchart-module concerns, not Workflows.Design.Core events.
        // DraftClonedFromVersion was dropped — both creation routes share the provider's
        // origination lifecycle path, so a cloned Draft emits the single DraftCreated event.
        // Generic ActivityPropertyChangedInDraft was removed per Joey iteration 2026-05-28 —
        // all per-activity mutations now go through the specialized CRUD events (Add/Remove
        // the whole activity for placement; CRUD on Inputs/Outputs for binding state).
        var actual = GetWorkflowDesignCoreEventNames();

        Assert.Equal(20, actual.Count);
    }

    private static IReadOnlyList<string> GetWorkflowDesignCoreEventNames()
    {
        // After Unit 1's event unification, the FR-018 / FR-018a events in this .Core all
        // implement the single IEvent marker. Scan for it so future additions are picked up.
        return typeof(DraftCreated).Assembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, IsPublic: true })
            .Where(t => typeof(IEvent).IsAssignableFrom(t))
            .Select(t => t.Name)
            .ToList();
    }
}
