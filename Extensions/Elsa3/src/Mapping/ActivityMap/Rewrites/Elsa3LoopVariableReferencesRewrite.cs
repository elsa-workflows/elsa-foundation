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
