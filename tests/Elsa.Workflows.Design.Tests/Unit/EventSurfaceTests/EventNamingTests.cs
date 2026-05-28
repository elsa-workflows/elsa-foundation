using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Core.Events;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit.EventSurfaceTests;

/// <summary>
/// SC-011 + Unit C FR-018 + FR-018a: all 18 events declared in <c>Elsa.Workflows.Design.Core</c>
/// exist with verbatim names. Plus the discipline assertion that no event uses the bare
/// <c>Input</c>/<c>Output</c> names — the <c>WorkflowInput</c>/<c>WorkflowOutput</c> prefix
/// is mandatory because workflow-level inputs/outputs are distinct from per-activity
/// inputs/outputs (which mutate via <c>OnActivityPropertyChangedInDraft</c>).
/// </summary>
public sealed class EventNamingTests
{
    private static readonly string[] ExpectedMutationEventNames =
    [
        // Lifecycle origination
        nameof(OnDraftCreated),
        // Activities (graph)
        nameof(OnActivityAddedToDraft),
        nameof(OnActivityRemovedFromDraft),
        nameof(OnActivityMovedInDraft),
        // Per-activity inputs (full CRUD, symmetric with workflow-input CRUD)
        nameof(OnActivityInputAddedToDraft),
        nameof(OnActivityInputUpdatedInDraft),
        nameof(OnActivityInputRemovedFromDraft),
        // Per-activity outputs (full CRUD, symmetric with workflow-output CRUD)
        nameof(OnActivityOutputAddedToDraft),
        nameof(OnActivityOutputUpdatedInDraft),
        nameof(OnActivityOutputRemovedFromDraft),
        // Connections (graph)
        nameof(OnConnectionAddedToDraft),
        nameof(OnConnectionRemovedFromDraft),
        // Variables (definition-bag, full CRUD)
        nameof(OnVariableDeclaredInDraft),
        nameof(OnVariableUpdatedInDraft),
        nameof(OnVariableRemovedFromDraft),
        // Workflow inputs (definition-bag, full CRUD)
        nameof(OnWorkflowInputAddedToDraft),
        nameof(OnWorkflowInputUpdatedInDraft),
        nameof(OnWorkflowInputRemovedFromDraft),
        // Workflow outputs (definition-bag, full CRUD)
        nameof(OnWorkflowOutputAddedToDraft),
        nameof(OnWorkflowOutputUpdatedInDraft),
        nameof(OnWorkflowOutputRemovedFromDraft),
    ];

    private static readonly string[] ExpectedLifecycleEventNames =
    [
        nameof(OnDraftClonedFromVersion),
        nameof(OnDraftDiscarded),
    ];

    [Fact]
    public void All_FR_018_mutation_events_exist()
    {
        var actual = GetWorkflowDesignCoreEventNames();

        foreach (var expected in ExpectedMutationEventNames)
            Assert.Contains(expected, actual);
    }

    [Fact]
    public void Both_FR_018a_lifecycle_events_exist()
    {
        var actual = GetWorkflowDesignCoreEventNames();

        foreach (var expected in ExpectedLifecycleEventNames)
            Assert.Contains(expected, actual);
    }

    [Fact]
    public void No_event_uses_the_bare_Input_or_Output_name()
    {
        var actual = GetWorkflowDesignCoreEventNames();

        // Bare names that Sipke item 11's earlier draft used (OnInputChangedInDraft / OnOutputChangedInDraft)
        // would conflate workflow-level with per-activity per FR-018 naming discipline.
        Assert.DoesNotContain("OnInputAddedToDraft", actual);
        Assert.DoesNotContain("OnInputChangedInDraft", actual);
        Assert.DoesNotContain("OnInputUpdatedInDraft", actual);
        Assert.DoesNotContain("OnInputRemovedFromDraft", actual);
        Assert.DoesNotContain("OnOutputAddedToDraft", actual);
        Assert.DoesNotContain("OnOutputChangedInDraft", actual);
        Assert.DoesNotContain("OnOutputUpdatedInDraft", actual);
        Assert.DoesNotContain("OnOutputRemovedFromDraft", actual);
    }

    [Fact]
    public void Workflows_Design_Core_publishes_exactly_23_events()
    {
        // 21 FR-018 mutation events (1 origination + 3 activity general [Added/Removed/Moved] +
        // 3 activity input CRUD + 3 activity output CRUD + 2 connection + 3 variable CRUD +
        // 3 wf-input CRUD + 3 wf-output CRUD = 21) plus 2 FR-018a lifecycle = 23 total.
        // Generic OnActivityPropertyChangedInDraft was removed per Joey iteration 2026-05-28 —
        // all per-activity mutations now go through the specialized CRUD events (Add/Remove
        // the whole activity for placement; CRUD on Inputs/Outputs for binding state).
        var actual = GetWorkflowDesignCoreEventNames();

        Assert.Equal(23, actual.Count);
    }

    private static IReadOnlyList<string> GetWorkflowDesignCoreEventNames()
    {
        return typeof(OnDraftCreated).Assembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, IsPublic: true })
            .Where(t => typeof(IDomainEvent).IsAssignableFrom(t))
            .Select(t => t.Name)
            .ToList();
    }
}
