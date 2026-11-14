using System.Text.Json.Nodes;
using Kernel.Durable;
using Kernel.Native;

// Literal reader/codec fixtures, separate from the actual workflow writes/reloads. No historical binary claim.
internal static class ReaderWitness
{
    private static NativeState Valid()
    {
        var state = new NativeState { Started = true, Next = 10 };
        state.Frames.Add("f1", new Frame { Id = "f1", Node = "owner" });
        state.Frames.Add("f2", new Frame { Id = "f2", Node = "borrower", Parent = "f1", LexicalScope = "s3" });
        state.LexicalScopes = new(StringComparer.Ordinal)
        {
            ["s3"] = new("f1", null, new() { ["total"] = Values.From(10) }),
            ["s4"] = new("f2", "s3", new() { ["counter"] = Values.From(1) })
        };
        state.Agenda.Add(new Work { Frame = "f5", Node = "work", Parent = "f2", LexicalScope = "s4" });
        return state;
    }
    private static IEnumerable<(string Name, Action Run)> Cases()
    {
        var model = new NativeModel(new WorkflowRegistry());
        yield return ("legacy-shape", () =>
        {
            var old = new NativeState(); old.Frames.Add("f1", new Frame { Id = "f1", Node = "owner" }); old.Agenda.Add(new Work { Frame = "f2", Node = "work", Parent = "f1" });
            Check(!Json.Write(old).Contains("\"LexicalScope", StringComparison.Ordinal), "unused fields changed legacy native serialization");
        });
        foreach (var version in new[] { 1, 2, 3 })
        {
            yield return ("legacy-v" + version, () =>
            {
                var legacy = new NativeState { Started = true, Next = 2 }; legacy.Variables.Add("kept", Values.From(7));
                var json = Json.Write(legacy); var upgraded = (NativeState)model.UpgradeState(json, version);
                Check(!LexicalScopes.Present(upgraded) && Json.Write(upgraded) == json, "old native reader invented resources or changed data");
            });
            yield return ("null-v" + version, () => Check(model.UpgradeState("null", version) is null, "JSON-null checkpoint behavior changed"));
        }
        yield return ("current-roundtrip", () =>
        {
            var data = Valid(); var json = Json.Write(data); var copy = Json.Read<NativeState>(json); LexicalScopes.Validate(copy);
            Check(Json.Write(copy) == json && copy.LexicalScopes!["s4"].Values["counter"]!.ToString() == "1" && copy.Frames["f2"].LexicalScope == "s3" && copy.Agenda[0].LexicalScope == "s4",
                "private resource/owner/parent/reference codec lost data");
        });
        yield return ("mixed-old", () => Refuse(() => model.UpgradeState(Json.Write(Valid()), 3), "Legacy native state contains unsupported lexical scope data."));
        yield return ("future-version", () =>
        {
            try { model.UpgradeState("{}", 5); }
            catch (NotSupportedException) { return; }
            throw new InvalidOperationException("L10: future native version was accepted");
        });
        var bad = new (string Name, Action<NativeState> Change, string Expected)[]
        {
            ("missing-owner", s => s.LexicalScopes!["s4"] = s.LexicalScopes["s4"] with { Owner = "missing" }, "Lexical scope owner or values are missing."),
            ("missing-parent", s => s.LexicalScopes!["s4"] = s.LexicalScopes["s4"] with { Parent = "missing" }, "Lexical scope is missing or inaccessible."),
            ("foreign-parent", s => { Foreign(s); s.LexicalScopes!["s4"] = s.LexicalScopes["s4"] with { Parent = "s6" }; }, "Lexical scope is missing or inaccessible."),
            ("resource-cycle", s => { s.LexicalScopes!["s3"] = s.LexicalScopes["s3"] with { Parent = "s4" }; s.LexicalScopes["s4"] = s.LexicalScopes["s4"] with { Owner = "f1" }; }, "Lexical scope ancestry is cyclic."),
            ("frame-cycle", s => s.Frames["f2"].Parent = "f2", "Lexical scope frame ancestry is cyclic."),
            ("work-scope", s => s.Agenda[0].LexicalScope = "missing", "Lexical scope is missing or inaccessible."),
            ("work-parent", s => s.Agenda[0].Parent = "missing", "Lexical scope is missing or inaccessible."),
            ("resume-work", s => s.Agenda[0].Resume = true, "Invalid lexical scope work reference."),
            ("root-work", s => s.Agenda[0].Parent = null, "Invalid lexical scope work reference."),
            ("frame-scope", s => s.Frames["f2"].LexicalScope = "missing", "Lexical scope is missing or inaccessible."),
            ("foreign-frame", s => { Foreign(s); s.Frames["f2"].LexicalScope = "s6"; }, "Lexical scope is missing or inaccessible."),
            ("null-resource", s => s.LexicalScopes!["s4"] = null!, "Lexical scope owner or values are missing."),
            ("null-values", s => s.LexicalScopes!["s4"] = s.LexicalScopes["s4"] with { Values = null! }, "Lexical scope owner or values are missing."),
            ("null-owner", s => s.LexicalScopes!["s4"] = s.LexicalScopes["s4"] with { Owner = null! }, "Lexical scope owner or values are missing."),
            ("empty-identity", s => s.LexicalScopes!.Add("", new("f1", null, [])), "Lexical scope owner or values are missing.")
        };
        foreach (var entry in bad) yield return (entry.Name, () => { var data = Valid(); entry.Change(data); Refuse(() => LexicalScopes.Validate(Json.Read<NativeState>(Json.Write(data))), entry.Expected); });
    }
    private static void Foreign(NativeState state)
    {
        state.Frames.Add("f6", new Frame { Id = "f6", Node = "sibling", Parent = "f1" });
        state.LexicalScopes!.Add("s6", new("f6", null, []));
    }
    private static void Check(bool ok, string reason) { if (!ok) throw new InvalidOperationException("L10: " + reason); }
    private static void Refuse(Action action, string expected)
    {
        try { action(); }
        catch (InvalidOperationException e) when (e.Message == expected) { return; }
        throw new InvalidOperationException("L10: invalid lexical reader data was accepted: " + expected);
    }
    internal static IEnumerable<string> Names => Cases().Select(c => "lexical-reader:" + c.Name);
    internal static void Run()
    {
        foreach (var c in Cases()) { c.Run(); Console.WriteLine("PASS lexical-reader:" + c.Name); }
        Console.WriteLine($"{Names.Count()}/{Names.Count()} literal lexical readers passed; historical writer/binary migration is unqualified.");
    }
}
