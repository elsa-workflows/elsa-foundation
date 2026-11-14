using System.Text.Json.Nodes;
using Kernel.Durable;
using Kernel.Native;

// Literal policy/root/parent fixtures, not an actual historical binary or writer migration.
internal static class RootPolicyReader
{
    private static NativeState Held()
    {
        var state = new NativeState { Started = true, InterventionEnabled = true, InterventionPolicy = 2 };
        state.Frames.Add("f", new() { Id = "f", Node = "leaf", RecoveryInputs = new() { ["Value"] = 7 }, RecoveryMaterial = new() { ["root"] = "leaf" },
            OperationKey = "logical", Intervention = new("incident", new("dependency", "repair needed")) });
        return state;
    }
    private static NativeState RootStaged()
    {
        var state = Held(); var source = state.Frames["f"]; state.Frames.Clear();
        state.Retries = new() { ["operator"] = new(new("operator", "f", "incident", "repaired"), source, "fresh") };
        state.Agenda.Add(new() { Frame = "fresh", Node = "leaf", RetrySource = "f", OperationKey = source.OperationKey,
            RecoveryInputs = (JsonObject)source.RecoveryInputs!.DeepClone(), RecoveryMaterial = (JsonObject)source.RecoveryMaterial!.DeepClone() });
        return state;
    }
    private static NativeState Materialized()
    {
        var state = RootStaged(); var work = state.Agenda.Single(); state.Agenda.Clear();
        state.Frames.Add("fresh", new() { Id = "fresh", Node = "leaf", RetrySource = "f", OperationKey = work.OperationKey,
            RecoveryInputs = work.RecoveryInputs, RecoveryMaterial = work.RecoveryMaterial });
        return state;
    }
    private static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("unused-policy", () => Check(!Json.Write(new NativeState()).Contains("InterventionPolicy", StringComparison.Ordinal), "unused policy changed legacy shape"));
        yield return ("held-codec", () => { var json = Json.Write(Held()); var state = Json.Read<NativeState>(json); NativeInterventions.Validate(state); Check(Json.Write(state) == json, "root codec changed"); });
        yield return ("staged-codec", () => NativeInterventions.Validate(Json.Read<NativeState>(Json.Write(RootStaged()))));
        foreach (var change in new[] { "wrong-root", "tag", "scope", "borrowed", "other-frame", "other-work" }) yield return ("staged-" + change, () =>
        {
            var state = RootStaged(); var source = state.Retries!["operator"].Source; var work = state.Agenda.Single();
            if (change == "wrong-root") { source.RecoveryMaterial!["root"] = "other"; work.RecoveryMaterial!["root"] = "other"; }
            if (change == "tag") source.Tag = work.Tag = "foreign";
            if (change == "scope") { source.Scope = new(); work.Scope = new(); }
            if (change == "borrowed") source.LexicalScope = work.LexicalScope = "foreign";
            if (change == "other-frame") state.Frames.Add("other", new() { Id = "other", Node = "other" });
            if (change == "other-work") state.Agenda.Add(new() { Frame = "other", Node = "other" });
            Refuse(() => NativeInterventions.Validate(state), change is "other-frame" or "other-work" ? "Invalid native staged root obligation." : "Invalid native root retry lineage.");
        });
        yield return ("completed-root-history", () => { var state = RootStaged(); state.Agenda.Clear(); NativeInterventions.Validate(state); });
        yield return ("materialized-root-codec", () => NativeInterventions.Validate(Materialized()));
        foreach (var context in new[] { "staged", "materialized", "child" }) foreach (var kind in new[] { "future", "outcome", "code", "fault" })
            yield return ($"archived-incident,context={context},case={kind}", () =>
            {
                var state = context == "child" ? RecoveryReader.Staged() : context == "materialized" ? Materialized() : RootStaged(); var source = state.Retries!["operator"].Source;
                source.Intervention = kind switch
                {
                    "future" => source.Intervention! with { Version = 2 }, "outcome" => source.Intervention! with { Outcome = "Open" },
                    "code" => source.Intervention! with { Fault = new("", "invalid") }, _ => source.Intervention! with { Fault = null! }
                };
                if (kind != "future") { Refuse(() => NativeInterventions.Validate(state), "Invalid native intervention ownership."); return; }
                try { NativeInterventions.Validate(state); } catch (NotSupportedException e) when (e.Message == "Unsupported native intervention version.") { return; }
                throw new InvalidOperationException("R30: future archived intervention version accepted");
            });
        foreach (var change in new[] { "wrong-root", "other-frame", "other-work" }) yield return ("materialized-" + change, () =>
        {
            var state = Materialized();
            if (change == "wrong-root") { state.Retries!["operator"].Source.RecoveryMaterial!["root"] = "other"; state.Frames["fresh"].RecoveryMaterial!["root"] = "other"; }
            if (change == "other-frame") state.Frames.Add("other", new() { Id = "other", Node = "other" });
            if (change == "other-work") state.Agenda.Add(new() { Frame = "other", Node = "other" });
            Refuse(() => NativeInterventions.Validate(state), change == "wrong-root" ? "Invalid native root retry lineage." : "Invalid native staged root obligation.");
        });
        yield return ("v6-staged-policy", () =>
        {
            var json = Json.Write(RecoveryReader.Staged()); var copy = (NativeState)new NativeModel(new()).UpgradeState(json, 6); NativeInterventions.Validate(copy);
            Check(Json.Write(copy) == json && NativeInterventions.Policy(copy) == 1 && copy.InterventionPolicy == 0, "v6 retry data gained latest capabilities or changed");
        });
        yield return ("mixed-v6-policy", () => Refuse(() => new NativeModel(new()).UpgradeState(Json.Write(Held()), 6), "Legacy native state contains unsupported recovery policy data."));
        foreach (var version in new[] { -1, 5 }) yield return ("unsupported-policy-" + version, () =>
        {
            var state = Held(); state.InterventionPolicy = version;
            try { NativeInterventions.Validate(state); } catch (NotSupportedException e) when (e.Message == "Unsupported native intervention policy.") { return; }
            throw new InvalidOperationException("R30: unsupported policy accepted");
        });
        foreach (var change in new (string Name, Action<NativeState> Apply)[]
        {
            ("old-policy-root", s => s.InterventionPolicy = 0), ("root-tag", s => s.Frames["f"].Tag = "branch"),
            ("root-iteration", s => s.Frames["f"].Scope = new()), ("root-borrowed", s => s.Frames["f"].LexicalScope = "foreign"),
            ("wrong-root-material", s => s.Frames["f"].RecoveryMaterial!["root"] = "other"), ("wrong-root-kind", s => s.Frames["f"].RecoveryMaterial!["root"] = 7),
            ("other-root", s => s.Frames.Add("other", new() { Id = "other", Node = "other" })),
            ("other-work", s => s.Agenda.Add(new() { Frame = "other", Node = "other" }))
        }) yield return (change.Name, () => { var state = Held(); change.Apply(state); Refuse(() => NativeInterventions.Validate(state), "Invalid native intervention ownership."); });
        yield return ("old-policy-staged-root", () => { var state = RootStaged(); state.InterventionPolicy = 0; Refuse(() => NativeInterventions.Validate(state), "Invalid native root retry lineage."); });
        foreach (var kind in new[] { "valid", "index", "item", "missing-index", "missing-item", "tag", "node", "finished", "null-items" }) yield return ("iteration-" + kind, () =>
        {
            var definition = Wf.ForEach("loop", Binding.Literal(new[] { 7, 9 }), Wf.Node<RootLeaf>("leaf"));
            var progress = new ForEachState { Items = new JsonArray(7, 9), Index = kind == "finished" ? 2 : 0 };
            if (kind == "null-items") progress.Items = null!;
            var scope = new Dictionary<string, JsonNode?> { ["CurrentIndex"] = Values.From(kind == "index" ? 1 : 0), ["CurrentItem"] = Values.From(kind == "item" ? 9 : 7) };
            if (kind == "missing-index") scope.Remove("CurrentIndex"); if (kind == "missing-item") scope.Remove("CurrentItem");
            var child = new RetryChild("old", "fresh", kind == "node" ? "foreign" : "leaf", kind == "tag" ? "foreign" : null, scope);
            var input = Values.From(progress); JsonNode? Prepare() => ((IInterventionParent)new ForEach()).PrepareRetry(definition, input, child);
            if (kind == "valid") Check(JsonNode.DeepEquals(Prepare(), input) && progress.Index == 0, "pure iteration preparation changed its obligation");
            else Refuse(() => Prepare(), "Invalid iteration retry obligation.");
        });
    }
    private static void Check(bool ok, string why) { if (!ok) throw new InvalidOperationException("R30: " + why); }
    private static void Refuse(Action action, string expected)
    {
        try { action(); } catch (InvalidOperationException e) when (e.Message == expected) { return; }
        throw new InvalidOperationException("R30: invalid policy/root/iteration data accepted: " + expected);
    }
    internal static IEnumerable<string> Names => Cases().Select(c => "recovery-policy-reader:" + c.Name);
    internal static void Run() { foreach (var c in Cases()) { c.Run(); Console.WriteLine("PASS recovery-policy-reader:" + c.Name); } Console.WriteLine($"{Names.Count()}/{Names.Count()} literal recovery policy/root/iteration readers passed."); }
}
