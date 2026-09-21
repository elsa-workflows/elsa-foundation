using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Activities.Primitives.Activities;

/// <summary>
/// Writes one hydrated line of text and returns one atomic unit result.
/// </summary>
[ActivityOutcome("Done")]
public sealed class WriteLine : Activity<ActivityUnit>
{
    /// <summary>The text to write.</summary>
    [ActivityInput(Key = "text")]
    [Required]
    public string Text { get; set; } = null!;

    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context)
    {
        Console.WriteLine(Text);
        return ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value));
    }
}
