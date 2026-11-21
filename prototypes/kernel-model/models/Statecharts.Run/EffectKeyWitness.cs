using Kernel.Durable;
using Kernel.Native;
using Kernel.Sim;
using Kernel.Statecharts;
using Kernel.Statecharts.Native;
using static NativeInternalWitness;

/// <summary>
/// Round 5 C (the owner's decision of 16 November): a state entered twice gives its outbound activity two effect keys,
/// named by the visit's committed serial, and the same history gives the same keys. Entry of B sends an HTTP request;
/// go, back, go enters B twice. The sim's N7 runs underneath (the harness is the simulation's world): it would fault the
/// run if the two visits shared a key or a resend changed one.
/// </summary>
internal static class EffectKeyWitness
{
    internal static IEnumerable<string> Names => new[] { Modes.Ephemeral, Modes.Balanced, Modes.Durable }.Select(m => "effect-key:statechart-reentry," + m);

    internal static void Run()
    {
        foreach (var mode in new[] { Modes.Ephemeral, Modes.Balanced, Modes.Durable })
        {
            var first = Visits(mode);
            var second = Visits(mode);
            Check(first.Length == 2, $"EK1 {mode}: B was entered twice and its entry sent twice ({first.Length} sendings)");
            Check(first[0].Key != first[1].Key, $"EK2 {mode}: the two visits of B carry two keys ({first[0].Key}, {first[1].Key})");
            Check(first.All(s => s.Path.StartsWith("machine=B-", StringComparison.Ordinal)), $"EK3 {mode}: the iteration path names the state and its visit serial ({string.Join(", ", first.Select(s => s.Path))})");
            Check(first.Select(s => s.Key).SequenceEqual(second.Select(s => s.Key)), $"EK4 {mode}: the same history gives the same keys ({string.Join(", ", first.Select(s => s.Key))} against {string.Join(", ", second.Select(s => s.Key))})");
            Console.WriteLine($"PASS effect-key:statechart-reentry,{mode}: {string.Join(", ", first.Select(s => s.Key))}");
        }
    }

    private static (string Key, string Path)[] Visits(string mode)
    {
        var h = new MachineInboxHarness();
        var definition = new StructuralDefinition(
            [new("root", NodeKind.Root, Initial: "A"), new("A", NodeKind.Atomic, "root"), new("B", NodeKind.Atomic, "root", Entry: "notify"), new("Done", NodeKind.Final, "root")],
            [new("go", 0, "A", "B", VisitMode.Reenter, "go"), new("back", 1, "B", "A", VisitMode.Reenter, "back"), new("finish", 2, "A", "Done", VisitMode.Reenter, "finish")]);
        var machine = Wf.Node<StateMachine>("machine", ("StructuralDefinition", Binding.Literal(definition)))
            .With(Wf.Http("notify", "https://partner.example/entered", Binding.Literal("B")));
        using var registration = Register(Wf.Workflow("machine-inbox", machine));
        h.Start(mode);
        var frame = h.State.Frames.Values.Single(f => f.Node == "machine").Id;
        foreach (var trigger in new[] { "go", "back", "go", "back", "finish" })
        {
            h.Send(CompositeSignal.Kind, new CompositeSignal(frame, trigger), false);
            h.Drain();
        }
        var sendings = h.World.Native.Outbound.Where(o => o.Key.Contains("|notify|", StringComparison.Ordinal)).OrderBy(o => o.Key, StringComparer.Ordinal).ToArray();
        return sendings.Select(o => (o.Value.Key, o.Key.Split('|')[2].Split(':')[0])).ToArray();
    }
}
