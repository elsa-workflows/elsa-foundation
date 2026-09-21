using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Activities.Primitives.Activities;

/// <summary>
/// Reads a single line of text from the console and surfaces it as the activity result.
/// The read counterpart of <see cref="WriteLine"/>: it pulls one line from
/// <see cref="Console.ReadLine"/> and returns one atomic <see cref="ReadLineResult"/> so downstream
/// activities can bind to its stable <c>Result</c> projection.
/// </summary>
/// <remarks>
/// CAVEAT — server-side semantics: a true interactive read isn't meaningful when the workflow runs
/// headless (there is no attached console). This activity mirrors the simplest sensible behavior:
/// <see cref="Console.ReadLine"/> returns <c>null</c> on a closed/redirected stdin, which is surfaced as
/// a <c>null</c> result rather than blocking. Hosts that need genuine interactive input should suspend
/// on a bookmark instead; that durable-input pattern is out of scope for this leaf parity port.
/// </remarks>
public sealed class ReadLine : Activity<ReadLineResult>
{
    protected override ValueTask<ActivityTransition<ReadLineResult>> ExecuteAsync(ActivityExecutionContext context) =>
        ValueTask.FromResult(ActivityTransition.Complete(new ReadLineResult(Console.ReadLine())));
}

/// <summary>The atomic result of reading one line from standard input.</summary>
public sealed record ReadLineResult
{
    public ReadLineResult(string? line) => Line = line;

    /// <summary>The line read, or <c>null</c> when standard input is exhausted.</summary>
    [Output(Key = "Result", Path = "line", IsRequired = false)]
    public string? Line { get; }
}
