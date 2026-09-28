using System.Reflection;

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
    public static IReadOnlyList<SchemaVersionRequirement> DeclaredBy(Type featureType)
    {
        ArgumentNullException.ThrowIfNull(featureType);
        return featureType
            .GetCustomAttributes<RequiresSchemaVersionAttribute>(inherit: false)
            .Select(declared => new SchemaVersionRequirement(declared.Family, declared.Version, declared.RequiresCompleteness))
            .Distinct()
            .ToArray();
    }

    public override string ToString() =>
        RequiresCompleteness ? $"{Family} at {Version}, complete" : $"{Family} at {Version}";
}
