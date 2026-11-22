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
    /// <summary>R5-M5, repaired: a marked (kernel-composed) terminate is parent-bound at a running recipient.</summary>
    public bool BindingRunning { get; init; }
    /// <summary>R5-M5, repaired: a marked terminate is parent-bound at an ended recipient that still has a registry or jobs.</summary>
    public bool BindingEnded { get; init; }
    /// <summary>R5-M5, mutation: every issued terminate is parent-bound, a model's application terminate included.</summary>
    public bool BindUnmarked { get; init; }
    /// <summary>R5-M5, repaired: the commit refuses a cascade origin mark on anything but a kernel-composed terminate from the committing row.</summary>
    public bool CommitChecksMark { get; init; }
    /// <summary>R5-B2, repaired: Validate is widened for exactly one shape, the retirement in the commit that terminates the row (ThenDelete).</summary>
    public bool WidenedValidate { get; init; }
    /// <summary>R5-B2, repaired: key admission reads the bound actor's cascade state and refuses a reuse retryably while it discharges.</summary>
    public bool AdmissionReadsCascade { get; init; }
    /// <summary>R5-m3, mutation: the cancel's derivation names another instance's origin.</summary>
    public bool ForeignDerivation { get; init; }
    /// <summary>R5-m3, mutation: the cancel's derivation ignores the generation and takes every same-origin ticket of the key.</summary>
    public bool GenerationBlind { get; init; }
    /// <summary>R5-m3, mutation: retirement accepts a job pair the previous row's registry does not hold.</summary>
    public bool AcceptOffRegistry { get; init; }
    /// <summary>R5-m3: retirement keeps only the reviewed text's two checks (data-lifecycle.md 102 at 0a1bb5e0c), so a repeated pair can stand for a missing one. Reviewed, and the duplicate-pair negative.</summary>
    public bool AcceptDuplicatePair { get; init; }
    /// <summary>R5-m3, mutation: this harness's earlier proposed derivation, the generation inside the length-prefixed key (the reviewed docs state none).</summary>
    public bool GenerationInsideKey { get; init; }

    public static Rules Reviewed => new("reviewed") { CountedCancel = true, AcceptDuplicatePair = true };
    /// <summary>R5-B2: ThenDelete at an ended row with a registry or jobs starts the terminate's cascade (repaired); otherwise it is the plain qualified delete.</summary>
    public bool EndedThenDeleteCascades { get; init; }
    public static Rules Repaired => new("repaired") { Paged = true, BindingRunning = true, BindingEnded = true, CommitChecksMark = true, WidenedValidate = true, AdmissionReadsCascade = true, EndedThenDeleteCascades = true };

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
            "thendelete" => Repaired with { Name = name, WidenedValidate = false },
            "keyreuse" => Repaired with { Name = name, AdmissionReadsCascade = false },
            "duplicate-pair" => Repaired with { Name = name, AcceptDuplicatePair = true },
            "thendelete-ended" => Repaired with { Name = name, EndedThenDeleteCascades = false },
            _ => throw new ArgumentException(family),
        },
        "presence-from-memory" => Repaired with { Name = name, PresenceFromMemory = true },
        "foreign-derivation" => Repaired with { Name = name, ForeignDerivation = true },
        "generation-blind" => Repaired with { Name = name, GenerationBlind = true },
        "accept-off-registry" => Repaired with { Name = name, AcceptOffRegistry = true },
        "generation-inside-key" => Repaired with { Name = name, GenerationInsideKey = true },
        "page-above-cap" => Repaired with { Name = name, PCascade = Harness.SendCap + 1 },
        "no-next-control" => Repaired with { Name = name, NextControl = false },
        "delete-drops-discharging" => Repaired with { Name = name, DeleteDropsDischarging = true },
        "binding-running-only" => Repaired with { Name = name, BindingEnded = false },
        "bind-every-issued-terminate" => Repaired with { Name = name, BindUnmarked = true },
        "commit-accepts-mark" => Repaired with { Name = name, CommitChecksMark = false },
        _ => throw new ArgumentException($"unknown rule set '{name}'"),
    };
}
