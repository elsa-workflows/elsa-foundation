namespace Kernel.Review7;

/// <summary>
/// One rule set. <c>reviewed</c> is the design at cfce7a2c1; <c>repaired</c> is the seventh fix round's design brief;
/// <c>negative</c> is the repaired set with the one rule of the case's finding put back as reviewed; the mutation
/// <c>refusal-without-prefix</c> removes the other half of R7-M1's rule. Every flag below is one rule, named as the
/// brief names it, and every one is proposed and held by the harness.
/// </summary>
sealed record Rules(string Name)
{
    // ---- R7-B1 (NT-2) ----
    /// <summary>
    /// R7-B1, repaired (proposed): every commit of a live row projects the terminal row with every other
    /// variable-length field of the row at its encoded size, <c>CustomHandles</c> included. Reviewed: the sixth round's
    /// formula, which counts the state, <c>TerminalGrowth(state)</c>, the registries and the fixed 16,384 bytes only.
    /// </summary>
    public bool CountHandles { get; init; }

    // ---- R7-M1 (MX-1) ----
    /// <summary>R7-M1, repaired (proposed, owner decision of 18 November): a turn's event id is stored and returned with the reserved first code unit <c>~</c>.</summary>
    public bool Prefix { get; init; }
    /// <summary>R7-M1, repaired (proposed): the outside publish refuses a caller event id whose first code unit is <c>~</c> with <c>InvalidEventId</c>, writes nothing and issues no receipt.</summary>
    public bool Refusal { get; init; }

    public static Rules Reviewed => new("reviewed");
    public static Rules Repaired => new("repaired") { CountHandles = true, Prefix = true, Refusal = true };

    /// <summary>The rule set by name, for a case family: <c>negative</c> removes the family's repaired rule.</summary>
    public static Rules For(string name, string family) => name switch
    {
        "reviewed" => Reviewed,
        "repaired" => Repaired,
        "negative" => family switch
        {
            "nt2" => Repaired with { Name = name, CountHandles = false },
            "mx1" => Repaired with { Name = name, Refusal = false },
            _ => throw new ArgumentException(family),
        },
        "refusal-without-prefix" when family == "mx1" => Repaired with { Name = name, Prefix = false },
        _ => throw new ArgumentException($"unknown rule set '{name}' for '{family}'"),
    };
}
