namespace Kernel.Review5;

/// <summary>
/// One rule set. <c>reviewed</c> is the design at 0a1bb5e0c; <c>repaired</c> is the fifth fix round's design brief;
/// <c>negative</c> is the repaired set with the one rule of the case's finding put back as reviewed; the mutations each
/// change one part of the repaired set. Every flag below is one rule, named as the brief names it.
/// </summary>
sealed record Rules(string Name)
{
    /// <summary>R5-B1, reviewed: a kernel cancel of a job ticket by exact key is a counted consume (Worker.cs 1450–1455).</summary>
    public bool CountedCancel { get; init; }
    /// <summary>R5-B1, mutation: presence is decided from the owner's memory of the tickets it settled, lost at takeover.</summary>
    public bool PresenceFromMemory { get; init; }
    /// <summary>R5-B2, repaired: a cascade is paged, at most <see cref="PCascade"/> entries a commit.</summary>
    public bool Paged { get; init; }
    /// <summary>R5-B2, repaired: each page writes the cursor on the row (or the marker). Negative: the cursor lives in the owner's memory.</summary>
    public bool DurableCursor { get; init; } = true;
    /// <summary>R5-B2, repaired: each page sends the next <c>kernel.cascade</c> control. Mutation: the owner continues from memory.</summary>
    public bool NextControl { get; init; } = true;
    /// <summary>R5-B2, mutation: a delete drops a Discharging obligation.</summary>
    public bool DeleteDropsDischarging { get; init; }
    /// <summary>R5-B2: the page size, entries a discharge commit sends (child terminates plus job cancels).</summary>
    public int PCascade { get; init; } = Program.PCascade;
    /// <summary>R5-M5, repaired: the parent binding at a running recipient.</summary>
    public bool BindingRunning { get; init; }
    /// <summary>R5-M5, repaired: the parent binding at an ended recipient that still has a registry or jobs.</summary>
    public bool BindingEnded { get; init; }
    /// <summary>R5-m3, mutation: the cancel's derivation names another instance's origin.</summary>
    public bool ForeignDerivation { get; init; }
    /// <summary>R5-m3, mutation: the cancel's derivation ignores the generation and takes every same-origin ticket of the key.</summary>
    public bool GenerationBlind { get; init; }
    /// <summary>R5-m3, mutation: retirement accepts a job pair the previous row's registry does not hold.</summary>
    public bool AcceptOffRegistry { get; init; }

    public static Rules Reviewed => new("reviewed") { CountedCancel = true };
    public static Rules Repaired => new("repaired") { Paged = true, BindingRunning = true, BindingEnded = true };

    /// <summary>The rule set by name, for a case family: <c>negative</c> removes the family's repaired rule.</summary>
    public static Rules For(string name, string family) => name switch
    {
        "reviewed" => Reviewed,
        "repaired" => Repaired,
        "negative" => family switch
        {
            "rt2" => Repaired with { Name = name, CountedCancel = true },
            "rt3" => Repaired with { Name = name, DurableCursor = false },
            "rt4" => Repaired with { Name = name, BindingRunning = false, BindingEnded = false },
            _ => throw new ArgumentException(family),
        },
        "presence-from-memory" => Repaired with { Name = name, PresenceFromMemory = true },
        "foreign-derivation" => Repaired with { Name = name, ForeignDerivation = true },
        "generation-blind" => Repaired with { Name = name, GenerationBlind = true },
        "accept-off-registry" => Repaired with { Name = name, AcceptOffRegistry = true },
        "page-above-cap" => Repaired with { Name = name, PCascade = Harness.SendCap + 1 },
        "no-next-control" => Repaired with { Name = name, NextControl = false },
        "delete-drops-discharging" => Repaired with { Name = name, DeleteDropsDischarging = true },
        "binding-running-only" => Repaired with { Name = name, BindingEnded = false },
        _ => throw new ArgumentException($"unknown rule set '{name}'"),
    };
}
