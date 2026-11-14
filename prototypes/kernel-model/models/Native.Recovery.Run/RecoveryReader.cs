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
        yield return ("v5-intervention", () => { var json = Json.Write(Valid()); Check(Json.Write(new NativeModel(new()).UpgradeState(json, 5)) == json, "v5 intervention was changed or blessed with current material"); });
        yield return ("v5-null", () => Check(new NativeModel(new()).UpgradeState("null", 5) is null, "v5 null checkpoint changed"));
        yield return ("mixed-successor-v5", () => Refuse(() => new NativeModel(new()).UpgradeState(Json.Write(Staged()), 5), "Legacy native state contains unsupported successor data."));
        yield return ("staged-codec", () => { var json = Json.Write(Staged()); var state = Json.Read<NativeState>(json); NativeInterventions.Validate(state); Check(Json.Write(state) == json, "staged source/successor codec changed"); });
        foreach (var entry in new (string Name, Action<NativeState> Apply, string Error)[]
        {
            ("missing-lineage", s => s.Retries = null, "Missing native retry lineage."),
            ("reused-physical-id", s => s.Retries!["operator"] = s.Retries["operator"] with { Successor = "f" }, "Invalid native retry lineage."),
            ("changed-operation", s => s.Agenda[0].OperationKey = "different", "Invalid native successor work provenance."),
            ("changed-input", s => s.Agenda[0].RecoveryInputs!["value"] = 99, "Invalid native successor work provenance."),
            ("changed-material", s => s.Agenda[0].RecoveryMaterial!["different"] = true, "Invalid native successor work provenance."),
            ("copied-attempt", s => s.Agenda[0].Attempt = 2, "Invalid native successor work provenance.")
        }) yield return (entry.Name, () => { var state = Staged(); entry.Apply(state); Refuse(() => NativeInterventions.Validate(state), entry.Error); });
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
    private static NativeState Staged()
    {
        var state = Valid(); var source = state.Frames["f"]; source.RecoveryMaterial = new(); state.Frames.Remove("f");
        state.Retries = new() { ["operator"] = new(new("operator", "f", "incident", "repaired"), source, "fresh") };
        state.Agenda.Add(new() { Frame = "fresh", Node = "leaf", Parent = "p", RetrySource = "f", OperationKey = source.OperationKey,
            RecoveryInputs = (JsonObject)source.RecoveryInputs!.DeepClone(), RecoveryMaterial = new() });
        return state;
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
