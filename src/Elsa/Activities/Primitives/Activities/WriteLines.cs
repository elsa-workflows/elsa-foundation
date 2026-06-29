using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Activities.Primitives.Activities;

/// <summary>
/// Writes multiple lines of text to the console. The multi-line counterpart of <see cref="WriteLine"/>:
/// each element of the <see cref="Lines"/> collection is written on its own line, preserving
/// console-parity with <see cref="WriteLine"/> (same <see cref="Console.WriteLine(string)"/> sink, no
/// <c>IStandardOutStreamProvider</c> indirection). A null/empty collection writes nothing.
/// </summary>
/// <remarks>
/// Ported from elsa-core's <c>WriteLines</c> activity and adapted to this repo's model: the input is an
/// <see cref="InputArgument{T}"/> (not elsa-core's <c>Input&lt;T&gt;</c>) and it derives from
/// <see cref="CodeActivity"/>. Resolved by the existing <c>ClrActivityConstructor</c> like
/// <see cref="WriteLine"/>.
/// </remarks>
public sealed class WriteLines : CodeActivity
{
    /// <summary>The lines to write. Each element is written on its own line in order.</summary>
    public InputArgument<ICollection<string>>? Lines { get; set; }

    protected override void Execute(IActivityExecutionContext context)
    {
        var lines = context.Get(Lines);

        if (lines is null)
            return;

        foreach (var line in lines)
            Console.WriteLine(line);
    }
}
