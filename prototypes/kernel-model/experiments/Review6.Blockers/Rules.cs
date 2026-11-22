namespace Kernel.Review6;

/// <summary>
/// One rule set. <c>reviewed</c> is the design at d49aad9cd; <c>repaired</c> is the sixth fix round's design brief;
/// <c>negative</c> is the repaired set with the one rule of the case's finding put back as reviewed; the mutations each
/// change one part of the repaired set. Every flag below is one rule, named as the brief names it.
/// </summary>
sealed record Rules(string Name)
{
    // ---- R6-B1 (NT-1) ----
    /// <summary>R6-B1, repaired: the envelope check of every commit of a row counts the model's <c>TerminalGrowth(state)</c>.</summary>
    public bool TerminalGrowth { get; init; }
    /// <summary>R6-B1: Native's bound per open invocation inbox, the measured closing cost (mutation: one byte less).</summary>
    public int GrowthPerInbox { get; init; } = Nt1.MeasuredPerInbox;

    // ---- R6-B2 (PI-1) ----
    /// <summary>R6-B2, repaired: the delivery id includes the publication's incarnation.</summary>
    public bool IncarnationInId { get; init; }
    /// <summary>R6-B2, repaired: the match digest includes the publication's incarnation.</summary>
    public bool IncarnationInMatch { get; init; }
    /// <summary>Existing prototype behaviour: the built key (Store.cs 247–251, 221–222) through the built <c>Events.PublishAsync</c> (287–296).</summary>
    public bool BuiltKey { get; init; }
    /// <summary>R6-B2, mutation: the insert-or-read hands a duplicate a fresh incarnation and runs its match step.</summary>
    public bool FreshIncarnationOnDuplicate { get; init; }

    public static Rules Reviewed => new("reviewed");
    public static Rules Repaired => new("repaired")
    {
        TerminalGrowth = true, IncarnationInId = true, IncarnationInMatch = true,
    };

    /// <summary>The rule set by name, for a case family: <c>negative</c> removes the family's repaired rule.</summary>
    public static Rules For(string name, string family) => name switch
    {
        "reviewed" => Reviewed,
        "repaired" => Repaired,
        "negative" => family switch
        {
            "nt1" => Repaired with { Name = name, TerminalGrowth = false },
            "pi1" => Repaired with { Name = name, IncarnationInId = false, IncarnationInMatch = false },
            _ => throw new ArgumentException(family),
        },
        "growth-undercount" => Repaired with { Name = name, GrowthPerInbox = Nt1.MeasuredPerInbox - 1 },
        "built-key" => Reviewed with { Name = name, BuiltKey = true },
        "match-without-incarnation" => Repaired with { Name = name, IncarnationInMatch = false },
        "fresh-incarnation-on-duplicate" => Repaired with { Name = name, FreshIncarnationOnDuplicate = true },
        _ => throw new ArgumentException($"unknown rule set '{name}'"),
    };
}
