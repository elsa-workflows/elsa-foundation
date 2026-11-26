using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Elsa.Modularity.Planning.Models;

namespace Elsa.Modularity.Planning.Json;

/// <summary>Reads supplied planning evidence; no file or host access occurs here.</summary>
public static class SelectionEvidenceJsonReader
{
    public static HostInventory ParseInventory(string json)
    {
        using var document = SelectionJsonReader.ParseStrict(json);
        var root = SelectionJsonReader.RequireObject(document.RootElement, "inventory");
        SelectionJsonReader.RequireFields(root, "invalid-field", "schemaVersion", "inventoryId", "targetId", "observedAt", "source", "features");
        CheckSchema(root);
        var features = SelectionJsonReader.RequiredArray(root, "features").EnumerateArray().Select(ReadFeature).ToImmutableArray();
        if (features.GroupBy(row => row.FeatureId, StringComparer.Ordinal).Any(group => group.Count() > 1))
            throw Invalid("Duplicate inventory feature IDs are not allowed.");
        var observedAt = SelectionJsonReader.RequiredString(root, "observedAt");
        if (!Regex.IsMatch(observedAt, @"(?:Z|[+-]\d{2}:\d{2})$", RegexOptions.CultureInvariant) ||
            !DateTimeOffset.TryParse(observedAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp))
            throw Invalid("Inventory observation time needs an ISO 8601 offset.");
        return new HostInventory(
            SafeLabel(root, "inventoryId"),
            SafeLabel(root, "targetId"),
            timestamp,
            SafeLabel(root, "source"),
            features);
    }

    public static PersistenceEvidence ParseResourceHints(string json)
    {
        using var document = SelectionJsonReader.ParseStrict(json);
        var root = SelectionJsonReader.RequireObject(document.RootElement, "resource hints");
        SelectionJsonReader.RequireFields(root, "invalid-field", "schemaVersion", "source", "resourceReferences");
        CheckSchema(root);
        _ = SafeLabel(root, "source");
        var references = ReadUniqueLabels(SelectionJsonReader.RequiredArray(root, "resourceReferences"), "resource reference");
        return new PersistenceEvidence("unchecked", "supplied-file", references.OrderBy(value => value, StringComparer.Ordinal).ToImmutableArray(), ["external-evidence-unverified"]);
    }

    private static InventoryFeature ReadFeature(JsonElement value)
    {
        var row = SelectionJsonReader.RequireObject(value, "inventory feature");
        SelectionJsonReader.RequireFields(row, "invalid-field", "featureId", "availability", "runtimeDependencies", "manifestDependencies", "manifestReadStatus", "package", "hostBundled", "compatibility", "evidenceSource");
        var availability = Enum(row, "availability", "loaded", "installed", "absent", "unknown");
        var readStatus = Enum(row, "manifestReadStatus", "read", "unreadable", "absent", "unknown");
        var compatibility = Enum(row, "compatibility", "compatible", "incompatible", "unknown");
        var runtimeValue = SelectionJsonReader.RequiredProperty(row, "runtimeDependencies");
        ImmutableArray<string>? runtime = runtimeValue.ValueKind == JsonValueKind.Null ? null : ReadUniqueLabels(Array(runtimeValue, "runtimeDependencies"), "runtime dependency");
        var manifestValue = SelectionJsonReader.RequiredProperty(row, "manifestDependencies");
        ImmutableArray<InventoryDependency>? manifest = null;
        if (manifestValue.ValueKind != JsonValueKind.Null)
        {
            manifest = Array(manifestValue, "manifestDependencies").EnumerateArray().Select(ReadManifestDependency).ToImmutableArray();
            if (manifest.Value.GroupBy(edge => edge.Id, StringComparer.Ordinal).Any(group => group.Count() > 1))
                throw Invalid("Duplicate manifest dependency IDs are not allowed.");
        }
        var packageValue = SelectionJsonReader.RequiredProperty(row, "package");
        InventoryPackage? package = null;
        if (packageValue.ValueKind != JsonValueKind.Null)
        {
            var packageObject = SelectionJsonReader.RequireObject(packageValue, "package");
            SelectionJsonReader.RequireFields(packageObject, "invalid-field", "packageId", "packageVersion", "manifestDigest");
            var digest = SelectionJsonReader.RequiredString(packageObject, "manifestDigest");
            if (!SelectionValueRules.IsDigest(digest))
                throw Invalid("A package needs a SHA-256 manifest digest.");
            package = new InventoryPackage(SafeLabel(packageObject, "packageId"), SafeLabel(packageObject, "packageVersion"), digest);
        }
        var hostBundledValue = SelectionJsonReader.RequiredProperty(row, "hostBundled");
        if (hostBundledValue.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw Invalid("hostBundled must be a boolean.");
        var hostBundled = hostBundledValue.GetBoolean();
        if (hostBundled && package is not null)
            throw Invalid("A host-bundled feature cannot have a package lock.");
        if (runtime is not null && availability != "loaded")
            throw Invalid("Runtime dependencies require a loaded feature observation.");
        return new InventoryFeature(SafeLabel(row, "featureId"), availability, runtime, manifest, readStatus, package, hostBundled, compatibility, SafeLabel(row, "evidenceSource"));
    }

    private static InventoryDependency ReadManifestDependency(JsonElement value)
    {
        var row = SelectionJsonReader.RequireObject(value, "manifest dependency");
        SelectionJsonReader.RequireFields(row, "invalid-field", "id", "optional");
        var optional = SelectionJsonReader.RequiredProperty(row, "optional");
        if (optional.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw Invalid("Manifest dependency optional must be a boolean.");
        return new InventoryDependency(SafeLabel(row, "id"), optional.GetBoolean());
    }

    private static ImmutableArray<string> ReadUniqueLabels(JsonElement array, string name)
    {
        var values = array.EnumerateArray().Select(value =>
        {
            var label = SelectionJsonReader.RequiredNonemptyString(value);
            if (!SelectionValueRules.IsSafeReference(label))
                throw Invalid($"A {name} must be a safe label.");
            return label;
        }).ToImmutableArray();
        if (values.Distinct(StringComparer.Ordinal).Count() != values.Length)
            throw Invalid($"Duplicate {name} IDs are not allowed.");
        return values;
    }

    private static JsonElement Array(JsonElement value, string name) => value.ValueKind == JsonValueKind.Array ? value : throw Invalid($"{name} must be an array or null.");

    private static string SafeLabel(JsonElement element, string name)
    {
        var label = SelectionJsonReader.RequiredString(element, name);
        return SelectionValueRules.IsSafeReference(label) ? label : throw Invalid($"{name} must be a safe label.");
    }

    private static string Enum(JsonElement element, string name, params string[] allowed)
    {
        var value = SelectionJsonReader.RequiredString(element, name);
        return allowed.Contains(value, StringComparer.Ordinal) ? value : throw Invalid($"{name} is unsupported.");
    }

    private static void CheckSchema(JsonElement root)
    {
        if (SelectionJsonReader.RequiredString(root, "schemaVersion") != "1")
            throw new SelectionDocumentException("schema-unsupported", "Evidence schema version is unsupported.");
    }

    private static SelectionDocumentException Invalid(string message) => new("invalid-field", message);
}
