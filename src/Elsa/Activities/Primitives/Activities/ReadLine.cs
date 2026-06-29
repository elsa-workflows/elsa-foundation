using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Contracts;

namespace Elsa.Activities.Primitives.Activities;

/// <summary>
/// Reads a single line of text from the console and surfaces it as the activity result.
/// The read counterpart of <see cref="WriteLine"/>: it pulls one line from
/// <see cref="Console.ReadLine"/> and writes it to <see cref="CodeActivity{T}.Result"/> so downstream
/// activities can capture and bind to it.
/// </summary>
/// <remarks>
/// Ported from elsa-core's <c>ReadLine</c> activity and adapted to this repo's model: the read value is
/// emitted through an <see cref="Elsa.Activities.Runtime.Core.Models.OutputArgument{T}"/> via the
/// <see cref="CodeActivity{T}"/> base, and it is resolved by the existing <c>ClrActivityConstructor</c>
/// like <see cref="WriteLine"/>.
///
/// CAVEAT — server-side semantics: a true interactive read isn't meaningful when the workflow runs
/// headless (there is no attached console). This activity mirrors the simplest sensible behavior:
/// <see cref="Console.ReadLine"/> returns <c>null</c> on a closed/redirected stdin, which is surfaced as
/// a <c>null</c> result rather than blocking. Hosts that need genuine interactive input should suspend
/// on a bookmark instead; that durable-input pattern is out of scope for this leaf parity port.
/// </remarks>
public sealed class ReadLine : CodeActivity<string>
{
    protected override void Execute(IActivityExecutionContext context)
    {
        var line = Console.ReadLine();
        context.Set(Result, line);
    }
}
