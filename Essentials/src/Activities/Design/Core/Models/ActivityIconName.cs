using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Elsa.Activities.Design.Core.Models;

/// <summary>
/// Validates and projects the optional designer icon carried on an activity descriptor. The CLR
/// scanner writes a valid value onto an <see cref="FacetKind"/> design facet; the authoring catalog
/// reads it back as <c>iconName</c>.
/// </summary>
public static partial class ActivityIconName
{
    public const string FacetKind = "elsa.activity-icon";
    public const string FacetSchemaVersion = "1";
    public const string PayloadProperty = "iconName";
    public const string AssetPrefix = "asset:";
    public const string ExtensionPrefix = "ext:";
    public const int MaxLength = 256;

    /// <summary>
    /// Returns the trimmed icon when <paramref name="value"/> is a lucide name, an <c>asset:</c>
    /// static-web-asset reference, or an <c>ext:</c> client-registry name; otherwise <see langword="false"/>.
    /// </summary>
    public static bool TryNormalize(string? value, [NotNullWhen(true)] out string? normalized)
    {
        normalized = null;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim();
        if (trimmed.Length > MaxLength)
            return false;
        if (trimmed.Contains("://", StringComparison.Ordinal) || trimmed.Contains('\\', StringComparison.Ordinal))
            return false;

        if (IsLucideName(trimmed) || IsAsset(trimmed) || IsExtension(trimmed))
        {
            normalized = trimmed;
            return true;
        }

        return false;
    }

    /// <summary>Builds the design facet for a valid icon, or <see langword="null"/> when the value is unused.</summary>
    public static ActivityDesignFacet? ToFacet(string? icon)
    {
        if (!TryNormalize(icon, out var normalized))
            return null;

        var payload = JsonSerializer.SerializeToElement(
            new Dictionary<string, string>(StringComparer.Ordinal) { [PayloadProperty] = normalized },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return new ActivityDesignFacet(FacetKind, FacetSchemaVersion, payload);
    }

    /// <summary>Reads the first valid icon facet from <paramref name="facets"/>.</summary>
    public static string? ReadFromFacets(IEnumerable<ActivityDesignFacet> facets)
    {
        foreach (var facet in facets)
        {
            if (!string.Equals(facet.Kind, FacetKind, StringComparison.Ordinal))
                continue;
            if (facet.Payload.ValueKind != JsonValueKind.Object)
                continue;
            if (!facet.Payload.TryGetProperty(PayloadProperty, out var property) || property.ValueKind != JsonValueKind.String)
                continue;
            if (TryNormalize(property.GetString(), out var normalized))
                return normalized;
        }

        return null;
    }

    private static bool IsLucideName(string value) =>
        LucidePascalRegex().IsMatch(value) || LucideKebabRegex().IsMatch(value);

    private static bool IsAsset(string value)
    {
        if (!value.StartsWith(AssetPrefix, StringComparison.Ordinal))
            return false;
        var remainder = value[AssetPrefix.Length..];
        var separator = remainder.IndexOf('/');
        if (separator <= 0 || separator == remainder.Length - 1)
            return false;
        var package = remainder[..separator];
        var path = remainder[(separator + 1)..];
        if (!PackageIdRegex().IsMatch(package) || !AssetPathRegex().IsMatch(path))
            return false;
        return !path.Split('/').Any(segment => segment is "." or "..");
    }

    private static bool IsExtension(string value)
    {
        if (!value.StartsWith(ExtensionPrefix, StringComparison.Ordinal))
            return false;
        return ExtensionNameRegex().IsMatch(value[ExtensionPrefix.Length..]);
    }

    [GeneratedRegex("^[A-Z][A-Za-z0-9]*$", RegexOptions.CultureInvariant)]
    private static partial Regex LucidePascalRegex();

    [GeneratedRegex("^[a-z][a-z0-9]*(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex LucideKebabRegex();

    [GeneratedRegex("^[A-Za-z0-9](?:[A-Za-z0-9._-]*[A-Za-z0-9])?$", RegexOptions.CultureInvariant)]
    private static partial Regex PackageIdRegex();

    [GeneratedRegex("^[A-Za-z0-9._-]+(?:/[A-Za-z0-9._-]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex AssetPathRegex();

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex ExtensionNameRegex();
}
