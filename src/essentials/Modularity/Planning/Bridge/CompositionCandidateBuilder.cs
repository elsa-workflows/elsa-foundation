using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Modularity.Planning.Json;
using Elsa.Modularity.Planning.Models;
using Elsa.Modularity.Planning.Services;

namespace Elsa.Modularity.Planning.Bridge;

/// <summary>A redacted semantic description of one reviewed setting change.</summary>
public sealed record CompositionCandidateChange(
    string FeatureId,
    string Pointer,
    string ValueType,
    string SourceLayer,
    bool Changed);

/// <summary>In-memory host-file candidate and planner result. File identities and bytes stay local to the caller.</summary>
public sealed record CompositionCandidate(
    IReadOnlyDictionary<string, byte[]> Files,
    ImmutableArray<CompositionCandidateChange> Changes,
    SelectionPlan Plan);

/// <summary>Builds a fresh file bundle by patching only reviewed, existing settings in their observed source layer.</summary>
public static class CompositionCandidateBuilder
{
    // Temporary CI selector probe; this validation PR is intentionally closed without merging.
    public static CompositionCandidate Build(
        SourceSnapshot snapshot,
        SelectionCatalog catalog,
        AuthoredComposition authored,
        SettingReviewDocument? review)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(authored);
        if (authored.Catalog is null || authored.Accepted is null)
            throw Refuse("bridge-authored-invalid", "The authored composition is missing its catalog or accepted selection.");

        if (!string.Equals(catalog.Id, authored.Catalog.Id, StringComparison.Ordinal) ||
            !string.Equals(catalog.Version, authored.Catalog.Version, StringComparison.Ordinal) ||
            !string.Equals(catalog.Digest, authored.Catalog.Digest, StringComparison.Ordinal) ||
            !string.Equals(catalog.Digest, authored.Accepted.CatalogDigest, StringComparison.Ordinal))
            throw Refuse("bridge-catalog-mismatch", "The authored composition does not match the supplied selection catalog.");
        if (authored.Add.Any(id => !SelectionValueRules.IsSafeReference(id)) ||
            authored.Remove.Any(id => !SelectionValueRules.IsSafeReference(id)) ||
            authored.Accepted.FeatureIds.Any(id => !SelectionValueRules.IsSafeReference(id)))
            throw Refuse("bridge-authored-invalid", "The authored composition contains an unsafe feature identity.");

        var files = snapshot.FileNames.ToDictionary(name => name, snapshot.CopyBytes, StringComparer.Ordinal);
        var selection = snapshot.Selection;
        var baseShellName = RequireFile(snapshot, "shells.json");
        var baseAppsettingsName = RequireFile(snapshot, "appsettings.json");
        var overlayShellName = RequireFile(snapshot, selection.ShellOverlayFileName);
        var overlayAppsettingsName = selection.AppsettingsOverlayFileName is { } appOverlayName
            ? RequireFile(snapshot, appOverlayName)
            : null;

        var baseShellJson = snapshot.ReadText(baseShellName);
        var overlayShellJson = snapshot.ReadText(overlayShellName);
        CshellsSource source;
        try
        {
            source = CshellsSourceReader.Read(baseShellJson, overlayShellJson, selection.ShellId);
        }
        catch (CshellsSourceException exception)
        {
            throw Refuse(exception.Code, "A selected shell source has an unsupported or invalid shape.");
        }

        SelectionPlan plan;
        try
        {
            plan = SelectionPlanner.Plan(catalog, authored, persistence: BuildPersistenceEvidence(authored));
        }
        catch (SelectionDocumentException)
        {
            throw Refuse("bridge-authored-invalid", "The authored composition or selection catalog is invalid.");
        }
        if (plan.Findings.Any(finding => finding.Code is "catalog-pin-unresolved" or "accepted-pin-unresolved" or "candidate-re-resolution"))
            throw Refuse("bridge-selection-drift", "The authored selection no longer matches its accepted selection.");
        if (!plan.SelectedFeatureIds.SequenceEqual(authored.Accepted.FeatureIds.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            throw Refuse("bridge-selection-drift", "The authored selection no longer matches its accepted selection.");
        if (!source.EnabledFeatureIds.Order(StringComparer.Ordinal).SequenceEqual(plan.SelectedFeatureIds, StringComparer.Ordinal))
            throw Refuse("bridge-selection-drift", "The authored selection cannot be applied to this source without an explicit activation mapping.");

        var settingRoot = ReadOptionalObject(authored.Settings, "Authored settings must be an object.");
        var patches = ReadSettingPatches(settingRoot, source, review);
        var baseShellRoot = ParseObject(baseShellJson, "The base shell document is malformed or unsupported.");
        var overlayShellRoot = ParseObject(overlayShellJson, "The selected shell overlay is malformed or unsupported.");
        foreach (var patch in patches)
        {
            var target = patch.SourceLayer == CshellsSourceLayer.Base ? baseShellRoot : overlayShellRoot;
            var featureNode = FindFeatureSettings(target, selection.ShellId, patch.FeatureId);
            if (!PatchExisting(featureNode, patch.Segments, patch.Value))
                patch.Changed = false;
        }

        var baseAppRoot = ParseObject(snapshot.ReadText(baseAppsettingsName), "An appsettings document is malformed or unsupported.");
        var overlayAppRoot = overlayAppsettingsName is null
            ? null
            : ParseObject(snapshot.ReadText(overlayAppsettingsName), "An appsettings overlay is malformed or unsupported.");
        var resourceChanges = PatchResources(authored.Resources, selection.ShellId, baseShellRoot, overlayShellRoot,
            baseAppRoot, overlayAppRoot);

        if (patches.Any(patch => patch.Changed && patch.SourceLayer == CshellsSourceLayer.Base) || resourceChanges.Any(change => change.SourceLayer == "base" && change.Changed && change.Prefix == "/shell/"))
            files[baseShellName] = Serialize(baseShellRoot);
        if (patches.Any(patch => patch.Changed && patch.SourceLayer == CshellsSourceLayer.Overlay) || resourceChanges.Any(change => change.SourceLayer == "overlay" && change.Changed && change.Prefix == "/shell/"))
            files[overlayShellName] = Serialize(overlayShellRoot);
        if (resourceChanges.Any(change => change.SourceLayer == "base" && change.Changed && change.Prefix == "/appsettings/"))
            files[baseAppsettingsName] = Serialize(baseAppRoot);
        if (overlayAppsettingsName is not null && resourceChanges.Any(change => change.SourceLayer == "overlay" && change.Changed && change.Prefix == "/appsettings/"))
            files[overlayAppsettingsName] = Serialize(overlayAppRoot!);

        var changes = patches.Select(patch => new CompositionCandidateChange(
                patch.FeatureId,
                patch.Pointer,
                patch.ValueType,
                patch.SourceLayer == CshellsSourceLayer.Base ? "base" : "overlay",
                patch.Changed))
            .Concat(resourceChanges.Select(change => new CompositionCandidateChange(
                change.FeatureId, change.Pointer, "resource-name", change.SourceLayer, change.Changed)))
            .OrderBy(item => item.FeatureId, StringComparer.Ordinal)
            .ThenBy(item => item.Pointer, StringComparer.Ordinal)
            .ToImmutableArray();

        return new CompositionCandidate(files, changes, plan);
    }

    private static ImmutableArray<SettingPatch> ReadSettingPatches(
        JsonElement? settings,
        CshellsSource source,
        SettingReviewDocument? review)
    {
        if (settings is null)
            return [];

        var output = ImmutableArray.CreateBuilder<SettingPatch>();
        foreach (var feature in settings.Value.EnumerateObject())
        {
            if (!SelectionValueRules.IsSafeReference(feature.Name) || feature.Value.ValueKind != JsonValueKind.Object)
                throw Refuse("bridge-portable-unsafe", "Authored settings contain an invalid feature or settings object.");
            ExpandAuthored(feature.Name, feature.Value, [], source, review, output);
        }
        return output.ToImmutable();
    }

    private static void ExpandAuthored(
        string featureId,
        JsonElement value,
        ImmutableArray<string> segments,
        CshellsSource source,
        SettingReviewDocument? review,
        ImmutableArray<SettingPatch>.Builder output)
    {
        if (value.ValueKind == JsonValueKind.Object && value.EnumerateObject().Any())
        {
            foreach (var property in value.EnumerateObject())
                ExpandAuthored(featureId, property.Value, segments.Add(property.Name), source, review, output);
            return;
        }
        if (value.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in value.EnumerateArray())
                ExpandAuthored(featureId, item, segments.Add(index++.ToString(CultureInfo.InvariantCulture)), source, review, output);
            if (index == 0)
                AddPatch(featureId, value, segments, source, review, output);
            return;
        }
        AddPatch(featureId, value, segments, source, review, output);
    }

    private static void AddPatch(
        string featureId,
        JsonElement value,
        ImmutableArray<string> segments,
        CshellsSource source,
        SettingReviewDocument? review,
        ImmutableArray<SettingPatch>.Builder output)
    {
        if (segments.IsEmpty)
            throw Refuse("bridge-portable-unsafe", "Authored settings must identify a setting below a feature.");
        var pointer = EncodePointer(segments);
        if (review is null || !review.TryGetExact(featureId, pointer, out var declaration) ||
            !declaration.Portable || !declaration.MatchesValueKind(value.ValueKind))
            throw Refuse("bridge-portable-unsafe", "An authored setting is unreviewed or its JSON type changed.");
        if (IsHostOwnedPersistenceField(segments))
            throw Refuse("bridge-portable-unsafe", "Host-owned provider and connection settings cannot be authored as portable values.");

        var matchingSource = source.SettingLeaves
            .Where(leaf => string.Equals(leaf.FeatureId, featureId, StringComparison.Ordinal) && IsPointerPrefix(leaf.Pointer, pointer))
            .OrderByDescending(leaf => leaf.Pointer.Length)
            .FirstOrDefault();
        if (matchingSource is null)
            throw Refuse("bridge-mapping-unresolved", "An authored setting has no existing local source path.");

        var patchSegments = DecodePointer(pointer).ToImmutableArray();
        if (patchSegments.Length == 0 || !PathMatches(matchingSource.Pointer, patchSegments))
            throw Refuse("bridge-mapping-unresolved", "An authored setting has no existing local source path.");
        var type = TypeName(value.ValueKind);
        output.Add(new SettingPatch(featureId, pointer, patchSegments, value.Clone(), type, matchingSource.SourceLayer));
    }

    private static JsonNode FindFeatureSettings(JsonObject root, string shellId, string featureId)
    {
        var shellRoot = GetProperty(root, "CShells") is JsonObject cshells ? cshells : null;
        var shells = shellRoot is null ? null : GetProperty(shellRoot, "Shells") as JsonObject;
        var shell = shells is null ? null : GetProperty(shells, shellId) as JsonObject;
        var features = shell is null ? null : GetProperty(shell, "Features");
        var feature = FindFeature(features, featureId);
        if (feature is null)
            throw Refuse("bridge-mapping-unresolved", "An authored setting has no existing local source path.");
        return feature;
    }

    private static JsonNode? FindFeature(JsonNode? features, string featureId)
    {
        if (features is JsonObject map)
            return GetProperty(map, featureId);
        if (features is JsonArray array)
        {
            foreach (var entry in array)
            {
                if (entry is JsonValue text && text.TryGetValue<string>(out var id) && string.Equals(id, featureId, StringComparison.Ordinal))
                    return entry;
                if (entry is JsonObject obj && GetProperty(obj, "Name") is JsonValue name && name.TryGetValue<string>(out id) && string.Equals(id, featureId, StringComparison.Ordinal))
                    return obj;
            }
        }
        return null;
    }

    private static bool PatchExisting(JsonNode feature, ImmutableArray<string> segments, JsonElement value)
    {
        var current = feature;
        for (var index = 0; index < segments.Length; index++)
        {
            var segment = segments[index];
            var last = index == segments.Length - 1;
            if (current is JsonObject obj)
            {
                var existingName = FindPropertyName(obj, segment);
                if (existingName is null)
                    throw Refuse("bridge-mapping-unresolved", "An authored setting has no existing local source path.");
                if (last)
                {
                    var existing = obj[existingName];
                    if ((existing is JsonArray or JsonObject) && value.ValueKind is not (JsonValueKind.Array or JsonValueKind.Object) || !SameType(TypeOf(existing), value.ValueKind))
                        throw Refuse("bridge-portable-unsafe", "An authored setting changed its JSON shape.");
                    var replacement = JsonNode.Parse(value.GetRawText());
                    var changed = !JsonNode.DeepEquals(existing, replacement);
                    obj[existingName] = replacement;
                    return changed;
                }
                current = obj[existingName] ?? throw Refuse("bridge-mapping-unresolved", "An authored setting has no existing local source path.");
            }
            else if (current is JsonArray array)
            {
                if (!int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out var arrayIndex) ||
                    arrayIndex < 0 || arrayIndex >= array.Count || arrayIndex.ToString(CultureInfo.InvariantCulture) != segment)
                    throw Refuse("bridge-mapping-unresolved", "An authored setting has no existing local source path.");
                if (last)
                {
                    var existing = array[arrayIndex];
                    if ((existing is JsonArray or JsonObject) && value.ValueKind is not (JsonValueKind.Array or JsonValueKind.Object) || !SameType(TypeOf(existing), value.ValueKind))
                        throw Refuse("bridge-portable-unsafe", "An authored setting changed its JSON shape.");
                    var replacement = JsonNode.Parse(value.GetRawText());
                    var changed = !JsonNode.DeepEquals(existing, replacement);
                    array[arrayIndex] = replacement;
                    return changed;
                }
                current = array[arrayIndex] ?? throw Refuse("bridge-mapping-unresolved", "An authored setting has no existing local source path.");
            }
            else
                throw Refuse("bridge-mapping-unresolved", "An authored setting has no existing local source path.");
        }
        throw Refuse("bridge-mapping-unresolved", "An authored setting has no existing local source path.");
    }

    private static ImmutableArray<ResourceChange> PatchResources(
        JsonElement? resources,
        string shellId,
        JsonObject baseShell,
        JsonObject overlayShell,
        JsonObject baseAppsettings,
        JsonObject? overlayAppsettings)
    {
        if (resources is null)
            return [];
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        AddResourceDefinitions(baseAppsettings, names);
        if (overlayAppsettings is not null)
            AddResourceDefinitions(overlayAppsettings, names);

        if (resources.Value.ValueKind != JsonValueKind.Object ||
            resources.Value.EnumerateObject().Any(property => property.Name != "persistence") ||
            GetProperty(resources.Value, "persistence") is not JsonElement persistence || persistence.ValueKind != JsonValueKind.Object)
            throw Refuse("bridge-portable-unsafe", "Authored logical resources have an unsupported shape.");
        if (persistence.EnumerateObject().Any(property => property.Name is not ("defaultResource" or "bindings")))
            throw Refuse("bridge-portable-unsafe", "Authored logical resources contain an unsupported field.");

        var changes = ImmutableArray.CreateBuilder<ResourceChange>();
        if (GetProperty(persistence, "defaultResource") is JsonElement defaultResource)
        {
            var name = RequireResource(defaultResource, names);
            var targets = new[]
            {
                (Persistence: ShellPersistence(overlayShell, shellId), Layer: "overlay", Prefix: "/shell/"),
                (Persistence: ShellPersistence(baseShell, shellId), Layer: "base", Prefix: "/shell/"),
                (Persistence: AppPersistence(overlayAppsettings), Layer: "overlay", Prefix: "/appsettings/"),
                (Persistence: AppPersistence(baseAppsettings), Layer: "base", Prefix: "/appsettings/")
            };
            var target = targets.FirstOrDefault(item => item.Persistence is not null && FindPropertyName(item.Persistence, "DefaultResource") is not null);
            if (target.Persistence is null)
                throw Refuse("bridge-mapping-unresolved", "The authored default resource has no existing local source path.");
            var changed = PatchString(target.Persistence, "DefaultResource", name);
            changes.Add(new ResourceChange("Persistence", "/persistence/defaultResource", target.Layer, changed, target.Prefix));
        }
        if (GetProperty(persistence, "bindings") is JsonElement bindings)
        {
            if (bindings.ValueKind != JsonValueKind.Object)
                throw Refuse("bridge-portable-unsafe", "Authored logical resource bindings have an unsupported shape.");
            var bindingIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var binding in bindings.EnumerateObject())
            {
                if (!SelectionValueRules.IsSafeReference(binding.Name) || !bindingIds.Add(binding.Name))
                    throw Refuse("bridge-portable-unsafe", "An authored logical resource binding has an invalid feature identity.");
                var name = RequireResource(binding.Value, names);
                var overlayBindings = GetProperty(ShellPersistence(overlayShell, shellId), "Bindings") as JsonObject;
                var baseBindings = GetProperty(ShellPersistence(baseShell, shellId), "Bindings") as JsonObject;
                var target = overlayBindings is not null && FindPropertyName(overlayBindings, binding.Name) is not null
                    ? (Bindings: overlayBindings, Layer: "overlay", Prefix: "/shell/")
                    : baseBindings is not null && FindPropertyName(baseBindings, binding.Name) is not null
                        ? (Bindings: baseBindings, Layer: "base", Prefix: "/shell/")
                        : (Bindings: (JsonObject?)null, Layer: "", Prefix: "");
                if (target.Bindings is null)
                    throw Refuse("bridge-mapping-unresolved", "An authored feature resource binding has no existing local source path.");
                var changed = PatchString(target.Bindings, binding.Name, name);
                changes.Add(new ResourceChange(binding.Name, "/persistence/bindings/" + EscapePointer(binding.Name), target.Layer, changed, target.Prefix));
            }
        }
        return changes.ToImmutable();
    }

    private static string RequireResource(JsonElement value, IReadOnlyDictionary<string, string> names)
    {
        if (value.ValueKind != JsonValueKind.String || !SelectionValueRules.IsSafeReference(value.GetString()) || !names.ContainsKey(value.GetString()!))
            throw Refuse("bridge-mapping-unresolved", "An authored logical resource is not defined in the selected local files.");
        return names[value.GetString()!];
    }

    private static JsonObject? ShellPersistence(JsonObject root, string shellId)
    {
        var shells = GetProperty(GetProperty(root, "CShells") as JsonObject, "Shells") as JsonObject;
        var shell = GetProperty(shells, shellId) as JsonObject;
        var configuration = GetProperty(shell, "Configuration") as JsonObject;
        var elsa = GetProperty(configuration, "Elsa") as JsonObject;
        return GetProperty(elsa, "Persistence") as JsonObject;
    }

    private static JsonObject? AppPersistence(JsonObject? root)
    {
        var elsa = GetProperty(root, "Elsa") as JsonObject;
        return GetProperty(elsa, "Persistence") as JsonObject;
    }

    private static bool PatchString(JsonObject parent, string propertyName, string value)
    {
        var actualName = FindPropertyName(parent, propertyName);
        if (actualName is null || parent[actualName] is not JsonValue existing || !existing.TryGetValue<string>(out var previous))
            throw Refuse("bridge-mapping-unresolved", "An authored logical resource has no existing local source path.");
        parent[actualName] = JsonValue.Create(value);
        return !string.Equals(previous, value, StringComparison.Ordinal);
    }

    private static void AddResourceDefinitions(JsonObject root, Dictionary<string, string> names)
    {
        var elsa = GetProperty(root, "Elsa") is JsonObject elsaObject ? elsaObject : null;
        var persistence = elsa is null ? null : GetProperty(elsa, "Persistence") as JsonObject;
        var resources = persistence is null ? null : GetProperty(persistence, "Resources");
        if (resources is null)
            return;
        if (resources is not JsonObject map)
            throw Refuse("bridge-source-invalid", "Selected persistence resources must be an object.");
        foreach (var (name, definition) in map)
        {
            if (!IsLogicalResourceName(name) || definition is not JsonObject)
                throw Refuse("bridge-source-invalid", "A selected persistence resource definition is invalid.");
            if (names.TryGetValue(name, out var existing) && !string.Equals(existing, name, StringComparison.Ordinal))
                throw Refuse("bridge-source-duplicate", "Selected persistence resource names that differ only by case are ambiguous.");
            names.TryAdd(name, name);
        }
    }

    private static PersistenceEvidence BuildPersistenceEvidence(AuthoredComposition authored)
    {
        var references = ImmutableArray.CreateBuilder<string>();
        if (authored.Resources is { ValueKind: JsonValueKind.Object } resources &&
            GetProperty(resources, "persistence") is JsonElement persistence && persistence.ValueKind == JsonValueKind.Object)
        {
            if (GetProperty(persistence, "defaultResource") is JsonElement defaultResource && defaultResource.ValueKind == JsonValueKind.String)
                references.Add(defaultResource.GetString()!);
            if (GetProperty(persistence, "bindings") is JsonElement bindings && bindings.ValueKind == JsonValueKind.Object)
                references.AddRange(bindings.EnumerateObject().Where(item => item.Value.ValueKind == JsonValueKind.String).Select(item => item.Value.GetString()!));
        }
        return new PersistenceEvidence("unchecked", "selected-local-files", references.Distinct(StringComparer.OrdinalIgnoreCase).ToImmutableArray(), []);
    }

    private static string RequireFile(SourceSnapshot snapshot, string fileName)
    {
        var match = snapshot.FileNames.FirstOrDefault(name => string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase));
        return match ?? throw Refuse("bridge-source-missing", "A selected source file is missing from the frozen snapshot.");
    }

    private static JsonObject ParseObject(string json, string message)
    {
        try
        {
            using var document = SelectionJsonReader.ParseStrict(json);
            return JsonNode.Parse(document.RootElement.GetRawText()) as JsonObject ?? throw Refuse("bridge-source-invalid", message);
        }
        catch (SelectionDocumentException)
        {
            throw Refuse("bridge-source-invalid", message);
        }
        catch (JsonException)
        {
            throw Refuse("bridge-source-invalid", message);
        }
    }

    private static JsonElement? ReadOptionalObject(JsonElement? value, string message)
    {
        if (value is null) return null;
        if (value.Value.ValueKind != JsonValueKind.Object) throw Refuse("bridge-portable-unsafe", message);
        return value;
    }

    private static JsonNode? GetProperty(JsonObject? parent, string name)
    {
        if (parent is null) return null;
        var property = FindPropertyName(parent, name);
        return property is null ? null : parent[property];
    }

    private static string? FindPropertyName(JsonObject? parent, string name)
    {
        if (parent is null) return null;
        var match = parent.Select(pair => pair.Key).Where(key => string.Equals(key, name, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (match.Length > 1) throw Refuse("bridge-source-duplicate", "Selected JSON keys that differ only by case are ambiguous.");
        return match.Length == 0 ? null : match[0];
    }

    private static JsonElement? GetProperty(JsonElement? parent, string name) => parent is null ? null : GetProperty(parent.Value, name);

    private static JsonElement? GetProperty(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object) return null;
        var matches = parent.EnumerateObject().Where(property => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length > 1) throw Refuse("bridge-source-duplicate", "Selected JSON keys that differ only by case are ambiguous.");
        return matches.Length == 0 ? null : matches[0].Value;
    }

    private static string[] DecodePointer(string pointer) => pointer[1..].Split('/').Select(Unescape).ToArray();
    private static string Unescape(string part) => part.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
    private static string EncodePointer(IEnumerable<string> segments) => string.Concat(segments.Select(segment => "/" + segment.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal)));
    private static string EscapePointer(string segment) => segment.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
    private static bool IsHostOwnedPersistenceField(ImmutableArray<string> segments) =>
        segments.Any(segment => segment.Equals("ConnectionStrings", StringComparison.OrdinalIgnoreCase)) ||
        segments.Length >= 2 && segments[0].Equals("Elsa", StringComparison.OrdinalIgnoreCase) && segments[1].Equals("Persistence", StringComparison.OrdinalIgnoreCase) ||
        segments.Length > 0 && (segments[^1].Equals("Provider", StringComparison.OrdinalIgnoreCase) ||
            segments[^1].Equals("ConnectionString", StringComparison.OrdinalIgnoreCase) ||
            segments[^1].Equals("ConnectionName", StringComparison.OrdinalIgnoreCase));
    private static bool IsLogicalResourceName(string name) => SelectionValueRules.IsSafeReference(name) &&
        name.Length > 0 && (char.IsLetter(name[0]) || name[0] == '_') &&
        !name.Equals("true", StringComparison.OrdinalIgnoreCase) && !name.Equals("false", StringComparison.OrdinalIgnoreCase) &&
        !name.Equals("null", StringComparison.OrdinalIgnoreCase) && name.All(character => char.IsLetterOrDigit(character) || character is '_' or '-' or '.');
    private static bool IsPointerPrefix(string leafPointer, string pointer) =>
        string.Equals(leafPointer, pointer, StringComparison.OrdinalIgnoreCase) ||
        pointer.StartsWith(leafPointer.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase);
    private static bool PathMatches(string sourcePointer, ImmutableArray<string> targetSegments) =>
        targetSegments.Length >= DecodePointer(sourcePointer).Length &&
        DecodePointer(sourcePointer).Select((part, index) => string.Equals(part, targetSegments[index], StringComparison.OrdinalIgnoreCase)).All(match => match);
    private static string TypeName(JsonValueKind type) => type switch
    {
        JsonValueKind.True or JsonValueKind.False => "boolean", JsonValueKind.Number => "number", JsonValueKind.String => "string",
        JsonValueKind.Null => "null", JsonValueKind.Object => "object", JsonValueKind.Array => "array", _ => "unknown"
    };
    private static JsonValueKind TypeOf(JsonNode? node)
    {
        if (node is null) return JsonValueKind.Null;
        using var document = JsonDocument.Parse(node.ToJsonString());
        return document.RootElement.ValueKind;
    }
    private static bool SameType(JsonValueKind left, JsonValueKind right) =>
        left == right || (left is JsonValueKind.True or JsonValueKind.False) && (right is JsonValueKind.True or JsonValueKind.False);
    private static byte[] Serialize(JsonObject root) => new UTF8Encoding(false).GetBytes(root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    private static CompositionImportException Refuse(string code, string message) => new(code, message);

    private sealed record SettingPatch(string FeatureId, string Pointer, ImmutableArray<string> Segments, JsonElement Value, string ValueType, CshellsSourceLayer SourceLayer)
    {
        public bool Changed { get; set; } = true;
    }
    private sealed record ResourceChange(string FeatureId, string Pointer, string SourceLayer, bool Changed, string Prefix);
}
