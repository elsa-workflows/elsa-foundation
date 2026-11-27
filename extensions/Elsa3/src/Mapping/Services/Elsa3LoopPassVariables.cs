using Elsa3.Mapping.ActivityMap.Rewrites;
using Elsa3.Mapping.Conversion;
using Elsa3.Mapping.Resolution;
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
/// <item>Every loop that reaches a block writes it: an inner loop that declares the same name, a loop inside a reusable
/// workflow (which runs inline), and a loop that may run alongside the read (<c>Parallel</c>, <c>Fork</c>, unconnected
/// <c>Flowchart</c> branches).</item>
/// <item>Nothing removes a block: a loop whose body is no container leaves its last pass's variable in the enclosing
/// container, where a later read finds it.</item>
/// <item>A declared variable whose id is <c>{camelName}Variable</c> shares the block.</item>
/// </list>
/// The read binds to the loop only where the pass value wins on every run; where a variable of that name wins on every
/// run, the read keeps today's handling; anything else is refused rather than guessed. The refusals for an inner loop and
/// an id clash come first, even where a nested variable of that name would win (refusal over guessing). Where the read
/// would reach the pass value, a reusable activity anywhere in the owning loop's body is refused, as its contents are not
/// visible here.
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
        {
            // Outside every loop that declares the name, a finished loop whose body is no container left its last pass's
            // variable in an enclosing container, where the read finds it once that loop has run.
            foreach (var loop in plan.Nodes.Keys.Where(node => PassVariable(plan, node, name) is not null))
            {
                var (container, fresh) = Location(plan, loop);
                if (!fresh && (container is null || path.Contains(container)) && PassWins(plan, read, path, container, fresh: false, name))
                    return Ambiguous(name, $"loop '{NodeId(loop)}' leaves its last pass's '{name}' in {(container is null ? "the workflow" : $"'{NodeId(container)}'")}, where the read finds it once that loop has run");
            }

            return Elsa3PassRead.None(name);
        }

        var owner = declaring[0];
        var loopVariable = PassVariable(plan, owner, name)!;
        var others = plan.Nodes.Keys
            .Where(node => !ReferenceEquals(node, owner) && PassVariable(plan, node, name) is not null && !path.Contains(node))
            .ToArray();

        // An inner loop that declares the name overwrites the block the read would find, unless the read always runs before
        // that loop's passes in the owner's pass (in the inner loop's own inputs, or ahead of it in a sequence).
        if (others.FirstOrDefault(node => IsDescendant(plan, node, owner) && !RunsBefore(plan, read, node, owner)) is { } inner)
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
        if (verdicts.All(verdict => !verdict.Wins))
        {
            // A container inside the loop's body hides the pass from every reader. A variable further up hides it only from a
            // JavaScript lookup by memory block before the workflow suspends: Liquid, and JavaScript after a resume (which
            // restores the pass's variable into the body's own scope), read the pass value.
            var body = Body(plan, top);
            return path.Any(scope => IsContainer(scope) && IsDescendant(plan, scope, top) && !ReferenceEquals(scope, body) && Declares(plan, scope, name))
                ? Elsa3PassRead.None(name)
                : Ambiguous(name, $"a variable named '{name}' hides it from a JavaScript lookup until the workflow suspends, while Liquid and a JavaScript read after a resume read loop '{NodeId(owner)}''s pass value");
        }

        // The block the read finds is written by every loop that reaches it: one in the owner's body, or one that may run
        // alongside the read, changes the value under it. A reusable workflow runs inline, and a loop inside it is one too.
        foreach (var reusable in plan.Nodes.Values.Where(node => node.Resolved?.Source == Elsa3ResolvedSource.Replacement).Select(node => node.Node))
        {
            if (IsDescendant(plan, reusable, owner))
                return Ambiguous(name, $"reusable activity '{NodeId(reusable)}' in loop '{NodeId(owner)}''s body runs inline, and a loop inside it that declares '{name}' overwrites the same Elsa 3 variable");
            if (SharesBlock(plan, reusable, top, locations, fresh: true, container: null) && MayRunAlongside(plan, reusable, read))
                return Ambiguous(name, $"reusable activity '{NodeId(reusable)}' may run alongside the read, and a loop inside it that declares '{name}' overwrites the same Elsa 3 variable");
        }

        foreach (var loop in others)
        {
            var (container, fresh) = Location(plan, loop);
            if (SharesBlock(plan, loop, top, locations, fresh, container) && MayRunAlongside(plan, loop, read))
                return Ambiguous(name, $"loop '{NodeId(loop)}' may run alongside the read and overwrite the same Elsa 3 variable");
        }

        if (verdicts.All(verdict => verdict.Wins))
            return new Elsa3PassRead(Elsa3PassReadKind.Loop, name, NodeId(owner), loopVariable);

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
        var body = Body(plan, loop);
        if (body is not null && IsContainer(body))
            return (body, true);
        return (Ancestors(plan, loop).FirstOrDefault(IsContainer), false);
    }

    /// <summary>
    /// Whether <paramref name="writer"/> may write the block the read finds, wherever that block lives (the outermost
    /// declaring loop's location, or an earlier loop's leftover further up): it sits under that loop or that container, or
    /// (with a block of its own in an enclosing container) above it.
    /// </summary>
    private static bool SharesBlock(
        Elsa3ConversionPlan plan,
        Elsa3Activity writer,
        Elsa3Activity top,
        IEnumerable<(Elsa3Activity? Container, bool Fresh, Elsa3Activity? Writer)> locations,
        bool fresh,
        Elsa3Activity? container) =>
        IsDescendant(plan, writer, top) ||
        locations.Any(location =>
            location.Container is null ||
            IsDescendant(plan, writer, location.Container) ||
            !fresh && (container is null || ReferenceEquals(container, location.Container) || IsDescendant(plan, location.Container, container)));

    /// <summary>
    /// Whether two activities may run interleaved: their nearest common ancestor runs its children concurrently (a
    /// <c>Parallel</c> or <c>Fork</c>), or is a <c>Flowchart</c> with no connection path between their branches.
    /// </summary>
    private static bool MayRunAlongside(Elsa3ConversionPlan plan, Elsa3Activity first, Elsa3Activity second)
    {
        var firstChain = new[] { first }.Concat(Ancestors(plan, first)).ToList();
        var secondChain = new[] { second }.Concat(Ancestors(plan, second)).ToList();
        var common = firstChain.FirstOrDefault(node => secondChain.Contains(node));
        if (common is null)
            return true;
        if (ReferenceEquals(common, first) || ReferenceEquals(common, second))
            return false;

        if (common.Type is "Elsa.Parallel" or "Elsa.Fork")
            return true;
        if (common.Type != "Elsa.Flowchart")
            return false;

        var firstBranch = firstChain[firstChain.IndexOf(common) - 1];
        var secondBranch = secondChain[secondChain.IndexOf(common) - 1];
        return !Reaches(common, firstBranch.Id, secondBranch.Id) && !Reaches(common, secondBranch.Id, firstBranch.Id);
    }

    /// <summary>Whether a flowchart's connections lead from one child to another.</summary>
    private static bool Reaches(Elsa3Activity flowchart, string from, string to)
    {
        var connections = flowchart.Connections ?? [];
        var seen = new HashSet<string>(StringComparer.Ordinal) { from };
        var pending = new Queue<string>([from]);
        while (pending.TryDequeue(out var current))
        {
            foreach (var target in connections.Where(connection => connection.Source?.Activity == current).Select(connection => connection.Target?.Activity))
            {
                if (StringComparer.Ordinal.Equals(target, to))
                    return true;
                if (target is null || !seen.Add(target))
                    continue;
                pending.Enqueue(target);
            }
        }

        return false;
    }

    /// <summary>
    /// Whether <paramref name="read"/> runs before <paramref name="loop"/>'s passes, within each pass of
    /// <paramref name="owner"/>: the read is the loop's own input, or comes ahead of it in a <c>Sequence</c> or along a
    /// <c>Flowchart</c>'s connections, and nothing between them and the owner runs them again in the same pass.
    /// </summary>
    private static bool RunsBefore(Elsa3ConversionPlan plan, Elsa3Activity read, Elsa3Activity loop, Elsa3Activity owner)
    {
        if (ReferenceEquals(read, loop))
            return !MayRepeat(plan, loop, owner);

        var readChain = new[] { read }.Concat(Ancestors(plan, read)).ToList();
        var loopChain = new[] { loop }.Concat(Ancestors(plan, loop)).ToList();
        var common = readChain.FirstOrDefault(node => loopChain.Contains(node));
        if (common is null || ReferenceEquals(common, read) || ReferenceEquals(common, loop))
            return false;

        var readBranch = readChain[readChain.IndexOf(common) - 1];
        var loopBranch = loopChain[loopChain.IndexOf(common) - 1];
        var ordered = common.Type switch
        {
            "Elsa.Sequence" => common.Activities?.IndexOf(readBranch) < common.Activities?.IndexOf(loopBranch),
            "Elsa.Flowchart" => Reaches(common, readBranch.Id, loopBranch.Id) && !Reaches(common, loopBranch.Id, readBranch.Id),
            _ => false,
        };
        return ordered && !MayRepeat(plan, readBranch, owner);
    }

    /// <summary>Whether something between <paramref name="node"/> and <paramref name="owner"/> may run it twice in one pass.</summary>
    private static bool MayRepeat(Elsa3ConversionPlan plan, Elsa3Activity node, Elsa3Activity owner)
    {
        var child = node;
        foreach (var ancestor in Ancestors(plan, node))
        {
            if (ReferenceEquals(ancestor, owner))
                return false;
            if (ancestor.Type is "Elsa.While" or "Elsa.DoWhile" or "Elsa.For" or "Elsa.ForEach" or "Elsa.ParallelForEach" ||
                ancestor.Type == "Elsa.Flowchart" && Reaches(ancestor, child.Id, child.Id))
                return true;
            child = ancestor;
        }

        return true;
    }

    private static Elsa3Activity? Body(Elsa3ConversionPlan plan, Elsa3Activity loop) =>
        plan.Nodes.Values.FirstOrDefault(node => ReferenceEquals(node.Parent, loop))?.Node;

    private static bool Declares(Elsa3ConversionPlan plan, Elsa3Activity scope, string name) =>
        ContainerVariables(plan, scope).Any(variable => StringComparer.Ordinal.Equals(variable.Name, name));

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
