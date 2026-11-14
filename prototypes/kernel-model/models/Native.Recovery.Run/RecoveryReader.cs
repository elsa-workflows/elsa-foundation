using System.Text.Json.Nodes;
using Kernel.Durable;
using Kernel.Native;

// Literal codec/refusal fixtures, separately counted from actual fault/reload schedules.
internal static class RecoveryReader
{
    private static NativeState Valid()
    {
        var state = new NativeState { Started = true, InterventionEnabled = true };
        state.Frames.Add("p", new Frame { Id = "p", Node = "parent" });
        state.Frames.Add("f", new Frame { Id = "f", Node = "leaf", Parent = "p", RecoveryInputs = new() { ["value"] = 17 },
            OperationKey = "logical", Intervention = new("incident", new("dependency", "unavailable")) });
        return state;
    }
    private static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("current-roundtrip", () => { var json = Json.Write(Valid()); var copy = Json.Read<NativeState>(json); NativeInterventions.Validate(copy); Check(Json.Write(copy) == json, "codec lost intervention data"); });
        foreach (var version in new[] { 1, 2, 3, 4 })
            yield return ("mixed-v" + version, () => Refuse(() => new NativeModel(new()).UpgradeState(Json.Write(Valid()), version), "Legacy native state contains unsupported recovery data."));
        foreach (var change in new (string Name, Action<NativeState> Apply, string Error)[]
        {
            ("no-profile", s => s.InterventionEnabled = false, "Native recovery data requires its opted-in profile."),
            ("unstarted", s => s.Started = false, "Invalid native intervention profile state."),
            ("ephemeral", s => s.Mode = Modes.Ephemeral, "Invalid native intervention profile state."),
            ("no-operation", s => s.Frames["f"].OperationKey = null, "Invalid native pinned-input ownership."),
            ("no-inputs", s => s.Frames["f"].RecoveryInputs = null, "Invalid native pinned-input ownership."),
            ("empty-operation", s => s.Frames["f"].OperationKey = "", "Invalid native pinned-input ownership."),
            ("wrong-outcome", s => s.Frames["f"].Intervention = s.Frames["f"].Intervention! with { Outcome = "Open" }, "Invalid native intervention ownership."),
            ("no-parent", s => s.Frames.Remove("p"), "Invalid native intervention ownership."),
            ("source-wait", s => s.Waits.Add("w", new("f", "event", "event:stale")), "Invalid native intervention ownership."),
            ("source-child", s => s.Frames.Add("c", new() { Id = "c", Parent = "f", Node = "child" }), "Invalid native intervention ownership."),
            ("duplicate-incident", s => s.Frames.Add("g", new() { Id = "g", Parent = "p", Node = "other", RecoveryInputs = new(), OperationKey = "other", Intervention = s.Frames["f"].Intervention }), "Invalid native intervention ownership.")
        }) yield return (change.Name, () => { var state = Valid(); change.Apply(state); Refuse(() => NativeInterventions.Validate(Json.Read<NativeState>(Json.Write(state))), change.Error); });
        yield return ("future-incident", () =>
        {
            var state = Valid(); state.Frames["f"].Intervention = state.Frames["f"].Intervention! with { Version = 2 };
            try { NativeInterventions.Validate(state); } catch (NotSupportedException e) when (e.Message == "Unsupported native intervention version.") { return; }
            throw new InvalidOperationException("R10: future incident version was accepted");
        });
    }
    private static void Check(bool ok, string reason) { if (!ok) throw new InvalidOperationException("R10: " + reason); }
    private static void Refuse(Action action, string expected)
    {
        try { action(); } catch (InvalidOperationException e) when (e.Message == expected) { return; }
        throw new InvalidOperationException("R10: invalid intervention reader data was accepted: " + expected);
    }
    internal static IEnumerable<string> Names => Cases().Select(c => "recovery-reader:" + c.Name);
    internal static void Run() { foreach (var c in Cases()) { c.Run(); Console.WriteLine("PASS recovery-reader:" + c.Name); } Console.WriteLine($"{Names.Count()}/{Names.Count()} literal intervention readers passed; no historical binary qualification."); }
}
