using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Modularity.Planning.Json;
using Elsa.Modularity.Planning.Models;
using Elsa.Modularity.Planning.Services;

namespace Elsa.Modularity.Planning.Bridge;

public sealed record CompositionImportResult(
    CompositionImportPreview Preview,
    AuthoredComposition Authored,
    SelectionPlan Plan);

/// <summary>A safe, file-derived view. It contains no source paths or unreviewed setting values.</summary>
public sealed record CompositionImportPreview(
    string ShellId,
    string Environment,
    ImmutableArray<string> EnabledFeatureIds,
    ImmutableArray<string> DisabledFeatureIds,
    ImmutableArray<CompositionImportSetting> Settings,
    string? DefaultResource,
    string? DefaultResourceSource,
    ImmutableArray<CompositionImportResourceBinding> Bindings,
    ImmutableArray<string> UncheckedEvidence,
    ImmutableArray<string> ResourceUnresolvedCodes);

public sealed record CompositionImportSetting(
    string FeatureId,
    string Pointer,
    string ValueKind,
    bool Present,
    bool Reviewed,
    bool Portable,
    bool Masked,
    string SourceLayer,
    JsonElement? PortableValue);

public sealed record CompositionImportResourceBinding(
    string FeatureId,
    string ResourceName,
    string Source);

public sealed class CompositionImportException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>Projects a frozen, selected host composition into reviewed authored intent without activating a host.</summary>
public static class CompositionImporter
{
    private static readonly StringComparer s_pathComparer = StringComparer.OrdinalIgnoreCase;

    public static CompositionImportResult Import(
        string baseShellsJson,
        string? overlayShellsJson,
        string baseAppsettingsJson,
        string? overlayAppsettingsJson,
        string shellId,
        string environment,
        SelectionCatalog catalog,
        SettingReviewDocument? settingReview = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (!SelectionValueRules.IsSafeReference(shellId) || !SelectionValueRules.IsSafeReference(environment))
            throw Invalid("The selected shell and environment must be safe identity labels.");

        CshellsSource source;
        try
        {
            source = CshellsSourceReader.Read(baseShellsJson, overlayShellsJson, shellId);
        }
        catch (CshellsSourceException exception)
        {
            throw new CompositionImportException(exception.Code, exception.Message);
        }

        var appsettings = ReadAppsettings(baseAppsettingsJson, overlayAppsettingsJson);
        var expandedSettings = ExpandSettings(source.SettingLeaves, settingReview);
        var portableSettings = BuildAuthoredSettings(expandedSettings);
        var resourceProjection = ReadResourceProjection(source, appsettings);

        var enabled = source.EnabledFeatureIds;
        var disabled = source.DisabledFeatureIds;
        var settingsElement = portableSettings;
        var resourcesElement = ToJsonElement(resourceProjection.AuthoredResources);
        var authored = new AuthoredComposition(
            "1",
            new CatalogPin(catalog.Id, catalog.Version, catalog.Digest),
            null,
            [],
            enabled,
            disabled,
            new AcceptedSelection(catalog.Digest, enabled, []),
            settingsElement,
            resourcesElement);

        var persistence = new PersistenceEvidence(
            resourceProjection.UnresolvedCodes.Length == 0 ? "unchecked" : "unresolved",
            "selected-local-files",
            resourceProjection.ResourceReferences,
            resourceProjection.UnresolvedCodes);
        var plan = SelectionPlanner.Plan(catalog, authored, persistence: persistence);
        if (!plan.SelectedFeatureIds.SequenceEqual(authored.Accepted.FeatureIds, StringComparer.Ordinal))
            throw Invalid("The imported feature selection did not match the planner's exact expansion.");

        var preview = new CompositionImportPreview(
            shellId,
            environment,
            enabled,
            disabled,
            expandedSettings.Select(item => item.Preview).ToImmutableArray(),
            resourceProjection.DefaultResource?.Name,
            resourceProjection.DefaultResource?.Source,
            resourceProjection.Bindings,
            [
                "environment-variable-overrides",
                "command-line-overrides",
                "package-inventory",
                "provider-reachability",
                "physical-persistence-layout",
                "runtime-host-state",
                "database-connectivity"
            ],
            resourceProjection.UnresolvedCodes);

        return new CompositionImportResult(preview, authored, plan);
    }

    private static ImmutableArray<ExpandedSetting> ExpandSettings(
        ImmutableArray<CshellsSettingLeaf> sourceSettings,
        SettingReviewDocument? review)
    {
        var expanded = ImmutableArray.CreateBuilder<ExpandedSetting>();
        foreach (var setting in sourceSettings)
        {
            var segments = DecodeSourcePointer(setting.Pointer);
            ExpandValue(setting.FeatureId, segments, setting.Value, setting.SourceLayer, insideArray: false, review, expanded);
        }
        return expanded.ToImmutable();
    }

    private static void ExpandValue(
        string featureId,
        ImmutableArray<string> segments,
        JsonElement value,
        CshellsSourceLayer sourceLayer,
        bool insideArray,
        SettingReviewDocument? review,
        ImmutableArray<ExpandedSetting>.Builder output)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var properties = value.EnumerateObject().ToArray();
            if (properties.Length > 0)
            {
                RejectPortableParentReview(featureId, segments, value, review);
                foreach (var property in properties)
                    ExpandValue(featureId, Append(segments, property.Name), property.Value, sourceLayer, insideArray, review, output);
                return;
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            var items = value.EnumerateArray().ToArray();
            if (items.Length > 0)
            {
                RejectPortableParentReview(featureId, segments, value, review);
                for (var index = 0; index < items.Length; index++)
                    ExpandValue(featureId, Append(segments, index.ToString(System.Globalization.CultureInfo.InvariantCulture)), items[index], sourceLayer, insideArray: true, review, output);
                return;
            }
        }

        // Features with an empty settings object have no setting path to classify or import.
        if (segments.IsEmpty)
            return;

        var pointer = EncodePointer(segments);
        SettingReviewField? declaration = null;
        if (review is not null && review.TryGetExact(featureId, pointer, out var match))
        {
            declaration = match;
            if (!match.MatchesValueKind(value.ValueKind))
                throw Unsafe("A reviewed setting's declared type does not match the selected source value.");
        }

        var portable = declaration is { Portable: true } && !IsHostOwnedPersistenceField(segments);
        if (portable && insideArray)
            throw Unsafe("Portable values inside nonempty arrays are unsupported until the authored projection preserves array shape.");
        var preview = new CompositionImportSetting(
            featureId,
            pointer,
            TypeName(value.ValueKind),
            true,
            declaration is not null,
            portable,
            !portable,
            sourceLayer == CshellsSourceLayer.Base ? "base" : "overlay",
            portable ? value.Clone() : null);

        output.Add(new ExpandedSetting(featureId, pointer, segments, value.Clone(), sourceLayer, portable, preview));
    }

    private static void RejectPortableParentReview(
        string featureId,
        ImmutableArray<string> segments,
        JsonElement value,
        SettingReviewDocument? review)
    {
        if (segments.IsEmpty || review is null)
            return;

        var pointer = EncodePointer(segments);
        if (!review.TryGetExact(featureId, pointer, out var declaration))
            return;
        if (!declaration.MatchesValueKind(value.ValueKind))
            throw Unsafe("A reviewed setting's declared type does not match the selected source value.");
        if (declaration.Portable)
            throw Unsafe("Nonempty object and array settings require reviews for their individual leaf values.");
    }

    private static JsonElement? BuildAuthoredSettings(ImmutableArray<ExpandedSetting> settings)
    {
        var root = new JsonObject();
        foreach (var setting in settings.Where(item => item.Portable))
        {
            if (IsHostOwnedPersistenceField(setting.Segments))
                continue;

            if (!root.TryGetPropertyValue(setting.FeatureId, out var existing))
            {
                existing = new JsonObject();
                root.Add(setting.FeatureId, existing);
            }

            if (existing is not JsonObject featureSettings)
                throw Invalid("Reviewed settings overlap an incompatible JSON path.");

            AddJsonPath(featureSettings, setting.Segments, setting.Value);
        }

        return root.Count == 0 ? null : ToJsonElement(root);
    }

    private static void AddJsonPath(JsonObject root, ImmutableArray<string> segments, JsonElement value)
    {
        if (segments.IsEmpty)
            throw Invalid("A reviewed setting must identify a leaf below its feature settings object.");

        var current = root;
        for (var index = 0; index < segments.Length - 1; index++)
        {
            var segment = segments[index];
            if (!current.TryGetPropertyValue(segment, out var child))
            {
                child = new JsonObject();
                current.Add(segment, child);
            }

            if (child is not JsonObject childObject)
                throw Invalid("Reviewed settings overlap an incompatible JSON path.");
            current = childObject;
        }

        var leafName = segments[^1];
        if (current.ContainsKey(leafName))
            throw Duplicate("Reviewed setting paths overlap in the authored projection.");
        current.Add(leafName, JsonNode.Parse(value.GetRawText()));
    }

    private static ResourceProjection ReadResourceProjection(CshellsSource source, AppsettingsLayers appsettings)
    {
        var baseShellPersistence = GetObjectPath(source.BaseConfiguration, "Elsa", "Persistence");
        var overlayShellPersistence = GetObjectPath(source.OverlayConfiguration, "Elsa", "Persistence");
        var baseAppPersistence = GetObjectPath(appsettings.BaseRoot, "Elsa", "Persistence");
        var overlayAppPersistence = GetObjectPath(appsettings.OverlayRoot, "Elsa", "Persistence");

        var resourceNames = ReadResourceNames(baseAppPersistence, overlayAppPersistence);
        var rootDefault = ReadLayeredString(
            baseAppPersistence,
            overlayAppPersistence,
            "DefaultResource",
            "appsettings-base",
            "appsettings-overlay",
            isLogicalResourceName: true);
        var shellDefault = ReadLayeredString(
            baseShellPersistence,
            overlayShellPersistence,
            "DefaultResource",
            "shell-configuration-base",
            "shell-configuration-overlay",
            isLogicalResourceName: true);
        var effectiveDefault = shellDefault ?? rootDefault;

        var baseBindings = ReadBindings(baseShellPersistence, "shell-configuration-base");
        var overlayBindings = ReadBindings(overlayShellPersistence, "shell-configuration-overlay");
        var effectiveBindings = MergeBindings(baseBindings, overlayBindings);

        var unresolved = ImmutableArray.CreateBuilder<string>();
        if (HasProperty(baseAppPersistence, "Bindings") || HasProperty(overlayAppPersistence, "Bindings"))
            unresolved.Add("root-resource-bindings-unsupported");
        if (HasProperty(baseShellPersistence, "Resources") || HasProperty(overlayShellPersistence, "Resources"))
            unresolved.Add("shell-resource-definitions-unsupported");

        if (effectiveDefault is not null)
            RequireResourceExists(resourceNames, effectiveDefault.Name);
        foreach (var binding in effectiveBindings.Values)
            RequireResourceExists(resourceNames, binding.ResourceName);

        var bindings = effectiveBindings.Values
            .OrderBy(item => item.FeatureId, StringComparer.Ordinal)
            .Select(item => new CompositionImportResourceBinding(item.FeatureId, item.ResourceName, item.Source))
            .ToImmutableArray();

        var resourceReferences = (effectiveDefault is null
                ? Enumerable.Empty<string>()
                : [effectiveDefault.Name])
            .Concat(effectiveBindings.Values.Select(item => item.ResourceName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToImmutableArray();

        var resources = BuildAuthoredResources(effectiveDefault?.Name, bindings);
        return new ResourceProjection(effectiveDefault, bindings, resources, resourceReferences, unresolved.ToImmutable());
    }

    private static JsonObject? BuildAuthoredResources(
        string? defaultResource,
        ImmutableArray<CompositionImportResourceBinding> bindings)
    {
        if (defaultResource is null && bindings.IsEmpty)
            return null;

        var persistence = new JsonObject();
        if (defaultResource is not null)
            persistence.Add("defaultResource", defaultResource);
        if (!bindings.IsEmpty)
        {
            var bindingMap = new JsonObject();
            foreach (var binding in bindings)
                bindingMap.Add(binding.FeatureId, binding.ResourceName);
            persistence.Add("bindings", bindingMap);
        }

        return new JsonObject { ["persistence"] = persistence };
    }

    private static HashSet<string> ReadResourceNames(JsonElement? basePersistence, JsonElement? overlayPersistence)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddResourceNames(basePersistence, result);
        AddResourceNames(overlayPersistence, result);
        return result;
    }

    private static void AddResourceNames(JsonElement? persistence, HashSet<string> names)
    {
        if (!TryGetProperty(persistence, "Resources", out var resources))
            return;
        if (resources.ValueKind != JsonValueKind.Object)
            throw Invalid("Persistence resource definitions must be an object.");

        var currentLayerNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in resources.EnumerateObject())
        {
            if (!IsLogicalResourceName(property.Name) || property.Value.ValueKind != JsonValueKind.Object)
                throw Invalid("A persistence resource definition must have a safe logical name and object value.");
            if (!currentLayerNames.Add(property.Name))
                throw Duplicate("Persistence resource names that differ only by case are ambiguous.");

            var existing = names.FirstOrDefault(name => string.Equals(name, property.Name, StringComparison.OrdinalIgnoreCase));
            if (existing is not null && !string.Equals(existing, property.Name, StringComparison.Ordinal))
                throw Duplicate("Persistence resource names that differ only by case are ambiguous across layers.");
            names.Add(property.Name);
        }
    }

    private static ResourceReference? ReadLayeredString(
        JsonElement? baseSection,
        JsonElement? overlaySection,
        string propertyName,
        string baseSource,
        string overlaySource,
        bool isLogicalResourceName)
    {
        if (TryGetProperty(overlaySection, propertyName, out var overlayValue))
            return ReadReference(overlayValue, overlaySource, isLogicalResourceName);
        if (TryGetProperty(baseSection, propertyName, out var baseValue))
            return ReadReference(baseValue, baseSource, isLogicalResourceName);
        return null;
    }

    private static ResourceReference ReadReference(JsonElement value, string source, bool isLogicalResourceName)
    {
        if (value.ValueKind != JsonValueKind.String)
            throw Invalid("A persistence resource reference must be a string.");
        var name = value.GetString()!;
        if (!isLogicalResourceName || !IsLogicalResourceName(name))
            throw Unsafe("A persistence selection must use a logical resource name, not a provider or connection value.");
        return new ResourceReference(name, source);
    }

    private static Dictionary<string, ResourceBinding> ReadBindings(JsonElement? persistence, string source)
    {
        var result = new Dictionary<string, ResourceBinding>(StringComparer.OrdinalIgnoreCase);
        if (!TryGetProperty(persistence, "Bindings", out var bindings))
            return result;
        if (bindings.ValueKind != JsonValueKind.Object)
            throw Invalid("Persistence feature bindings must be an object.");

        var currentLayerIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in bindings.EnumerateObject())
        {
            if (!SelectionValueRules.IsSafeReference(property.Name))
                throw Invalid("A persistence binding feature ID must be a safe identity label.");
            if (!currentLayerIds.Add(property.Name))
                throw Duplicate("Persistence binding feature IDs that differ only by case are ambiguous.");
            var reference = ReadReference(property.Value, source, isLogicalResourceName: true);
            result.Add(property.Name, new ResourceBinding(property.Name, reference.Name, source));
        }
        return result;
    }

    private static Dictionary<string, ResourceBinding> MergeBindings(
        Dictionary<string, ResourceBinding> baseBindings,
        Dictionary<string, ResourceBinding> overlayBindings)
    {
        var result = new Dictionary<string, ResourceBinding>(baseBindings, StringComparer.OrdinalIgnoreCase);
        foreach (var (featureId, binding) in overlayBindings)
        {
            if (result.TryGetValue(featureId, out var previous) &&
                !string.Equals(previous.FeatureId, featureId, StringComparison.Ordinal))
                throw Duplicate("Persistence binding feature IDs that differ only by case are ambiguous across layers.");
            result[featureId] = binding;
        }
        return result;
    }

    private static void RequireResourceExists(IReadOnlySet<string> resourceNames, string resourceName)
    {
        if (!resourceNames.Contains(resourceName))
            throw new CompositionImportException("bridge-mapping-unresolved", "A selected logical persistence resource is not defined in the selected appsettings files.");
    }

    private static AppsettingsLayers ReadAppsettings(string baseJson, string? overlayJson)
    {
        try
        {
            using var baseDocument = SelectionJsonReader.ParseStrict(baseJson);
            var baseRoot = RequireObject(baseDocument.RootElement, "The base appsettings document must be a JSON object.").Clone();
            if (overlayJson is null)
                return new AppsettingsLayers(baseRoot, null);

            using var overlayDocument = SelectionJsonReader.ParseStrict(overlayJson);
            var overlayRoot = RequireObject(overlayDocument.RootElement, "The selected appsettings overlay must be a JSON object.").Clone();
            return new AppsettingsLayers(baseRoot, overlayRoot);
        }
        catch (SelectionDocumentException exception)
        {
            var code = exception.Code == "duplicate-json-key" ? "bridge-source-duplicate" : "bridge-source-invalid";
            throw new CompositionImportException(code, "A selected appsettings document is malformed or contains duplicate JSON properties.");
        }
        catch (JsonException)
        {
            throw Invalid("A selected appsettings document is malformed.");
        }
    }

    private static JsonElement? GetObjectPath(JsonElement? root, params string[] path)
    {
        if (root is null)
            return null;
        var current = RequireObject(root.Value, "A selected configuration root must be a JSON object.");
        foreach (var segment in path)
        {
            if (!TryGetProperty(current, segment, out var child))
                return null;
            current = RequireObject(child, "A selected persistence configuration section must be a JSON object.");
        }
        return current;
    }

    private static bool TryGetProperty(JsonElement? parent, string name, out JsonElement value)
    {
        if (parent is null)
        {
            value = default;
            return false;
        }
        return TryGetProperty(parent.Value, name, out value);
    }

    private static bool TryGetProperty(JsonElement parent, string name, out JsonElement value)
    {
        if (parent.ValueKind != JsonValueKind.Object)
            throw Invalid("A selected configuration section must be a JSON object.");

        var matches = parent.EnumerateObject()
            .Where(property => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matches.Length > 1)
            throw Duplicate("Selected configuration keys that differ only by case are ambiguous.");
        value = matches.Length == 0 ? default : matches[0].Value;
        return matches.Length > 0;
    }

    private static bool HasProperty(JsonElement? parent, string name) => TryGetProperty(parent, name, out _);

    private static ImmutableArray<string> DecodeSourcePointer(string pointer)
    {
        if (pointer.Length == 0)
            return [];
        if (pointer[0] != '/')
            throw Invalid("A source setting path is not a valid JSON pointer.");

        var result = ImmutableArray.CreateBuilder<string>();
        foreach (var encoded in pointer[1..].Split('/', StringSplitOptions.None))
        {
            var segment = new System.Text.StringBuilder(encoded.Length);
            for (var index = 0; index < encoded.Length; index++)
            {
                if (encoded[index] != '~')
                {
                    segment.Append(encoded[index]);
                    continue;
                }
                if (++index >= encoded.Length || encoded[index] is not ('0' or '1'))
                    throw Invalid("A source setting path is not a valid JSON pointer.");
                segment.Append(encoded[index] == '0' ? '~' : '/');
            }
            result.Add(segment.ToString());
        }
        return result.ToImmutable();
    }

    private static ImmutableArray<string> Append(ImmutableArray<string> path, string segment) => path.Add(segment);

    private static string EncodePointer(ImmutableArray<string> segments) => string.Concat(segments.Select(segment => "/" + EscapePointerSegment(segment)));

    private static string EscapePointerSegment(string segment) => segment.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);

    private static string TypeName(JsonValueKind valueKind) => valueKind switch
    {
        JsonValueKind.True or JsonValueKind.False => "boolean",
        JsonValueKind.Number => "number",
        JsonValueKind.String => "string",
        JsonValueKind.Null => "null",
        JsonValueKind.Object => "object",
        JsonValueKind.Array => "array",
        _ => "unknown"
    };

    private static bool IsHostOwnedPersistenceField(ImmutableArray<string> segments) =>
        segments.Any(segment => segment.Equals("ConnectionStrings", StringComparison.OrdinalIgnoreCase)) ||
        segments.Length >= 2 &&
        segments[0].Equals("Elsa", StringComparison.OrdinalIgnoreCase) &&
        segments[1].Equals("Persistence", StringComparison.OrdinalIgnoreCase) ||
        segments.Length > 0 && segments[^1] is { } last &&
        (last.Equals("Provider", StringComparison.OrdinalIgnoreCase) ||
         last.Equals("ConnectionString", StringComparison.OrdinalIgnoreCase) ||
         last.Equals("ConnectionName", StringComparison.OrdinalIgnoreCase));

    private static bool IsLogicalResourceName(string name) =>
        SelectionValueRules.IsSafeReference(name) &&
        name.Length > 0 &&
        (char.IsLetter(name[0]) || name[0] == '_') &&
        !name.Equals("true", StringComparison.OrdinalIgnoreCase) &&
        !name.Equals("false", StringComparison.OrdinalIgnoreCase) &&
        !name.Equals("null", StringComparison.OrdinalIgnoreCase) &&
        name.All(character => char.IsLetterOrDigit(character) || character is '_' or '-' or '.');

    private static JsonElement? ToJsonElement(JsonObject? value)
    {
        if (value is null)
            return null;
        using var document = JsonDocument.Parse(value.ToJsonString());
        return document.RootElement.Clone();
    }

    private static JsonElement RequireObject(JsonElement value, string message) =>
        value.ValueKind == JsonValueKind.Object ? value : throw Invalid(message);

    private static CompositionImportException Invalid(string message) => new("bridge-source-invalid", message);

    private static CompositionImportException Duplicate(string message) => new("bridge-source-duplicate", message);

    private static CompositionImportException Unsafe(string message) => new("bridge-portable-unsafe", message);

    private sealed record ExpandedSetting(
        string FeatureId,
        string Pointer,
        ImmutableArray<string> Segments,
        JsonElement Value,
        CshellsSourceLayer SourceLayer,
        bool Portable,
        CompositionImportSetting Preview);

    private sealed record ResourceReference(string Name, string Source);

    private sealed record ResourceBinding(string FeatureId, string ResourceName, string Source);

    private sealed record ResourceProjection(
        ResourceReference? DefaultResource,
        ImmutableArray<CompositionImportResourceBinding> Bindings,
        JsonObject? AuthoredResources,
        ImmutableArray<string> ResourceReferences,
        ImmutableArray<string> UnresolvedCodes);

    private sealed record AppsettingsLayers(JsonElement BaseRoot, JsonElement? OverlayRoot);
}
