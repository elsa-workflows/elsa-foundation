namespace Elsa.Activities.Switch.Models;

/// <summary>How a <c>Switch</c> selects among its cases (spec 1208 FR-031).</summary>
public static class SwitchModes
{
    /// <summary>The structure property that holds the mode; the multi-outcome declaration reads it.</summary>
    public const string ModeKey = "mode";

    /// <summary>The first matching case is selected, or <c>Default</c> when none matches. The default mode.</summary>
    public const string MatchFirst = "MatchFirst";

    /// <summary>
    /// Every matching case is selected, in case order, or <c>Default</c> when none matches. The node then allows several
    /// outcomes per completion and is an inclusive fork. A case that holds a branch activity runs it only when it is the
    /// one selected case; several selected cases route through their outcome connections.
    /// </summary>
    public const string MatchAny = "MatchAny";
}
