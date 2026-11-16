using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Xunit;

namespace Elsa.Activities.ControlFlow.Tests;

/// <summary>
/// Spec 1208 FR-002b conformance: an activity that declares no <c>Done</c> and carries the every-completion guarantee
/// must complete with a named selection, never an unspecified one, which would fault against its pinned outcome set.
/// </summary>
internal static class NamedSelectionConformance
{
    public static void AssertNamedSelection(WorkflowExecutionRun run, string executableNodeId, params string[] expected)
    {
        var state = run.AssertCompleted(executableNodeId);
        var completion = state.Completion;
        Assert.True(completion is not null, $"Activity '{executableNodeId}' recorded no completion, so its selection is unknown.");
        Assert.Equal(OutcomeSelectionState.Named, completion.Selection.State);
        Assert.Equal(expected, completion.Selection.ResolveNames());
    }
}
