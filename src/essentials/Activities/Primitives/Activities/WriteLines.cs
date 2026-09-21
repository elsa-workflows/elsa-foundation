using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Activities.Primitives.Activities;

/// <summary>
/// Writes multiple lines of text to the console. The multi-line counterpart of <see cref="WriteLine"/>:
/// each element of the <see cref="Lines"/> collection is written on its own line, preserving
/// console-parity with <see cref="WriteLine"/> (same <see cref="Console.WriteLine(string)"/> sink, no
/// <c>IStandardOutStreamProvider</c> indirection). A null/empty collection writes nothing.
/// </summary>
/// <remarks>
/// The runtime hydrates <see cref="Lines"/> from the invocation's committed input snapshot before
/// execution. The activity owns no argument wrapper or value address.
/// </remarks>
public sealed class WriteLines : Activity<ActivityUnit>
{
    /// <summary>The lines to write. Each element is written on its own line in order.</summary>
    [ActivityInput(Key = nameof(Lines))]
    public List<string>? Lines { get; set; }

    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context)
    {
        if (Lines is not null)
        {
            foreach (var line in Lines)
                Console.WriteLine(line);
        }

        return ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value));
    }
}
