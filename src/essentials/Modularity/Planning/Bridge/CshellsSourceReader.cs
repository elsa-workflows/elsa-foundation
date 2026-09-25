using System.Collections.Immutable;
using System.Text.Json;
using Elsa.Modularity.Planning.Json;

namespace Elsa.Modularity.Planning.Bridge;

public enum CshellsFeatureShape
{
    ObjectMap,
    Array
}

public enum CshellsSourceLayer
{
    Base,
    Overlay
}

public sealed record CshellsSettingLeaf(
    string FeatureId,
    string Pointer,
    JsonElement Value,
    CshellsSourceLayer SourceLayer);

/// <summary>The selected shell's exact feature state and raw configuration layers.</summary>
public sealed record CshellsSource(
    CshellsFeatureShape FeatureShape,
    ImmutableArray<string> EnabledFeatureIds,
    ImmutableArray<string> DisabledFeatureIds,
    ImmutableArray<CshellsSettingLeaf> SettingLeaves,
    JsonElement? BaseConfiguration,
    JsonElement? OverlayConfiguration);

public sealed class CshellsSourceException : Exception
{
    public CshellsSourceException(string code) : base(MessageFor(code)) => Code = code;

    public string Code { get; }

    private static string MessageFor(string code) => code switch
    {
        "bridge-source-duplicate" => "The selected shell source contains duplicate identities.",
        _ => "The selected shell source has an unsupported or invalid shape."
    };
}

/// <summary>Reads raw CShells JSON layers without loading CShells or a host.</summary>
public static class CshellsSourceReader
{
    public static CshellsSource Read(string baseJson, string? overlayJson, string shellId)
    {
        if (!SelectionValueRules.IsSafeReference(shellId))
            throw Invalid();

        using var baseDocument = Parse(baseJson);
        using var overlayDocument = overlayJson is null ? null : Parse(overlayJson);
        ValidateNoDuplicateJsonProperties(baseDocument.RootElement);
        if (overlayDocument is not null)
            ValidateNoDuplicateJsonProperties(overlayDocument.RootElement);

        var baseShell = FindShell(baseDocument.RootElement, shellId);
        var overlayShell = overlayDocument is null ? default : FindOptionalShell(overlayDocument.RootElement, shellId);
        var baseFeatures = ReadFeatures(baseShell);
        var overlayFeatures = overlayShell.ValueKind == JsonValueKind.Undefined ? null : ReadOptionalFeatures(overlayShell);

        if (overlayFeatures is not null && overlayFeatures.Shape != baseFeatures.Shape)
            throw Invalid();

        return baseFeatures.Shape switch
        {
            CshellsFeatureShape.ObjectMap => ReadObjectMap(baseFeatures, overlayFeatures, baseShell, overlayShell),
            CshellsFeatureShape.Array => ReadArray(baseFeatures, overlayFeatures, baseShell, overlayShell),
            _ => throw Invalid()
        };
    }

    private static CshellsSource ReadObjectMap(
        FeatureLayer baseLayer,
        FeatureLayer? overlayLayer,
        JsonElement baseShell,
        JsonElement overlayShell)
    {
        var baseById = ReadObjectMapEntries(baseLayer.Features);
        var overlayById = overlayLayer is null ? new Dictionary<string, JsonElement>(StringComparer.Ordinal) : ReadObjectMapEntries(overlayLayer.Features);
        var effective = new Dictionary<string, FeatureState>(StringComparer.Ordinal);

        foreach (var (id, value) in baseById)
            effective.Add(id, new FeatureState(value, MergeObjectSettings(value, null)));

        foreach (var (id, value) in overlayById)
        {
            var previous = FindCaseEquivalent(effective, id);
            if (previous is not null && !string.Equals(previous, id, StringComparison.Ordinal))
                throw Duplicate();
            if (previous is not null)
            {
                var baseValue = effective[previous].Value;
                // CShells accepts an overlay false as an environment-local disable for a base
                // feature object. The inverse (base false, overlay object) stays unsupported:
                // the base disable wins in the pinned package.
                if ((baseValue.ValueKind == JsonValueKind.Object) != (value.ValueKind == JsonValueKind.Object) &&
                    !(baseValue.ValueKind == JsonValueKind.Object && value.ValueKind == JsonValueKind.False))
                    throw Invalid();
            }

            var settings = MergeObjectSettings(
                previous is null ? default : effective[previous].Value,
                value);
            effective[id] = new FeatureState(value, settings);
        }

        var enabled = ImmutableArray.CreateBuilder<string>();
        var disabled = ImmutableArray.CreateBuilder<string>();
        var leaves = ImmutableArray.CreateBuilder<CshellsSettingLeaf>();
        foreach (var (id, state) in effective.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            var isDisabled = state.Value.ValueKind == JsonValueKind.False;
            (isDisabled ? disabled : enabled).Add(id);
            if (!isDisabled)
                leaves.AddRange(state.Settings.OrderBy(item => item.Pointer, StringComparer.Ordinal)
                    .Select(item => new CshellsSettingLeaf(id, item.Pointer, item.Value, item.Layer)));
        }

        return new CshellsSource(
            CshellsFeatureShape.ObjectMap,
            enabled.ToImmutable(),
            disabled.ToImmutable(),
            leaves.ToImmutable(),
            GetConfiguration(baseShell),
            GetOptionalConfiguration(overlayShell));
    }

    private static ImmutableArray<SettingEntry> MergeObjectSettings(JsonElement baseValue, JsonElement? overlayValue)
    {
        var values = new Dictionary<string, SettingEntry>(StringComparer.OrdinalIgnoreCase);
        if (baseValue.ValueKind == JsonValueKind.Object)
            FlattenSettings(baseValue, [], CshellsSourceLayer.Base, values);

        if (overlayValue is { ValueKind: JsonValueKind.Object } overlay)
        {
            var overlayValues = new Dictionary<string, SettingEntry>(StringComparer.OrdinalIgnoreCase);
            FlattenSettings(overlay, [], CshellsSourceLayer.Overlay, overlayValues);
            foreach (var (path, entry) in overlayValues)
            {
                var pointer = values.TryGetValue(path, out var prior) ? prior.Pointer : entry.Pointer;
                values[path] = entry with { Pointer = pointer };
            }
        }

        return values.Values
            .OrderBy(item => item.Pointer, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    private static void FlattenSettings(
        JsonElement value,
        IReadOnlyList<string> path,
        CshellsSourceLayer layer,
        IDictionary<string, SettingEntry> output)
    {
        if (value.ValueKind == JsonValueKind.Object && value.EnumerateObject().Any())
        {
            foreach (var property in value.EnumerateObject())
            {
                var childPath = new string[path.Count + 1];
                for (var i = 0; i < path.Count; i++)
                    childPath[i] = path[i];
                childPath[^1] = property.Name;
                FlattenSettings(property.Value, childPath, layer, output);
            }
            return;
        }

        var pointer = "";
        foreach (var segment in path)
            pointer += "/" + EscapePointerSegment(segment);
        // Empty objects and arrays are values too; preserving their exact JsonElement kind is deliberate.
        if (output.ContainsKey(pointer))
            throw Duplicate();
        var cloned = value.Clone();
        output[pointer] = new SettingEntry(pointer, cloned, layer);
    }

    private static CshellsSource ReadArray(
        FeatureLayer baseLayer,
        FeatureLayer? overlayLayer,
        JsonElement baseShell,
        JsonElement overlayShell)
    {
        var entries = baseLayer.Features.EnumerateArray()
            .Select(item => new ArrayFeatureEntry(item.Clone(), CshellsSourceLayer.Base))
            .ToList();
        if (overlayLayer is not null)
        {
            var index = 0;
            foreach (var item in overlayLayer.Features.EnumerateArray())
            {
                var entry = new ArrayFeatureEntry(item.Clone(), CshellsSourceLayer.Overlay);
                if (index < entries.Count)
                    entries[index] = entry;
                else
                    entries.Add(entry);
                index++;
            }
        }

        var parsed = entries.Select(ReadArrayEntry).ToArray();
        EnsureUniqueFeatureIds(parsed.Select(item => item.Id));
        var enabled = parsed.Select(item => item.Id).OrderBy(id => id, StringComparer.Ordinal).ToImmutableArray();
        var leaves = parsed
            .SelectMany(item => item.Settings.Select(setting => new CshellsSettingLeaf(item.Id, setting.Pointer, setting.Value, item.Layer)))
            .OrderBy(item => item.FeatureId, StringComparer.Ordinal)
            .ThenBy(item => item.Pointer, StringComparer.Ordinal)
            .ToImmutableArray();
        return new CshellsSource(
            CshellsFeatureShape.Array,
            enabled,
            [],
            leaves,
            GetConfiguration(baseShell),
            GetOptionalConfiguration(overlayShell));
    }

    private static ParsedArrayFeature ReadArrayEntry(ArrayFeatureEntry entry)
    {
        if (entry.Value.ValueKind == JsonValueKind.String)
        {
            var id = entry.Value.GetString();
            RequireSafeFeatureId(id);
            return new ParsedArrayFeature(id!, entry.Layer, []);
        }

        if (entry.Value.ValueKind != JsonValueKind.Object || !entry.Value.TryGetProperty("Name", out var name) || name.ValueKind != JsonValueKind.String)
            throw Invalid();
        var featureId = name.GetString();
        RequireSafeFeatureId(featureId);
        var settings = ImmutableArray.CreateBuilder<SettingEntry>();
        foreach (var property in entry.Value.EnumerateObject())
        {
            if (property.NameEquals("Name"))
                continue;
            var map = new Dictionary<string, SettingEntry>(StringComparer.Ordinal);
            FlattenSettings(property.Value, [property.Name], entry.Layer, map);
            settings.AddRange(map.Values);
        }
        return new ParsedArrayFeature(featureId!, entry.Layer, settings.ToImmutable());
    }

    private static FeatureLayer ReadFeatures(JsonElement shell)
    {
        if (!shell.TryGetProperty("Features", out var features))
            throw Invalid();
        return LayerFrom(features);
    }

    private static FeatureLayer? ReadOptionalFeatures(JsonElement shell) =>
        shell.TryGetProperty("Features", out var features) ? LayerFrom(features) : null;

    private static FeatureLayer LayerFrom(JsonElement features) => features.ValueKind switch
    {
        JsonValueKind.Object => new FeatureLayer(CshellsFeatureShape.ObjectMap, features),
        JsonValueKind.Array => new FeatureLayer(CshellsFeatureShape.Array, features),
        _ => throw Invalid()
    };

    private static Dictionary<string, JsonElement> ReadObjectMapEntries(JsonElement features)
    {
        var entries = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in features.EnumerateObject())
        {
            RequireSafeFeatureId(property.Name);
            if (!seen.Add(property.Name))
                throw Duplicate();
            entries.Add(property.Name, property.Value.Clone());
        }
        return entries;
    }

    private static JsonElement FindShell(JsonElement root, string shellId)
    {
        var shell = FindOptionalShell(root, shellId);
        return shell.ValueKind == JsonValueKind.Undefined ? throw Invalid() : shell;
    }

    private static JsonElement FindOptionalShell(JsonElement root, string shellId)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("CShells", out var cshells) ||
            cshells.ValueKind != JsonValueKind.Object || !cshells.TryGetProperty("Shells", out var shells) ||
            shells.ValueKind != JsonValueKind.Object || !shells.TryGetProperty(shellId, out var shell) ||
            shell.ValueKind != JsonValueKind.Object)
            return default;
        return shell;
    }

    private static JsonElement? GetConfiguration(JsonElement shell)
    {
        if (!shell.TryGetProperty("Configuration", out var configuration))
            return null;
        if (configuration.ValueKind != JsonValueKind.Object)
            throw Invalid();
        return configuration.Clone();
    }

    private static JsonElement? GetOptionalConfiguration(JsonElement shell)
    {
        if (shell.ValueKind == JsonValueKind.Undefined || !shell.TryGetProperty("Configuration", out var configuration))
            return null;
        if (configuration.ValueKind != JsonValueKind.Object)
            throw Invalid();
        return configuration.Clone();
    }

    private static JsonDocument Parse(string? json)
    {
        if (json is null)
            throw Invalid();
        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            throw Invalid();
        }
    }

    private static void ValidateNoDuplicateJsonProperties(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw Duplicate();
                ValidateNoDuplicateJsonProperties(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray())
                ValidateNoDuplicateJsonProperties(item);
    }

    private static string? FindCaseEquivalent<TValue>(IReadOnlyDictionary<string, TValue> values, string id)
    {
        foreach (var key in values.Keys)
            if (string.Equals(key, id, StringComparison.OrdinalIgnoreCase))
                return key;
        return null;
    }

    private static void EnsureUniqueFeatureIds(IEnumerable<string> ids)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in ids)
        {
            RequireSafeFeatureId(id);
            if (!seen.Add(id))
                throw Duplicate();
        }
    }

    private static void RequireSafeFeatureId(string? featureId)
    {
        if (!SelectionValueRules.IsSafeReference(featureId))
            throw Invalid();
    }

    private static string EscapePointerSegment(string segment) => segment.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);

    private static CshellsSourceException Invalid() => new("bridge-source-invalid");

    private static CshellsSourceException Duplicate() => new("bridge-source-duplicate");

    private sealed record FeatureLayer(CshellsFeatureShape Shape, JsonElement Features);
    private sealed record FeatureState(JsonElement Value, ImmutableArray<SettingEntry> Settings);
    private sealed record SettingEntry(string Pointer, JsonElement Value, CshellsSourceLayer Layer);
    private sealed record ArrayFeatureEntry(JsonElement Value, CshellsSourceLayer Layer);
    private sealed record ParsedArrayFeature(string Id, CshellsSourceLayer Layer, ImmutableArray<SettingEntry> Settings);
}
