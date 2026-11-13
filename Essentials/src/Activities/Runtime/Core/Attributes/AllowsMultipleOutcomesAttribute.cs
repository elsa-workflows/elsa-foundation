namespace Elsa.Activities.Runtime.Core.Attributes;

/// <summary>
/// Declares that one completion of the activity may select more than one of its outcomes (ADR 0045 §6, spec 1208
/// FR-014). Without arguments the declaration is static: every node of the activity allows several. With
/// <see cref="InputKey"/> and <see cref="EqualsValue"/> it depends on the node's configuration: the node allows several
/// only when the input's authored literal is that value (a Switch in <c>MatchAny</c> mode). The publish compiler pins the
/// resolved flag with the node's outcome set, and Studio reads the same declaration through the <c>elsa.outcomes</c>
/// facet. A node that allows several outcomes is an inclusive fork for joins and analysis.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class AllowsMultipleOutcomesAttribute : Attribute
{
    /// <summary>Every node of the activity allows several outcomes per completion.</summary>
    public AllowsMultipleOutcomesAttribute()
    {
    }

    /// <summary>A node allows several outcomes per completion when input <paramref name="inputKey"/> is <paramref name="equalsValue"/>.</summary>
    public AllowsMultipleOutcomesAttribute(string inputKey, string equalsValue)
    {
        InputKey = !string.IsNullOrWhiteSpace(inputKey) ? inputKey : throw new ArgumentException("An input key is required.", nameof(inputKey));
        EqualsValue = !string.IsNullOrWhiteSpace(equalsValue) ? equalsValue : throw new ArgumentException("A value is required.", nameof(equalsValue));
    }

    /// <summary>The contract key of the input the flag depends on; <see langword="null"/> for a static declaration.</summary>
    public string? InputKey { get; }

    /// <summary>The authored literal value of <see cref="InputKey"/> that allows several outcomes.</summary>
    public string? EqualsValue { get; }
}
