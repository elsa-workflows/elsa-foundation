using System.Reflection;

namespace Elsa.Persistence.EntityFramework;

/// <summary>
/// What <see cref="EfSchemaFamilyCatalog.Discover"/> read off one <see cref="EfSchemaFamilyAttribute"/> declaration: the
/// family, the canonical name of its owning EF module as that module's <see cref="EfModuleAttribute"/> spells it, the
/// version this build writes, its upcaster chain, and the assembly that declared it. <see cref="Module"/> is
/// <see langword="null"/> for a family shared by no single EF module: one whose declaration named none, because shared
/// mapping code owns it rather than one module's own assembly.
/// </summary>
public sealed record EfSchemaFamilyDescriptor(string Name, string? Module, string CurrentVersion, Assembly Assembly)
{
    /// <summary>The declared upcasters in the order <see cref="EfSchemaFamilyAttribute.Upcasters"/> lists them.</summary>
    public IReadOnlyList<EfSchemaUpcasterDescriptor> Upcasters { get; init; } = [];

    /// <summary>
    /// The entity types <see cref="EfSchemaFamilyAttribute.Entities"/> names: empty for a family that is its module's
    /// only one, whose module's stamped tables all belong to it.
    /// </summary>
    public IReadOnlyList<Type> Entities { get; init; } = [];

    /// <summary>
    /// The versions this declaration can read, as opaque labels in chain order ending at <see cref="CurrentVersion"/>
    /// (spec 180, FR-004): the current version and every predecessor the chain reaches from it without a gap. A version
    /// below a gap, or reached only through an upcaster that is malformed, is never in it. A store's read accepts
    /// exactly these versions (<see cref="EfSchemaChain"/>), and a host's readability report credits exactly these.
    /// </summary>
    public IReadOnlyList<string> ReadableVersions => EfSchemaChainRules.ReadableVersions(CurrentVersion, Upcasters);

    /// <summary>
    /// What is wrong with the declared chain, one line per fault (spec 180, FR-005), or nothing when it is sound. A
    /// family with any fault fails the build and its registration at startup; <see cref="ReadableVersions"/> still
    /// never credits a version the fault leaves unreachable.
    /// </summary>
    public IReadOnlyList<string> Defects => EfSchemaChainRules.Defects(CurrentVersion, Upcasters);
}

/// <summary>
/// One entry of a family's declared chain, read as metadata: the upcaster type, the versions its
/// <see cref="EfSchemaUpcasterAttribute"/> names (<see langword="null"/> when it names none), and why its type cannot
/// serve as an upcaster at all, or <see langword="null"/> when it can.
/// </summary>
public sealed record EfSchemaUpcasterDescriptor(Type Type, string? From, string? To, string? Refusal = null)
{
    /// <summary>True when this entry can take part in a chain: well-formed type, two non-blank, distinct versions.</summary>
    public bool IsWellFormed =>
        Refusal is null && !string.IsNullOrWhiteSpace(From) && !string.IsNullOrWhiteSpace(To) && !StringComparer.Ordinal.Equals(From, To);
}

/// <summary>
/// The chain rules of spec 180's FR-004 and FR-005 over a declared chain, in one place, so the readable set a host
/// reports and the one its reads accept are computed by the same function.
/// </summary>
internal static class EfSchemaChainRules
{
    /// <summary>
    /// Walks back from the chain's end while each upcaster is well-formed, produces the version already reached, and
    /// starts at a version not yet reached. A chain that does not end at <paramref name="currentVersion"/> reaches no
    /// predecessor at all.
    /// </summary>
    public static IReadOnlyList<string> ReadableVersions(string currentVersion, IReadOnlyList<EfSchemaUpcasterDescriptor> upcasters)
    {
        var readable = new List<string> { currentVersion };
        for (var index = upcasters.Count - 1; index >= 0; index--)
        {
            var upcaster = upcasters[index];
            if (!upcaster.IsWellFormed ||
                !StringComparer.Ordinal.Equals(upcaster.To, readable[0]) ||
                readable.Contains(upcaster.From!, StringComparer.Ordinal))
                break;
            readable.Insert(0, upcaster.From!);
        }

        return readable;
    }

    public static IReadOnlyList<string> Defects(string currentVersion, IReadOnlyList<EfSchemaUpcasterDescriptor> upcasters)
    {
        var defects = new List<string>();
        foreach (var upcaster in upcasters)
        {
            var name = upcaster.Type.FullName ?? upcaster.Type.Name;
            if (upcaster.Refusal is not null)
                defects.Add($"'{name}' cannot serve as an upcaster: {upcaster.Refusal}");
            if (string.IsNullOrWhiteSpace(upcaster.From) || string.IsNullOrWhiteSpace(upcaster.To))
                defects.Add($"'{name}' names no source and target version; it needs [EfSchemaUpcaster(from, to)].");
            else if (StringComparer.Ordinal.Equals(upcaster.From, upcaster.To))
                defects.Add($"'{name}' upcasts '{upcaster.From}' to itself.");
        }

        for (var index = 1; index < upcasters.Count; index++)
        {
            var previous = upcasters[index - 1];
            var next = upcasters[index];
            if (previous.To is not null && next.From is not null && !StringComparer.Ordinal.Equals(previous.To, next.From))
                defects.Add($"The chain has a gap: '{previous.Type.Name}' produces '{previous.To}' but the next upcaster, '{next.Type.Name}', reads '{next.From}'.");
        }

        if (upcasters.Count > 0 && upcasters[^1].To is { } last && !StringComparer.Ordinal.Equals(last, currentVersion))
            defects.Add($"The chain ends at '{last}', not at the current version '{currentVersion}'.");

        var repeated = upcasters
            .Select(upcaster => upcaster.From)
            .Append(currentVersion)
            .OfType<string>()
            .GroupBy(version => version, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key);
        defects.AddRange(repeated.Select(version => $"Version '{version}' appears more than once in the chain, which makes it a branch or a cycle."));

        return defects;
    }
}
