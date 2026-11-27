namespace Elsa3.Mapping.ActivityMap.Rewrites;

/// <summary>
/// For and ForEach (R4 with lowering): Elsa 3 exposes the current loop value as the <c>CurrentValue</c> output; Elsa 4
/// declares scoped loop variables instead (<c>index</c> for For, <c>currentItem</c> for ForEach). The rewrite registers
/// the loop variable, so value-flow lowering turns references to <c>CurrentValue</c> inside the body into reads of that
/// variable, with the loop node as declaring scope.
/// </summary>
internal sealed class Elsa3LoopVariableReferencesRewrite : IElsa3NodeRewrite
{
    /// <summary>The Elsa 4 loop variables, by loop type key.</summary>
    internal static readonly IReadOnlyDictionary<string, string> CurrentValueVariables = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Elsa.Activities.For.Activities.For"] = "index",
        ["Elsa.Activities.ForEach.Activities.ForEach"] = "currentItem",
    };

    /// <summary>
    /// The per-pass variables an Elsa 3 loop declares on its body, by Elsa 4 loop type key: each Elsa 3 name with the Elsa 4
    /// loop variable that holds the same value (#5873). Elsa 3 <c>ForEach</c> declares <c>CurrentValue</c> and
    /// <c>CurrentIndex</c> (<c>ForEachT.cs:97-98</c> at 3.6.3); <c>For</c> declares <c>CurrentValue</c> only
    /// (<c>For.cs:105-108</c>). Elsa 3 matches these names ordinally. A loop that converts to another Elsa 4 loop adds
    /// its row here (spec 1243 FR-013, PR #5841, for <c>ParallelForEach</c>).
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> PassVariables =
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
        {
            ["Elsa.Activities.For.Activities.For"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["CurrentValue"] = "index" },
            ["Elsa.Activities.ForEach.Activities.ForEach"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["CurrentValue"] = "currentItem",
                ["CurrentIndex"] = "currentIndex",
            },
        };

    /// <summary>Every Elsa 4 loop variable a lowered reference may name.</summary>
    internal static readonly IReadOnlySet<string> LoopVariableKeys = new HashSet<string>(["index", "currentItem", "currentIndex"], StringComparer.Ordinal);

    public Elsa3NodeRewriteKind Kind => Elsa3NodeRewriteKind.LoopVariableReferences;

    public ValueTask ApplyAsync(Elsa3RewriteContext context, CancellationToken cancellationToken)
    {
        if (context.Node.Resolved?.ActivityTypeKey is { } typeKey &&
            CurrentValueVariables.TryGetValue(typeKey, out var variable) &&
            context.TryGetProperty("CurrentValue", out var property, out _))
            context.Node.LoopVariables[property] = variable;
        return ValueTask.CompletedTask;
    }
}
