namespace Elsa3.Mapping.ActivityMap;

/// <summary>The conversion of one Elsa 3 activity type: its target, every member's rule and its outcome renames.</summary>
/// <param name="Elsa3TypeName">The Elsa 3 type name, for example <c>Elsa.WriteLine</c>; the ordinal key.</param>
/// <param name="Target">What it becomes.</param>
/// <param name="Members">Every Elsa 3 input, output, port and serialized property, each classified once.</param>
/// <param name="Outcomes">Elsa 3 outcome renames, applied by graph conversion (#2705).</param>
/// <param name="Rewrite">The named strategy for logic that is not data, if any.</param>
/// <param name="BehaviourWarnings">The <c>ELS3-BEHAVIOUR-*</c> codes emitted for every node of this type.</param>
public sealed record Elsa3ActivityMapEntry(
    string Elsa3TypeName,
    Elsa3ActivityTarget Target,
    IReadOnlyList<Elsa3MemberRule> Members,
    IReadOnlyList<Elsa3OutcomeRule> Outcomes,
    Elsa3NodeRewriteKind? Rewrite = null,
    IReadOnlyList<string>? BehaviourWarnings = null)
{
    /// <summary>The <c>ELS3-BEHAVIOUR-*</c> codes emitted for every node of this type.</summary>
    public IReadOnlyList<string> BehaviourWarnings { get; init; } = BehaviourWarnings ?? [];

    /// <summary>The rule for the Elsa 3 member <paramref name="elsa3Name"/>, matched case-insensitively, or null.</summary>
    public Elsa3MemberRule? FindMember(string elsa3Name) =>
        Members.FirstOrDefault(member => StringComparer.OrdinalIgnoreCase.Equals(member.Elsa3Name, elsa3Name));
}
