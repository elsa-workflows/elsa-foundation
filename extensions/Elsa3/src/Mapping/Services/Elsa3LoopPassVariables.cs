using Elsa3.Mapping.ActivityMap.Rewrites;
using Elsa3.Mapping.Conversion;
using Elsa3.Models;

namespace Elsa3.Mapping.Services;

/// <summary>What an Elsa 3 by-name read of a loop's per-pass variable reached.</summary>
internal enum Elsa3PassReadKind
{
    /// <summary>No enclosing loop's per-pass variable: the read keeps the handling of any other variable.</summary>
    None,

    /// <summary>The owning loop's current pass value, on every run.</summary>
    Loop,

    /// <summary>The pass value on some runs and another value on others, depending on what ran before.</summary>
    Ambiguous,
}

/// <summary>
/// The outcome of a read of the per-pass variable <paramref name="Name"/>: for <see cref="Elsa3PassReadKind.Loop"/>, the loop
/// node that owns it and the Elsa 4 loop variable that holds its value; for <see cref="Elsa3PassReadKind.Ambiguous"/>, why.
/// </summary>
internal sealed record Elsa3PassRead(Elsa3PassReadKind Kind, string Name, string? ScopeNodeId = null, string? LoopVariable = null, string? Reason = null)
{
    public static Elsa3PassRead None(string name) => new(Elsa3PassReadKind.None, name);
}

/// <summary>
/// Which value an Elsa 3 script read when it named a loop's per-pass variable (#5873; spec 1243 FR-013, PR #5841, specifies
/// the same rule for ParallelForEach, which adds its row to <see cref="Elsa3LoopVariableReferencesRewrite.PassVariables"/>).
/// It models Elsa 3.6.3:
/// <list type="bullet">
/// <item>A loop creates each pass's variable (<c>Variable(name, value)</c>, id <c>{camelName}Variable</c>) through
/// <c>CreateVariable</c>, which stores it in the nearest variable container from the body up (the body itself when it is a
/// <c>Sequence</c>, <c>Flowchart</c> or <c>Parallel</c>), or overwrites a block of that id already there or further up.</item>
/// <item>A name is looked up scope by scope from the reader up, and within a scope in declaration order: a container's own
/// variables, declared when it started, come before a block added to it later; a body container, created per pass, holds
/// the pass's block before its own variables.</item>
/// <item>An inner loop that declares the same name overwrites the outer loop's block.</item>
/// </list>
/// The read binds to the loop only where the pass value wins on every run; where a variable of that name wins on every
/// run, the read keeps today's handling; anything else is refused rather than guessed.
/// </summary>
internal static class Elsa3LoopPassVariables
{
    /// <summary>The Elsa 3 activities that are variable containers (<c>Container</c> subclasses).</summary>
    private static readonly HashSet<string> ContainerTypes = new(["Elsa.Sequence", "Elsa.Flowchart", "Elsa.Parallel"], StringComparer.Ordinal);

    /// <summary>Every per-pass name some loop declares.</summary>
    public static IReadOnlySet<string> Names { get; } =
        Elsa3LoopVariableReferencesRewrite.PassVariables.Values.SelectMany(names => names.Keys).ToHashSet(StringComparer.Ordinal);

    /// <summary>What a by-name read of <paramref name="name"/> from <paramref name="read"/> reached.</summary>
    public static Elsa3PassRead Resolve(Elsa3ConversionPlan plan, Elsa3Activity read, string name)
    {
        var path = Ancestors(plan, read);
        var declaring = path.Where(node => PassVariable(plan, node, name) is not null).ToArray();
        if (declaring.Length == 0)
            return Elsa3PassRead.None(name);

        var owner = declaring[0];
        var loopVariable = PassVariable(plan, owner, name)!;
        var others = plan.Nodes.Keys
            .Where(node => !ReferenceEquals(node, owner) && PassVariable(plan, node, name) is not null && !path.Contains(node))
            .ToArray();

        // An inner loop that declares the name overwrites the block the read would find.
        if (others.FirstOrDefault(node => IsDescendant(plan, node, owner)) is { } inner)
            return Ambiguous(name, $"loop '{NodeId(inner)}' in loop '{NodeId(owner)}''s body also declares '{name}' and overwrites the same Elsa 3 variable");

        var variableId = $"{char.ToLowerInvariant(name[0])}{name[1..]}Variable";
        if (path.Any(node => IsContainer(node) && ContainerVariables(plan, node).Any(variable => StringComparer.Ordinal.Equals(variable.Id, variableId))) ||
            plan.Definition.Variables.Any(variable => StringComparer.Ordinal.Equals(variable.Id, variableId)))
            return Ambiguous(name, $"a declared variable has the id '{variableId}' that Elsa 3 gave the per-pass variable, so both share one value");

        // Where the block lives: the outermost declaring loop's location, unless a loop that may have run earlier left a
        // block further up, which the pass then overwrites in place.
        var top = declaring[^1];
        var (topContainer, topFresh) = Location(plan, top);
        var locations = new List<(Elsa3Activity? Container, bool Fresh, Elsa3Activity? Writer)> { (topContainer, topFresh, null) };
        foreach (var earlier in others.Where(node => !IsDescendant(plan, node, top)))
        {
            var (container, fresh) = Location(plan, earlier);
            if (!fresh && IsStrictAncestor(plan, container, topContainer))
                locations.Add((container, false, earlier));
        }

        var verdicts = locations.Select(location => (location.Writer, Wins: PassWins(plan, read, path, location.Container, location.Fresh, name))).ToArray();
        if (verdicts.All(verdict => verdict.Wins))
            return new Elsa3PassRead(Elsa3PassReadKind.Loop, name, NodeId(owner), loopVariable);
        if (verdicts.All(verdict => !verdict.Wins))
            return Elsa3PassRead.None(name);

        var writer = verdicts.First(verdict => verdict.Writer is not null).Writer!;
        return Ambiguous(name, $"loop '{NodeId(writer)}' may run first and leave its '{name}' where a variable named '{name}' hides it");
    }

    /// <summary>The per-pass reads a computed name could reach from <paramref name="read"/>: those not plainly another variable.</summary>
    public static IReadOnlyList<Elsa3PassRead> Reachable(Elsa3ConversionPlan plan, Elsa3Activity read) =>
        Names.Order(StringComparer.Ordinal)
            .Select(name => Resolve(plan, read, name))
            .Where(result => result.Kind != Elsa3PassReadKind.None)
            .ToArray();

    /// <summary>
    /// The variable name Elsa 3's generated <c>get{Name}</c>/<c>set{Name}</c> read from <paramref name="read"/>, when one of
    /// the names that <paramref name="matches"/> is in scope. Elsa 3 registered one per distinct name in scope: from the
    /// reader up, a scope's own variables and then the per-pass variables on it, ending with the workflow's (in a reusable
    /// run, its inputs too); a later registration of the same function replaced an earlier one, so the last name wins.
    /// </summary>
    public static string? AccessorName(Elsa3ConversionPlan plan, Elsa3Activity read, Func<string, bool> matches)
    {
        var names = new List<string>();
        foreach (var scope in new[] { read }.Concat(Ancestors(plan, read)))
        {
            if (IsContainer(scope))
                names.AddRange(ContainerVariables(plan, scope).Select(variable => variable.Name));
            if (plan.Find(scope)?.Parent is { } loop && PassVariables(plan, loop) is { } passNames)
                names.AddRange(passNames.Keys);
        }

        names.AddRange(plan.Definition.Variables.Select(variable => variable.Name));
        if (plan.Request.IsReusable)
            names.AddRange(plan.Definition.Inputs.Select(input => input.Name));

        return names.Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.Ordinal).LastOrDefault(matches);
    }

    /// <summary>Whether the pass's block, at <paramref name="location"/>, is found before any variable named <paramref name="name"/>.</summary>
    private static bool PassWins(Elsa3ConversionPlan plan, Elsa3Activity read, IReadOnlyList<Elsa3Activity> path, Elsa3Activity? location, bool fresh, string name)
    {
        // The reader is the body container that holds the block: its own scope comes first.
        if (location is not null && ReferenceEquals(location, read))
            return true;

        foreach (var scope in path)
        {
            var declares = IsContainer(scope) && ContainerVariables(plan, scope).Any(variable => StringComparer.Ordinal.Equals(variable.Name, name));
            if (ReferenceEquals(scope, location))
                return fresh || !declares;
            if (declares)
                return false;
        }

        // The workflow's own scope, whose variables were declared before any loop ran.
        return !plan.Definition.Variables.Any(variable => StringComparer.Ordinal.Equals(variable.Name, name)) &&
               !(plan.Request.IsReusable && plan.Definition.Inputs.Any(input => StringComparer.Ordinal.Equals(input.Name, name)));
    }

    /// <summary>
    /// Where a loop's <c>CreateVariable</c> stores the pass's block when no block of its id exists further up: its body when
    /// that is a container (created per pass, so the block comes before the body's own variables), else the nearest enclosing
    /// container (null: the workflow), whose own variables come first.
    /// </summary>
    private static (Elsa3Activity? Container, bool Fresh) Location(Elsa3ConversionPlan plan, Elsa3Activity loop)
    {
        var body = plan.Nodes.Values.FirstOrDefault(node => ReferenceEquals(node.Parent, loop))?.Node;
        if (body is not null && IsContainer(body))
            return (body, true);
        return (Ancestors(plan, loop).FirstOrDefault(IsContainer), false);
    }

    private static Elsa3PassRead Ambiguous(string name, string reason) => new(Elsa3PassReadKind.Ambiguous, name, Reason: reason);

    private static string? PassVariable(Elsa3ConversionPlan plan, Elsa3Activity node, string name) =>
        PassVariables(plan, node)?.GetValueOrDefault(name);

    private static IReadOnlyDictionary<string, string>? PassVariables(Elsa3ConversionPlan plan, Elsa3Activity node) =>
        plan.Find(node)?.Resolved?.ActivityTypeKey is { } typeKey ? Elsa3LoopVariableReferencesRewrite.PassVariables.GetValueOrDefault(typeKey) : null;

    private static bool IsContainer(Elsa3Activity node) => ContainerTypes.Contains(node.Type);

    private static IEnumerable<(string Id, string Name)> ContainerVariables(Elsa3ConversionPlan plan, Elsa3Activity node) =>
        plan.Find(node)?.Variables.Select(variable => (variable.ReferenceKey, variable.Name)) ?? [];

    private static List<Elsa3Activity> Ancestors(Elsa3ConversionPlan plan, Elsa3Activity node)
    {
        var ancestors = new List<Elsa3Activity>();
        for (var parent = plan.Find(node)?.Parent; parent is not null; parent = plan.Find(parent)?.Parent)
            ancestors.Add(parent);
        return ancestors;
    }

    private static bool IsDescendant(Elsa3ConversionPlan plan, Elsa3Activity node, Elsa3Activity ancestor) =>
        Ancestors(plan, node).Any(parent => ReferenceEquals(parent, ancestor));

    /// <summary>Whether <paramref name="outer"/> (null: the workflow) encloses <paramref name="inner"/> and is not it.</summary>
    private static bool IsStrictAncestor(Elsa3ConversionPlan plan, Elsa3Activity? outer, Elsa3Activity? inner) =>
        inner is not null && (outer is null || IsDescendant(plan, inner, outer));

    private static string NodeId(Elsa3Activity node) => !string.IsNullOrWhiteSpace(node.NodeId) ? node.NodeId : node.Id;
}
