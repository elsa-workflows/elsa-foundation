namespace Kernel.Native.Run;

/// <summary>The release 1 activity list (K6; GA test drive card 3 B): 26 designer activities run on Native types, 8 are refused.</summary>
internal static class CatalogChecks
{
    internal static void Run(Action<bool, string> check)
    {
        Console.WriteLine("activity catalog");
        var supported = NativeActivityCatalog.Supported;
        check(supported.Count == 26 && NativeActivityCatalog.Unsupported.Count == 8 && supported.Keys.Concat(NativeActivityCatalog.Unsupported).Distinct().Count() == 34,
            $"26 activities run and 8 are left out, 34 in all, none in both ({supported.Count}, {NativeActivityCatalog.Unsupported.Count})");
        check(supported.Values.Distinct().Count() == 26 && supported.Values.All(t => typeof(ActivityBase).IsAssignableFrom(t)) &&
              supported.All(p => p.Key.EndsWith("." + p.Value.Name, StringComparison.Ordinal)),
            "each runs on its own Native activity type, named as the designer's");
        check(NativeActivityCatalog.Resolve("Elsa.Activities.Switch.Activities.Switch", "sw") == typeof(Switch) &&
              NativeActivityCatalog.Resolve("Elsa.HttpEndpoint", "in") == typeof(HttpEndpoint) && NativeActivityCatalog.Resolve("Elsa.Timer", "t") == typeof(Timer) &&
              Refusal("Elsa.CreateUserTask", "task") is { Hidden: true },
            "a supported activity resolves to its Native type, by its catalog key or the declared key a published node carries");
        var hidden = Refusal("Elsa.Activities.Compensation.Activities.CompensationScope", "scope");
        var unknown = Refusal("Elsa.Activities.Missing.Activities.LaterInstalled", "later");
        check(hidden is { Hidden: true, Node: "scope" } && hidden.Message.Contains("'scope'", StringComparison.Ordinal) &&
              hidden.Message.Contains(NativeActivityCatalog.KernelUnsupportedSet, StringComparison.Ordinal) && unknown is { Hidden: false, Node: "later" },
            "a left-out or unknown activity is refused, naming the node, and a left-out one names the set that hides it");
        check(NativeActivityCatalog.Unsupported.All(key => Refusal(key, "n") is { Hidden: true }), "each of the eight is refused");
    }

    private static NativeActivityUnsupportedException? Refusal(string type, string node)
    {
        try { NativeActivityCatalog.Resolve(type, node); return null; }
        catch (NativeActivityUnsupportedException refused) { return refused; }
    }
}
