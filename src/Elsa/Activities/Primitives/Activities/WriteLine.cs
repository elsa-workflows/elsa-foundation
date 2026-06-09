using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Activities.Primitives.Activities;

/// <summary>
/// Writes a line of text to the console. Ported from elsa-core
/// (<c>Elsa.Workflows.Core/Activities/WriteLine.cs</c>) and adapted to this repo's model: the input
/// is an <see cref="InputArgument{T}"/> (not elsa-core's <c>Input&lt;T&gt;</c>), and it derives from
/// <see cref="ActivityBase"/> rather than <c>CodeActivity</c>. Kept deliberately minimal (no
/// <c>IStandardOutStreamProvider</c> indirection).
/// </summary>
public sealed class WriteLine : ActivityBase
{
    /// <summary>The text to write.</summary>
    public InputArgument<string> Text { get; set; } = null!;

    protected override void Execute(IActivityExecutionContext context)
    {
        var text = context.Get(Text);
        Console.WriteLine(text);
    }
}
