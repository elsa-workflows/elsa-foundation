using System.Globalization;
using System.Text.Json;
using Elsa3.Mapping.ActivityMap;
using Elsa3.Mapping.ActivityMap.Rewrites;
using Elsa3.Mapping.Resolution;
using Elsa3.Models;
using static Elsa3.Models.Elsa3ActivityDiagnosticCodes;

namespace Elsa3.Mapping.Conversion;

/// <summary>
/// Converts one Elsa 3 flowchart's connections into the Elsa 4 Flowchart's <c>connections</c> and <c>startNodeId</c>
/// (spec 239, research R-4 to R-8). Runs after every node is normalized, so each child's Elsa 4 outcomes are known.
/// FlowFork, FlowJoin and Start leave no node (G-2): a fork becomes fan-out connections, a join the implicit join of
/// what follows it, Start the start node. A shape Elsa 4 cannot express exactly is reported with a stable code (G-1);
/// a connection Elsa 3 never follows is removed with a warning (G-7). Nothing throws for input.
/// </summary>
internal static class Elsa3FlowchartGraphBuilder
{
    public const string FlowchartActivityTypeKey = "Elsa.Activities.Flowchart.Activities.Flowchart";
    public const string ActivitiesSlot = "Flowchart.Activities";
    private const string ConnectionsField = "connections";
    private const string StartNodeIdField = "startNodeId";
    private const string Done = Elsa3ActivityOutcomes.Done;
    private const string Break = "Break";
    private const string SwitchCaseSlot = "Switch.Case";
    private const string MatchPlaceholder = "{match}";
    private static readonly HashSet<string> TerminalTypes = new(StringComparer.Ordinal) { "Elsa.Complete", "Elsa.End", "Elsa.Break" };

    /// <summary>Whether <paramref name="node"/> converts to the Elsa 4 Flowchart.</summary>
    public static bool IsFlowchart(Elsa3NodePlan? node) =>
        StringComparer.Ordinal.Equals(node?.Resolved?.ActivityTypeKey, FlowchartActivityTypeKey);

    /// <summary>Builds the graph of <paramref name="flowchart"/>; on any error nothing is written and the errors are reported.</summary>
    public static void Build(Elsa3ConversionPlan plan, Elsa3NodePlan flowchart) => new Graph(plan, flowchart).Build();

    private sealed record Edge(int Index, Elsa3Activity Source, string Port, Elsa3Activity Target)
    {
        /// <summary>The Elsa 4 ports this connection becomes; constructs keep <c>Done</c> until they are collapsed.</summary>
        public IReadOnlyList<string> Elsa4Ports { get; set; } = [];
    }

    private enum JoinMode { WaitAll, WaitAllActive, WaitAny }

    private sealed class Graph(Elsa3ConversionPlan plan, Elsa3NodePlan flowchart)
    {
        private readonly List<Elsa3Activity> _children = flowchart.Slots
            .Where(slot => StringComparer.Ordinal.Equals(slot.SlotName, ActivitiesSlot) && slot.Child is not null)
            .Select(slot => slot.Child!)
            .ToList();
        private bool _failed;

        public void Build()
        {
            var edges = CollectEdges();
            ChainFollowers(edges);
            edges = DropDeadEdges(edges);
            MapPorts(edges);
            if (_failed)
                return;

            var start = StartCandidate(edges);
            var forward = RemoveLoops(edges, start);
            CheckJoins(forward);
            if (_failed)
                return;

            var collapsed = Collapse(edges, ref start);
            CheckStartTriggers(collapsed, start);
            if (_failed)
                return;

            Write(collapsed, start);
        }

        // ── Stage 1: connections → edges ──────────────────────────────────────────────────────────────────────────

        private List<Edge> CollectEdges()
        {
            var edges = new List<Edge>();
            var connections = flowchart.Node.Connections ?? [];
            var byId = new Dictionary<string, Elsa3Activity>(StringComparer.Ordinal);
            var ambiguous = new HashSet<string>(StringComparer.Ordinal);
            var duplicates = new HashSet<Elsa3Activity>(ReferenceEqualityComparer.Instance);
            foreach (var child in _children)
            {
                if (!string.IsNullOrWhiteSpace(child.Id))
                    Register(child.Id, child);
                Register(Elsa3ConversionDiagnostics.NodeId(child), child);
            }

            for (var index = 0; index < connections.Count; index++)
            {
                var connection = connections[index];
                var sourceId = connection?.Source?.Activity;
                var targetId = connection?.Target?.Activity;
                // A shared id is already refused where the second child registered it; never guess which child it means.
                if (new[] { sourceId, targetId }.Any(id => id is not null && ambiguous.Contains(id)))
                    continue;

                var source = sourceId is null ? null : byId.GetValueOrDefault(sourceId);
                var target = targetId is null ? null : byId.GetValueOrDefault(targetId);
                var port = string.IsNullOrWhiteSpace(connection?.Source?.Port) ? Done : connection!.Source.Port;
                if (source is null || target is null)
                {
                    var missing = source is null ? $"source '{sourceId}'" : $"target '{targetId}'";
                    Report(Elsa3MigrationDiagnosticSeverity.Error, GraphConnectionInvalid, ConnectionPath(index), source ?? flowchart.Node,
                        $"Connection {index} of flowchart '{NodeId(flowchart.Node)}' names {missing}, which is not an activity of this flowchart.",
                        "Remove the connection, or connect it to an activity of the same flowchart, before converting.",
                        ConnectionMetadata(target is null ? targetId : NodeId(target), port));
                    continue;
                }

                edges.Add(new Edge(index, source, port, target));
            }

            return edges;

            void Register(string id, Elsa3Activity child)
            {
                if (byId.TryAdd(id, child) || ReferenceEquals(byId[id], child))
                    return;

                ambiguous.Add(id);
                // Reported once per child, even when both its Id and its NodeId collide.
                if (!duplicates.Add(child))
                    return;
                Error(GraphDuplicateNodeId, plan.Find(child)!.JsonPath, child,
                    $"'{NodeId(child)}' ({child.Type}) uses id '{id}', which activity '{NodeId(byId[id])}' of flowchart '{NodeId(flowchart.Node)}' already uses. Connections and Elsa 4 node ids need one activity per id.",
                    "Give every activity of the flowchart a unique id before converting.");
            }
        }

        /// <summary>
        /// A node followed by synthesized nodes (HttpEndpoint projections, plan D-12) hands its outbound connections to the
        /// last of them, and the chain is joined on <c>Done</c>.
        /// </summary>
        private void ChainFollowers(List<Edge> edges)
        {
            foreach (var child in _children.ToArray())
            {
                if (plan.Find(child) is not { FollowedBy.Count: > 0 } childPlan)
                    continue;

                var chain = new[] { child }.Concat(childPlan.FollowedBy).ToArray();
                for (var index = 0; index < edges.Count; index++)
                {
                    if (ReferenceEquals(edges[index].Source, child))
                        edges[index] = edges[index] with { Source = chain[^1] };
                }

                for (var link = 0; link < chain.Length - 1; link++)
                    edges.Add(new Edge(-1, chain[link], Done, chain[link + 1]));
            }
        }

        // ── Stage 2: connections Elsa 3 never follows, and construct ports ───────────────────────────────────────

        private List<Edge> DropDeadEdges(List<Edge> edges)
        {
            var kept = new List<Edge>();
            var branchesByFork = new Dictionary<Elsa3Activity, IReadOnlyList<string>?>(ReferenceEqualityComparer.Instance);
            foreach (var edge in edges)
            {
                if (TerminalTypes.Contains(edge.Source.Type ?? string.Empty))
                {
                    EdgeWarning(DeadConnectionRemoved, edge,
                        $"Connection {edge.Index} leaves '{NodeId(edge.Source)}' ({edge.Source.Type}), which ends its flowchart or workflow, so Elsa 3 never follows it; it is removed.",
                        "Remove the connection from the Elsa 3 workflow.");
                    continue;
                }

                switch (Construct(edge.Source))
                {
                    case Elsa3GraphConstructKind.Fork:
                        if (!branchesByFork.TryGetValue(edge.Source, out var branches))
                            branchesByFork[edge.Source] = branches = ForkBranches(edge.Source);
                        if (branches is null)
                            continue; // reported once per fork
                        if (!(branches.Count == 0 ? StringComparer.Ordinal.Equals(edge.Port, Done) : branches.Contains(edge.Port, StringComparer.Ordinal)))
                        {
                            EdgeWarning(DeadConnectionRemoved, edge,
                                $"Connection {edge.Index} leaves fork '{NodeId(edge.Source)}' on '{edge.Port}', which is not one of its branches, so Elsa 3 never follows it; it is removed.",
                                "Remove the connection, or add the branch to the fork, in the Elsa 3 workflow.");
                            continue;
                        }

                        edge.Elsa4Ports = [Done];
                        break;
                    case Elsa3GraphConstructKind.Join:
                    case Elsa3GraphConstructKind.Start:
                        if (!StringComparer.Ordinal.Equals(edge.Port, Done))
                        {
                            EdgeError(GraphOutcomeUnmapped, edge,
                                $"Connection {edge.Index} leaves '{NodeId(edge.Source)}' ({edge.Source.Type}) on '{edge.Port}'; it only completes with 'Done'.",
                                "Connect from 'Done' before converting.");
                            continue;
                        }

                        edge.Elsa4Ports = [Done];
                        break;
                }

                kept.Add(edge);
            }

            return kept;
        }

        /// <summary>A fork's literal branches; null (reported) when Elsa 3 computes them at run time.</summary>
        private IReadOnlyList<string>? ForkBranches(Elsa3Activity fork)
        {
            var forkPlan = plan.Find(fork)!;
            if (forkPlan.GraphConstructSetting is not { } setting || !Elsa3TypedValues.IsBound(setting))
                return [];
            if (Elsa3TerminalPlacementRewrite.TryReadNameList(setting, out var text, out var names) && names is not null)
                return names;

            Error(UnsupportedMode, $"{forkPlan.JsonPath}.branches", fork,
                names is null && text.Length > 0
                    ? $"Fork '{NodeId(fork)}' has branches '{text}', which cannot be read as a list of names."
                    : $"Fork '{NodeId(fork)}' sets its branches by an expression, which only Elsa 3 can evaluate.",
                "List the fork's branches literally before converting.");
            return null;
        }

        // ── Stage 3: Elsa 3 ports → Elsa 4 ports ─────────────────────────────────────────────────────────────────

        private void MapPorts(List<Edge> edges)
        {
            foreach (var edge in edges.ToArray())
            {
                if (Construct(edge.Source) is not null)
                    continue;

                var sourcePlan = plan.Find(edge.Source);
                if (sourcePlan?.Resolved is not { } resolved)
                {
                    // The node itself is unconvertible and already reported; its connections cannot be routed.
                    _failed = true;
                    continue;
                }

                var rule = resolved.Entry?.Outcomes.FirstOrDefault(candidate => StringComparer.Ordinal.Equals(candidate.Elsa3Outcome, edge.Port));
                if (rule is not null)
                {
                    if (rule.Elsa4Outcomes.Count == 0)
                    {
                        EdgeError(NoCounterpart, edge,
                            $"Connection {edge.Index} leaves '{NodeId(edge.Source)}' on '{edge.Port}', which has no Elsa 4 counterpart: {rule.UnsupportedReason}",
                            "Remove the connection, or redesign this part of the workflow, before converting.");
                        continue;
                    }

                    edge.Elsa4Ports = rule.Elsa4Outcomes
                        .SelectMany(outcome => StringComparer.Ordinal.Equals(outcome, MatchPlaceholder) ? SwitchMatches(sourcePlan) : [outcome])
                        .Distinct(StringComparer.Ordinal)
                        .ToArray();
                    continue;
                }

                if (Emits(sourcePlan, edge.Port))
                {
                    edge.Elsa4Ports = [edge.Port];
                    continue;
                }

                EdgeError(GraphOutcomeUnmapped, edge,
                    $"Connection {edge.Index} leaves '{NodeId(edge.Source)}' on '{edge.Port}', which its Elsa 4 activity does not emit (it emits {Describe(EmittedOutcomes(sourcePlan))}).",
                    "Connect from an outcome the activity has. When its outcomes come from an input (status codes, possible outcomes), list them literally.");
            }
        }

        private static bool Emits(Elsa3NodePlan node, string port) =>
            EmittedOutcomes(node) is { } emitted && emitted.Contains(port, StringComparer.Ordinal);

        /// <summary>Every outcome the node can complete with, or null when an input bound by expression decides them.</summary>
        private static IReadOnlyList<string>? EmittedOutcomes(Elsa3NodePlan node)
        {
            var outcomes = node.Resolved!.Outcomes;
            var emitted = outcomes.Ports.Concat(SwitchMatches(node)).ToList();
            if (outcomes.Dynamic is { } dynamic)
            {
                if (DynamicItems(node, dynamic.SourceInputKey) is not { } items)
                    return null;
                emitted.AddRange(items);
                if (dynamic.UnmatchedOutcome is { } unmatched)
                    emitted.Add(unmatched);
            }

            return emitted.Distinct(StringComparer.Ordinal).ToArray();
        }

        private static IEnumerable<string> SwitchMatches(Elsa3NodePlan node) =>
            node.Slots.Where(slot => StringComparer.Ordinal.Equals(slot.SlotName, SwitchCaseSlot) && slot.Label is not null).Select(slot => slot.Label!);

        /// <summary>The literal items of the input that names a node's dynamic outcomes; empty when unset, null when an expression decides them.</summary>
        private static IReadOnlyList<string>? DynamicItems(Elsa3NodePlan node, string inputKey)
        {
            var value = node.Node.AdditionalProperties.FirstOrDefault(item => StringComparer.OrdinalIgnoreCase.Equals(item.Key, inputKey)).Value;
            if (value.ValueKind == JsonValueKind.Undefined || !Elsa3TypedValues.IsBound(value))
                return [];

            string? text = null;
            if (Elsa3TypedValues.TryGetLiteral(value, out var literal))
                text = literal;
            else if (Elsa3TypedValues.TryGetExpression(value, out var type, out var expression) &&
                     string.Equals(type, "Object", StringComparison.OrdinalIgnoreCase))
                text = expression.ValueKind == JsonValueKind.String ? expression.GetString() : expression.GetRawText();
            if (text is null)
                return null;

            try
            {
                using var document = JsonDocument.Parse(text);
                if (document.RootElement.ValueKind != JsonValueKind.Array)
                    return null;
                return document.RootElement.EnumerateArray()
                    .Select(item => item.ValueKind switch
                    {
                        JsonValueKind.String => item.GetString(),
                        JsonValueKind.Number => item.GetRawText(),
                        _ => null
                    })
                    .OfType<string>()
                    .ToArray();
            }
            catch (JsonException)
            {
                return null;
            }
        }

        // ── Stage 4: start node and loops ────────────────────────────────────────────────────────────────────────

        /// <summary>Elsa 3's start order without the run-time trigger: Start, first start-capable child, first child with no inbound connection, first child.</summary>
        private Elsa3Activity? StartCandidate(List<Edge> edges)
        {
            var targets = edges.Select(edge => edge.Target).ToHashSet(ReferenceEqualityComparer.Instance);
            return _children.FirstOrDefault(child => Construct(child) == Elsa3GraphConstructKind.Start)
                   ?? _children.FirstOrDefault(child => child.CustomProperties?.CanStartWorkflow == true)
                   ?? _children.FirstOrDefault(child => !targets.Contains(child))
                   ?? _children.FirstOrDefault();
        }

        /// <summary>
        /// Classifies back edges by a depth-first walk from the entry points (the start candidate, then every other child
        /// with no inbound connection). Elsa 4 accepts a loop only back to the start node when that is its only inbound
        /// connection (R-8); any other loop is reported. Returns the forward edges.
        /// </summary>
        private List<Edge> RemoveLoops(List<Edge> edges, Elsa3Activity? start)
        {
            if (start is null)
                return edges;

            var targets = edges.Select(edge => edge.Target).ToHashSet(ReferenceEqualityComparer.Instance);
            var entries = new[] { start }.Concat(_children.Where(child => !ReferenceEquals(child, start) && !targets.Contains(child)));
            var state = new Dictionary<Elsa3Activity, int>(ReferenceEqualityComparer.Instance);
            var back = new HashSet<Edge>(ReferenceEqualityComparer.Instance);
            foreach (var entry in entries)
                Walk(entry, edges, state, back);

            foreach (var edge in back)
            {
                var inbound = edges.Count(candidate => ReferenceEquals(candidate.Target, edge.Target));
                var supported = ReferenceEquals(edge.Target, start) && Construct(edge.Target) is null && inbound == 1;
                if (!supported)
                    EdgeError(GraphLoopUnsupported, edge,
                        $"Connection {edge.Index} from '{NodeId(edge.Source)}' loops back to '{NodeId(edge.Target)}'. An Elsa 4 flowchart loops only back to its start node, and only when nothing else leads into it.",
                        "Use a While, For or ForEach activity for the loop before converting.");
            }

            return edges.Where(edge => !back.Contains(edge)).ToList();
        }

        private static void Walk(Elsa3Activity entry, List<Edge> edges, Dictionary<Elsa3Activity, int> state, HashSet<Edge> back)
        {
            const int onStack = 1, done = 2;
            if (state.ContainsKey(entry))
                return;

            var stack = new Stack<(Elsa3Activity Node, int Next)>();
            state[entry] = onStack;
            stack.Push((entry, 0));
            while (stack.TryPop(out var frame))
            {
                var outgoing = edges.Where(edge => ReferenceEquals(edge.Source, frame.Node)).ToArray();
                if (frame.Next >= outgoing.Length)
                {
                    state[frame.Node] = done;
                    continue;
                }

                stack.Push((frame.Node, frame.Next + 1));
                var edge = outgoing[frame.Next];
                switch (state.GetValueOrDefault(edge.Target))
                {
                    case onStack:
                        back.Add(edge);
                        break;
                    case 0:
                        state[edge.Target] = onStack;
                        stack.Push((edge.Target, 0));
                        break;
                }
            }
        }

        // ── Stage 5: joins (G-4, G-5, R-6) ───────────────────────────────────────────────────────────────────────

        private void CheckJoins(List<Edge> forward)
        {
            foreach (var join in _children.Where(child => Construct(child) == Elsa3GraphConstructKind.Join))
            {
                var joinPlan = plan.Find(join)!;
                var inbound = forward.Where(edge => ReferenceEquals(edge.Target, join)).ToArray();
                var mode = ReadJoinMode(joinPlan);
                if (mode is null)
                {
                    JoinError(joinPlan, $"Join '{NodeId(join)}' sets its mode by an expression, which only Elsa 3 can evaluate.",
                        "Set the join's mode literally before converting.");
                    continue;
                }

                switch (mode)
                {
                    case JoinMode.WaitAny when ConcurrentPair(forward, join, inbound):
                        JoinError(joinPlan,
                            $"Join '{NodeId(join)}' waits for any branch, but its branches can run at the same time. Elsa 3 continues on the first and cancels the others; Elsa 4 has no join that cancels.",
                            "Use WaitAll or WaitAllActive, or remove the parallel branches, before converting.");
                        break;
                    case JoinMode.WaitAll when !AllBranchesRun(forward, join, inbound):
                        JoinError(joinPlan,
                            $"Join '{NodeId(join)}' waits for all branches, but not every branch provably runs. Elsa 3 skips the join when one does not; Elsa 4 continues with the branches that ran.",
                            "Use WaitAllActive, or make every branch into the join unconditional, before converting.");
                        break;
                }
            }
        }

        private void JoinError(Elsa3NodePlan joinPlan, string message, string guidance) =>
            Error(GraphJoinUnsupported, joinPlan.JsonPath, joinPlan.Node, message, guidance);

        /// <summary>The join's mode (Elsa 3 default WaitAny); null when an expression sets it.</summary>
        private static JoinMode? ReadJoinMode(Elsa3NodePlan join)
        {
            if (join.GraphConstructSetting is not { } setting || !Elsa3TypedValues.IsBound(setting))
                return JoinMode.WaitAny;
            if (!Elsa3TypedValues.TryGetLiteral(setting, out var literal) || literal is null)
                return null;
            if (Enum.TryParse<JoinMode>(literal.Trim(), ignoreCase: true, out var named) && Enum.IsDefined(named) && !int.TryParse(literal, out _))
                return named;
            // Elsa 3 FlowJoinMode: WaitAll = 0, WaitAllActive = 1, WaitAny = 2.
            return int.TryParse(literal.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) && number is >= 0 and <= 2
                ? (JoinMode)number
                : null;
        }

        /// <summary>
        /// Fan-out groups: connections one completion follows together. A fork's branches, or two or more connections
        /// on the same port of one node. Different ports of a node are alternatives, not fan-out.
        /// </summary>
        private IEnumerable<Edge[]> FanOutGroups(List<Edge> forward) =>
            forward
                .GroupBy(edge => (edge.Source, Port: Construct(edge.Source) == Elsa3GraphConstructKind.Fork ? string.Empty : edge.Port))
                .Select(group => group.ToArray())
                .Where(group => group.Length > 1);

        /// <summary>Whether two inbound connections of <paramref name="join"/> can be live at the same time (conservative).</summary>
        private bool ConcurrentPair(List<Edge> forward, Elsa3Activity join, IReadOnlyList<Edge> inbound)
        {
            if (inbound.Count < 2)
                return false;
            foreach (var group in FanOutGroups(forward))
            {
                var reachedBy = group.Select(lane => InboundReachedBy(forward, lane, join, inbound)).ToArray();
                for (var first = 0; first < reachedBy.Length; first++)
                for (var second = first + 1; second < reachedBy.Length; second++)
                {
                    if (reachedBy[first].Any(a => reachedBy[second].Any(b => !ReferenceEquals(a, b))))
                        return true;
                }
            }

            return false;
        }

        /// <summary>The inbound connections of <paramref name="join"/> reachable through <paramref name="lane"/> without passing the join.</summary>
        private static HashSet<Edge> InboundReachedBy(List<Edge> forward, Edge lane, Elsa3Activity join, IReadOnlyList<Edge> inbound)
        {
            var reached = new HashSet<Edge>(ReferenceEqualityComparer.Instance);
            if (ReferenceEquals(lane.Target, join))
            {
                reached.Add(lane);
                return reached;
            }

            var seen = new HashSet<Elsa3Activity>(ReferenceEqualityComparer.Instance) { lane.Target };
            var pending = new Queue<Elsa3Activity>([lane.Target]);
            while (pending.TryDequeue(out var node))
            {
                foreach (var edge in forward.Where(edge => ReferenceEquals(edge.Source, node)))
                {
                    if (ReferenceEquals(edge.Target, join))
                    {
                        if (inbound.Contains(edge))
                            reached.Add(edge);
                        continue;
                    }

                    if (seen.Add(edge.Target))
                        pending.Enqueue(edge.Target);
                }
            }

            return reached;
        }

        /// <summary>
        /// Whether every inbound connection of a WaitAll join provably runs: one fan-out group reaches each inbound
        /// connection through its own lane of unconditional nodes, each with exactly one outgoing connection that covers
        /// every outcome the node can complete with.
        /// </summary>
        private bool AllBranchesRun(List<Edge> forward, Elsa3Activity join, IReadOnlyList<Edge> inbound)
        {
            // One inbound connection: Elsa 3 runs the join exactly when it is followed, as Elsa 4 does.
            if (inbound.Count < 2)
                return true;
            foreach (var group in FanOutGroups(forward))
            {
                var reached = new List<Edge>();
                foreach (var lane in group)
                {
                    if (FollowUnconditional(forward, lane, join) is not { } edge)
                    {
                        reached.Clear();
                        break;
                    }

                    reached.Add(edge);
                }

                if (reached.Count == inbound.Count &&
                    reached.Distinct(ReferenceEqualityComparer.Instance).Count() == inbound.Count &&
                    inbound.All(edge => reached.Contains(edge)))
                    return true;
            }

            return false;
        }

        /// <summary>The inbound connection of <paramref name="join"/> a lane surely reaches, or null when a node on the way may not continue.</summary>
        private Edge? FollowUnconditional(List<Edge> forward, Edge lane, Elsa3Activity join)
        {
            var edge = lane;
            var seen = new HashSet<Elsa3Activity>(ReferenceEqualityComparer.Instance);
            while (!ReferenceEquals(edge.Target, join))
            {
                var node = edge.Target;
                if (!seen.Add(node) || Construct(node) is not null || plan.Find(node) is not { Resolved: not null } nodePlan)
                    return null;
                var outgoing = forward.Where(candidate => ReferenceEquals(candidate.Source, node)).ToArray();
                if (outgoing.Length != 1 || !StringComparer.Ordinal.Equals(outgoing[0].Port, Done))
                    return null;
                if (EmittedOutcomes(nodePlan) is not { } emitted ||
                    !emitted.Where(outcome => !StringComparer.Ordinal.Equals(outcome, Break)).All(outcome => outgoing[0].Elsa4Ports.Contains(outcome, StringComparer.Ordinal)))
                    return null;
                edge = outgoing[0];
            }

            return edge;
        }

        // ── Stage 6: collapse constructs (R-5) ───────────────────────────────────────────────────────────────────

        /// <summary>One Elsa 4 connection: source node, Elsa 4 port, target node, and the Elsa 3 connection it came from.</summary>
        private sealed record Elsa4Edge(Elsa3Activity Source, string Port, Elsa3Activity Target, int Index);

        private List<Elsa4Edge> Collapse(List<Edge> edges, ref Elsa3Activity? start)
        {
            var list = edges
                .SelectMany(edge => edge.Elsa4Ports.Select(port => new Elsa4Edge(edge.Source, port, edge.Target, edge.Index)))
                .ToList();
            foreach (var construct in _children.Where(child => Construct(child) is not null))
            {
                var outgoing = list.Where(edge => ReferenceEquals(edge.Source, construct)).ToArray();
                var next = new List<Elsa4Edge>();
                foreach (var edge in list)
                {
                    if (ReferenceEquals(edge.Source, construct))
                        continue;
                    if (!ReferenceEquals(edge.Target, construct))
                    {
                        next.Add(edge);
                        continue;
                    }

                    next.AddRange(outgoing.Select(outbound => edge with { Target = outbound.Target }));
                }

                list = next;
                if (ReferenceEquals(start, construct))
                {
                    var targets = outgoing.Select(edge => edge.Target).Distinct(ReferenceEqualityComparer.Instance).Cast<Elsa3Activity>().ToArray();
                    if (targets.Length == 1)
                        start = targets[0];
                    else
                    {
                        Error(GraphStartAmbiguous, plan.Find(construct)!.JsonPath, construct,
                            $"'{NodeId(construct)}' ({construct.Type}) starts flowchart '{NodeId(flowchart.Node)}' and leads to {targets.Length} activities; an Elsa 4 flowchart starts at exactly one.",
                            "Put one activity first, and branch after it, before converting.");
                        start = null;
                    }
                }
            }

            return list
                .DistinctBy(edge => (edge.Source, edge.Port, edge.Target))
                .ToList();
        }

        /// <summary>
        /// Spec 238's start-path rule: an Elsa 4 run started by a trigger begins at it only when it is the start node or has
        /// no inbound connection. Elsa 3 started at any triggered direct child, so a start trigger something else leads
        /// into is refused here instead of at publish. Placement through nested containers is left to publish preflight.
        /// </summary>
        private void CheckStartTriggers(List<Elsa4Edge> edges, Elsa3Activity? start)
        {
            foreach (var child in _children.Where(child => child.CustomProperties?.CanStartWorkflow == true && !ReferenceEquals(child, start)))
            {
                if (Construct(child) is not null || !edges.Any(edge => ReferenceEquals(edge.Target, child)))
                    continue;

                Error(GraphStartTriggerMisplaced, plan.Find(child)!.JsonPath, child,
                    $"'{NodeId(child)}' ({child.Type}) can start the workflow, but another activity leads into it and it is not the start node of flowchart '{NodeId(flowchart.Node)}'. Elsa 4 begins a run at a trigger only where nothing leads into it, or at the start node.",
                    "Move the trigger to an entry position with no incoming connection, or make it a mid-flow wait (clear 'Can start workflow'), before converting.");
            }
        }

        // ── Stage 7: write the structure fields, remove the constructs ──────────────────────────────────────────

        private void Write(List<Elsa4Edge> edges, Elsa3Activity? start)
        {
            foreach (var construct in _children.Where(child => Construct(child) is not null).ToArray())
            {
                flowchart.Slots.RemoveAll(slot => ReferenceEquals(slot.Child, construct));
                flowchart.Node.Activities?.Remove(construct);
                _children.Remove(construct);
            }

            flowchart.StructureFields[ConnectionsField] = edges
                .Select(edge => new
                {
                    source = new { nodeId = NodeId(edge.Source), port = edge.Port },
                    target = new { nodeId = NodeId(edge.Target), port = (string?)null }
                })
                .ToArray();
            if (start is not null && _children.Count > 0)
                flowchart.StructureFields[StartNodeIdField] = NodeId(start);
        }

        // ── Helpers ──────────────────────────────────────────────────────────────────────────────────────────────

        private Elsa3GraphConstructKind? Construct(Elsa3Activity node) => plan.Find(node)?.GraphConstruct;

        private string ConnectionPath(int index) => $"{flowchart.JsonPath}.connections[{index}]";

        private string EdgePath(Edge edge) => edge.Index < 0 ? flowchart.JsonPath : ConnectionPath(edge.Index);

        private static string NodeId(Elsa3Activity node) => Elsa3ConversionDiagnostics.NodeId(node);

        private static string Describe(IReadOnlyList<string>? outcomes) =>
            outcomes is null ? "outcomes set by an expression" : outcomes.Count == 0 ? "nothing" : string.Join(", ", outcomes.Select(outcome => $"'{outcome}'"));

        private void Error(string code, string path, Elsa3Activity node, string message, string guidance) =>
            Report(Elsa3MigrationDiagnosticSeverity.Error, code, path, node, message, guidance);

        private void Warning(string code, string path, Elsa3Activity node, string message, string guidance) =>
            Report(Elsa3MigrationDiagnosticSeverity.Warning, code, path, node, message, guidance);

        /// <summary>A diagnostic about one connection: the source node, the connection's path, its target and port (FR-014).</summary>
        private void EdgeError(string code, Edge edge, string message, string guidance) =>
            Report(Elsa3MigrationDiagnosticSeverity.Error, code, EdgePath(edge), edge.Source, message, guidance, ConnectionMetadata(NodeId(edge.Target), edge.Port));

        private void EdgeWarning(string code, Edge edge, string message, string guidance) =>
            Report(Elsa3MigrationDiagnosticSeverity.Warning, code, EdgePath(edge), edge.Source, message, guidance, ConnectionMetadata(NodeId(edge.Target), edge.Port));

        private static Dictionary<string, string> ConnectionMetadata(string? targetNodeId, string port) =>
            new(StringComparer.Ordinal) { ["TargetNodeId"] = targetNodeId ?? string.Empty, ["Port"] = port };

        private void Report(
            Elsa3MigrationDiagnosticSeverity severity,
            string code,
            string path,
            Elsa3Activity node,
            string message,
            string guidance,
            IReadOnlyDictionary<string, string>? extra = null)
        {
            if (severity == Elsa3MigrationDiagnosticSeverity.Error)
                _failed = true;
            var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["ActivityNodeId"] = NodeId(node),
                ["Elsa3Type"] = node.Type ?? string.Empty,
                ["FlowchartNodeId"] = NodeId(flowchart.Node)
            };
            foreach (var (key, value) in extra ?? new Dictionary<string, string>())
                metadata[key] = value;
            plan.Diagnostics.Add(Elsa3ConversionDiagnostics.ForDefinition(plan, severity, code, message, guidance, path, metadata));
        }
    }
}
