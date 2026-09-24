using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using Elsa.Modularity.Planning.Models;

namespace Elsa.Modularity.Planning.Json;

public sealed class SelectionDocumentException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>Strictly reads v1 selection documents without rewriting their opaque authored content.</summary>
public static class SelectionJsonReader
{
    private static readonly StringComparer s_comparer = StringComparer.Ordinal;

    public static SelectionCatalog ParseCatalog(string json)
    {
        using var document = ParseStrict(json);
        var root = RequireObject(document.RootElement, "catalog");
        RequireFields(root, "unknown-catalog-field", "schemaVersion", "id", "version", "publisher", "digest", "profiles", "groups");
        var schemaVersion = RequiredString(root, "schemaVersion");
        CheckSchema(schemaVersion);
        var profiles = ReadDefinitions(RequiredArray(root, "profiles"), "profile");
        var groups = ReadDefinitions(RequiredArray(root, "groups"), "group");
        var all = profiles.Concat(groups).ToArray();
        if (all.GroupBy(x => x.Id, s_comparer).Any(group => group.Select(x => x.Kind).Distinct(s_comparer).Count() > 1))
            throw new SelectionDocumentException("duplicate-definition", "A definition ID cannot name both a profile and a group.");
        if (all.GroupBy(x => (x.Kind, x.Id, x.Version)).Any(group => group.Count() > 1))
            throw new SelectionDocumentException("duplicate-definition", "A definition kind, ID, and version must be unique.");

        var catalog = new SelectionCatalog(
            schemaVersion,
            RequiredString(root, "id"),
            RequiredString(root, "version"),
            RequiredString(root, "publisher"),
            RequiredDigest(root, "digest"),
            profiles,
            groups);
        CheckDigest(catalog.Digest, SelectionDigest.ComputeCatalogDigest(catalog), "catalog");
        return catalog;
    }

    public static WorkspaceProfile ParseWorkspaceProfile(string json)
    {
        using var document = ParseStrict(json);
        var root = RequireObject(document.RootElement, "workspace profile");
        RequireFields(root, "invalid-field", "schemaVersion", "kind", "id", "version", "digest", "members", "rationale", "title", "description", "dependencyExplanations");
        var schemaVersion = RequiredString(root, "schemaVersion");
        CheckSchema(schemaVersion);
        return new WorkspaceProfile(schemaVersion, ReadDefinition(root, "profile", hasSchemaVersion: true));
    }

    public static AuthoredComposition ParseComposition(string json)
    {
        using var document = ParseStrict(json);
        var root = RequireObject(document.RootElement, "composition");
        RequireFields(root, "invalid-field", "schemaVersion", "catalog", "profile", "groups", "add", "remove", "accepted", "settings", "resources");
        var schemaVersion = RequiredString(root, "schemaVersion");
        CheckSchema(schemaVersion);
        var catalog = ReadCatalogPin(RequiredObject(root, "catalog"));
        DefinitionReference? profile = null;
        if (root.TryGetProperty("profile", out var profileValue) && profileValue.ValueKind is not JsonValueKind.Null)
        {
            profile = ReadReference(RequireObject(profileValue, "profile"));
            if (profile.Kind != "profile")
                throw new SelectionDocumentException("invalid-field", "The starting selection must reference a profile.");
        }

        var groups = RequiredArray(root, "groups").EnumerateArray()
            .Select(value => ReadReference(RequireObject(value, "group reference")))
            .ToImmutableArray();
        if (groups.Any(group => group.Kind != "group" || group.Origin != "foundation"))
            throw new SelectionDocumentException("invalid-field", "Groups must reference Foundation group definitions.");
        if (groups.GroupBy(x => (x.Origin, x.Kind, x.Id, x.Version, x.Digest)).Any(group => group.Count() > 1))
            throw new SelectionDocumentException("duplicate-authored-selection", "The same group reference is repeated.");

        var add = ReadUniqueStrings(RequiredArray(root, "add"), "duplicate-authored-selection");
        var remove = ReadUniqueStrings(RequiredArray(root, "remove"), "duplicate-authored-selection");
        var accepted = ReadAccepted(RequiredObject(root, "accepted"));
        return new AuthoredComposition(
            schemaVersion,
            catalog,
            profile,
            groups,
            add,
            remove,
            accepted,
            OptionalOpaqueObject(root, "settings"),
            OptionalOpaqueObject(root, "resources"));
    }

    private static JsonDocument ParseStrict(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        ValidateUnicode(json);
        try
        {
            var document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Disallow, AllowTrailingCommas = false, MaxDepth = 64 });
            try
            {
                ValidateNoDuplicateKeys(document.RootElement);
                return document;
            }
            catch
            {
                document.Dispose();
                throw;
            }
        }
        catch (JsonException ex)
        {
            throw new SelectionDocumentException("invalid-field", $"Invalid JSON document: {ex.Message}");
        }
    }

    private static void ValidateNoDuplicateKeys(JsonElement element)
    {
        if (element.ValueKind is JsonValueKind.Object)
        {
            var names = new HashSet<string>(s_comparer);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw new SelectionDocumentException("duplicate-json-key", $"Duplicate JSON property: {property.Name}.");
                ValidateNoDuplicateKeys(property.Value);
            }
        }
        else if (element.ValueKind is JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                ValidateNoDuplicateKeys(item);
        }
        else if (element.ValueKind is JsonValueKind.Number && (!double.TryParse(element.GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number)))
            throw new SelectionDocumentException("invalid-field", "A JSON number is not an I-JSON finite double.");
    }

    private static void ValidateUnicode(string json)
    {
        var insideString = false;
        for (var index = 0; index < json.Length; index++)
        {
            var ch = json[index];
            if (char.IsHighSurrogate(ch))
            {
                if (++index >= json.Length || !char.IsLowSurrogate(json[index]))
                    throw new SelectionDocumentException("invalid-unicode", "A JSON document contains an unpaired surrogate.");
                continue;
            }
            if (char.IsLowSurrogate(ch))
                throw new SelectionDocumentException("invalid-unicode", "A JSON document contains an unpaired surrogate.");
            if (ch == '"')
            {
                insideString = !insideString;
                continue;
            }
            if (!insideString || ch != '\\' || index + 1 >= json.Length)
                continue;
            if (json[index + 1] != 'u')
            {
                index++;
                continue;
            }
            if (index + 5 >= json.Length || !ushort.TryParse(json.AsSpan(index + 2, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
                continue; // The JSON parser supplies the syntax error.
            index += 5;
            if (code is >= 0xD800 and <= 0xDBFF)
            {
                if (index + 6 >= json.Length || json[index + 1] != '\\' || json[index + 2] != 'u' ||
                    !ushort.TryParse(json.AsSpan(index + 3, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var low) ||
                    low is < 0xDC00 or > 0xDFFF)
                    throw new SelectionDocumentException("invalid-unicode", "An escaped high surrogate has no escaped low surrogate.");
                index += 6;
            }
            else if (code is >= 0xDC00 and <= 0xDFFF)
                throw new SelectionDocumentException("invalid-unicode", "A JSON document contains an unpaired escaped low surrogate.");
        }
    }

    private static ImmutableArray<SelectionDefinition> ReadDefinitions(JsonElement array, string expectedKind) =>
        array.EnumerateArray().Select(value => ReadDefinition(RequireObject(value, "definition"), expectedKind)).ToImmutableArray();

    private static SelectionDefinition ReadDefinition(JsonElement element, string expectedKind, bool hasSchemaVersion = false)
    {
        if (!hasSchemaVersion)
            RequireFields(element, "unknown-catalog-field", "kind", "id", "version", "digest", "members", "rationale", "title", "description", "dependencyExplanations");
        var kind = RequiredString(element, "kind");
        if (kind != expectedKind)
            throw new SelectionDocumentException("invalid-field", $"Expected a {expectedKind} definition.");
        var members = ReadUniqueStrings(RequiredArray(element, "members"), "duplicate-member");
        var explanations = RequiredArray(element, "dependencyExplanations").EnumerateArray().Select(value =>
        {
            var row = RequireObject(value, "dependency explanation");
            RequireFields(row, "unknown-catalog-field", "featureId", "dependencyId", "mode", "reason");
            var mode = RequiredString(row, "mode");
            if (mode is not ("required" or "optional"))
                throw new SelectionDocumentException("invalid-field", "Dependency explanation mode must be required or optional.");
            return new DependencyExplanation(RequiredString(row, "featureId"), RequiredString(row, "dependencyId"), mode, RequiredString(row, "reason"));
        }).ToImmutableArray();
        if (explanations.GroupBy(x => (x.FeatureId, x.DependencyId, x.Mode)).Any(group => group.Count() > 1))
            throw new SelectionDocumentException("duplicate-explanation", "The same dependency explanation is repeated.");
        if (explanations.Any(x => !members.Contains(x.FeatureId, s_comparer)))
            throw new SelectionDocumentException("invalid-field", "Dependency explanations must belong to a selected member.");
        var definition = new SelectionDefinition(
            kind,
            RequiredString(element, "id"),
            RequiredString(element, "version"),
            RequiredDigest(element, "digest"),
            members,
            RequiredString(element, "rationale"),
            RequiredString(element, "title"),
            RequiredString(element, "description"),
            explanations);
        CheckDigest(definition.Digest, SelectionDigest.ComputeDefinitionDigest(definition), $"definition {definition.Id}@{definition.Version}");
        return definition;
    }

    private static CatalogPin ReadCatalogPin(JsonElement element)
    {
        RequireFields(element, "invalid-field", "id", "version", "digest");
        return new CatalogPin(RequiredString(element, "id"), RequiredString(element, "version"), RequiredDigest(element, "digest"));
    }

    private static DefinitionReference ReadReference(JsonElement element)
    {
        RequireFields(element, "invalid-field", "origin", "kind", "id", "version", "digest");
        var origin = RequiredString(element, "origin");
        var kind = RequiredString(element, "kind");
        if (origin is not ("foundation" or "workspace") || kind is not ("profile" or "group"))
            throw new SelectionDocumentException("invalid-field", "Definition origin or kind is unsupported.");
        return new DefinitionReference(origin, kind, RequiredString(element, "id"), RequiredString(element, "version"), RequiredDigest(element, "digest"));
    }

    private static AcceptedSelection ReadAccepted(JsonElement element)
    {
        RequireFields(element, "invalid-field", "catalogDigest", "featureIds", "locks");
        var ids = ReadUniqueStrings(RequiredArray(element, "featureIds"), "duplicate-authored-selection");
        var locks = RequiredArray(element, "locks").EnumerateArray().Select(value =>
        {
            var row = RequireObject(value, "accepted lock");
            RequireFields(row, "invalid-field", "featureId", "kind", "packageId", "packageVersion", "manifestDigest", "evidenceSource");
            var kind = RequiredString(row, "kind");
            if (kind is not ("package" or "hostBundled"))
                throw new SelectionDocumentException("invalid-field", "Feature lock kind is unsupported.");
            var packageId = OptionalString(row, "packageId");
            var packageVersion = OptionalString(row, "packageVersion");
            var manifestDigest = OptionalString(row, "manifestDigest");
            if (kind == "package" && (packageId is null || packageVersion is null || manifestDigest is null))
                throw new SelectionDocumentException("invalid-field", "A package lock needs package ID, version and manifest digest.");
            if (kind == "package" && (!SelectionValueRules.IsSafeReference(packageId) ||
                !SelectionValueRules.IsSafeReference(packageVersion) || !SelectionValueRules.IsDigest(manifestDigest)))
                throw new SelectionDocumentException("invalid-field", "A package lock needs safe identity labels and a SHA-256 manifest digest.");
            if (kind == "hostBundled" && (packageId is not null || packageVersion is not null || manifestDigest is not null))
                throw new SelectionDocumentException("invalid-field", "A host-bundled lock cannot carry package fields.");
            var evidenceSource = OptionalString(row, "evidenceSource") ?? "accepted";
            if (!SelectionValueRules.IsSafeReference(evidenceSource))
                throw new SelectionDocumentException("invalid-field", "A lock evidence source must be a safe reference label.");
            return new FeatureLock(RequiredString(row, "featureId"), kind, packageId, packageVersion, manifestDigest, evidenceSource);
        }).ToImmutableArray();
        if (locks.GroupBy(x => x.FeatureId, s_comparer).Any(group => group.Count() > 1) || locks.Any(x => !ids.Contains(x.FeatureId, s_comparer)))
            throw new SelectionDocumentException("invalid-field", "Accepted locks must uniquely refer to accepted feature IDs.");
        return new AcceptedSelection(RequiredDigest(element, "catalogDigest"), ids, locks);
    }

    private static JsonElement? OptionalOpaqueObject(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
            return null;
        return RequireObject(value, name).Clone();
    }

    private static ImmutableArray<string> ReadUniqueStrings(JsonElement array, string duplicateCode)
    {
        var values = array.EnumerateArray().Select(value => RequiredNonemptyString(value)).ToImmutableArray();
        if (values.Distinct(s_comparer).Count() != values.Length)
            throw new SelectionDocumentException(duplicateCode, "The same ID appears more than once in a set-valued array.");
        return values;
    }

    private static string RequiredDigest(JsonElement element, string name)
    {
        var digest = RequiredString(element, name);
        if (!SelectionValueRules.IsDigest(digest))
            throw new SelectionDocumentException("invalid-field", $"{name} must be 64 lowercase hexadecimal characters.");
        return digest;
    }

    private static void CheckDigest(string supplied, string computed, string subject)
    {
        if (!string.Equals(supplied, computed, StringComparison.Ordinal))
            throw new SelectionDocumentException("digest-mismatch", $"The {subject} digest does not match its content.");
    }

    private static void CheckSchema(string version)
    {
        if (version != "1")
            throw new SelectionDocumentException("schema-unsupported", $"Selection schema version {version} is unsupported.");
    }

    private static void RequireFields(JsonElement element, string code, params string[] allowed)
    {
        var names = new HashSet<string>(allowed, s_comparer);
        foreach (var property in element.EnumerateObject())
            if (!names.Contains(property.Name))
                throw new SelectionDocumentException(code, $"Unknown field {property.Name}.");
    }

    private static JsonElement RequireObject(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object ? element : throw new SelectionDocumentException("invalid-field", $"{name} must be an object.");

    private static JsonElement RequiredObject(JsonElement element, string name) => RequireObject(RequiredProperty(element, name), name);

    private static JsonElement RequiredArray(JsonElement element, string name)
    {
        var value = RequiredProperty(element, name);
        return value.ValueKind == JsonValueKind.Array ? value : throw new SelectionDocumentException("invalid-field", $"{name} must be an array.");
    }

    private static JsonElement RequiredProperty(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value : throw new SelectionDocumentException("invalid-field", $"Required field {name} is missing.");

    private static string RequiredString(JsonElement element, string name) => RequiredNonemptyString(RequiredProperty(element, name));

    private static string RequiredNonemptyString(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(element.GetString()))
            throw new SelectionDocumentException("invalid-field", "A required value must be a nonempty string.");
        return element.GetString()!;
    }

    private static string? OptionalString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
            return null;
        return RequiredNonemptyString(value);
    }
}
