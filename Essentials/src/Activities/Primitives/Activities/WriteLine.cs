using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Activities.Primitives.Activities;

/// <summary>
/// Writes one hydrated line of text and returns one atomic unit result.
/// </summary>
// ReplaySafe (#2581): the body writes one console line from its materialized input and returns a constant
// transition. A crash before the next commit runs it again and writes the line twice; no workflow state differs.
// The text leaves the workflow on the console, so a secret binding is refused, as on Inline.
[RefusesSecretBinding("text", SecretBindingRefusalReason.EchoedToOutput)]
[ActivityOutcome("Done")]
[ActivitySideEffectProfile(SideEffectProfile.ReplaySafe)]
public sealed class WriteLine : Activity<ActivityUnit>
{
    /// <summary>The text to write.</summary>
    [ActivityInput(Key = "text")]
    [Required]
    public string Text { get; set; } = null!;

    /// <summary>Writes the hydrated text to the console and completes with a unit result.</summary>
    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context)
    {
        Console.WriteLine(Text);
        return ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value));
    }
}
