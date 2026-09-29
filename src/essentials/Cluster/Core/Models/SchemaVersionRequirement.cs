namespace Elsa.Cluster.Core.Models;

/// <summary>
/// A dormancy requirement (spec 182, Key Entities): schema family <see cref="Family"/> at <see cref="Version"/> or later,
/// and, when <see cref="RequiresCompleteness"/>, no row of the family below that version remaining (FR-005).
/// </summary>
public sealed record SchemaVersionRequirement
{
    public SchemaVersionRequirement(string family, string version, bool requiresCompleteness = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(family);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        Family = family;
        Version = version;
        RequiresCompleteness = requiresCompleteness;
    }

    public string Family { get; }

    public string Version { get; }

    public bool RequiresCompleteness { get; }

    /// <summary>The requirements <paramref name="featureType"/> declares with <see cref="RequiresSchemaVersionAttribute"/>, in declaration order.</summary>
    /// <remarks>
    /// Read from the attribute's metadata and matched by the attribute's full name, not its runtime type, so a feature
    /// whose package loaded its own copy of this assembly still has its requirements read rather than silently missed.
    /// </remarks>
    public static IReadOnlyList<SchemaVersionRequirement> DeclaredBy(Type featureType)
    {
        ArgumentNullException.ThrowIfNull(featureType);
        var attributeName = typeof(RequiresSchemaVersionAttribute).FullName;
        return featureType
            .GetCustomAttributesData()
            .Where(declared => declared.AttributeType.FullName == attributeName && declared.ConstructorArguments.Count == 2)
            .Select(declared => new SchemaVersionRequirement(
                (string)declared.ConstructorArguments[0].Value!,
                (string)declared.ConstructorArguments[1].Value!,
                declared.NamedArguments.Any(named => named.MemberName == nameof(RequiresSchemaVersionAttribute.RequiresCompleteness) && named.TypedValue.Value is true)))
            .Distinct()
            .ToArray();
    }

    public override string ToString() =>
        RequiresCompleteness ? $"{Family} at {Version}, complete" : $"{Family} at {Version}";
}
