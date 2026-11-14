using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Workflows.Publishing.Releases.Core;

namespace Elsa.Workflows.Publishing.Releases;

/// <summary>Common closed-schema and declared-graph checks; deliberately does not interpret owner payloads.</summary>
internal static class ReleaseManifest
{
    internal const string Profile = "native-bpmn-etl/1";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static (JsonElement Manifest, ReleaseBundleMember[] Members) Parse(byte[] bytes, ReleaseBundleLimits limits)
    {
        CheckJson(bytes, limits.MaxJsonDepth);
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = limits.MaxJsonDepth });
        var root = document.RootElement;
        Fields(root, "format", "formatVersion", "profile", "roots", "members", "edges", "prerequisites", "resourceRequirements");
        if (Text(root, "format") != "elsa.workflow-release" || Integer(root, "formatVersion") != 1 || Text(root, "profile") != Profile)
            throw Refuse("unsupported_format_or_profile");
        var members = Items(root, "members", limits.MaxMembers);
        if (members.Length == 0) throw Refuse("empty_members");
        var descriptors = new Dictionary<Key, ReleaseBundleMember>();
        foreach (var item in members)
        {
            Fields(item, "key", "codec", "codecVersion", "ownerVersion", "payloadDigest", "byteLength");
            var key = ReadKey(item.GetProperty("key"));
            var codec = Text(item, "codec");
            var version = Integer(item, "codecVersion");
            var size = Integer(item, "byteLength");
            if (version <= 0 || version > int.MaxValue || size <= 0 || size > limits.MaxBlobBytes) throw Refuse("invalid_member_bounds");
            var digest = Text(item, "payloadDigest");
            CheckDigest(digest);
            CheckVersion(item.GetProperty("ownerVersion"));
            if (!descriptors.TryAdd(key, new(key.Kind, key.Identity, codec, (int)version, digest, size))) throw Refuse("duplicate_member_key");
        }
        var roots = Items(root, "roots", limits.MaxMembers);
        if (roots.Length == 0) throw Refuse("empty_roots");
        var selected = new HashSet<Key>();
        var reachableRoots = new List<Key>();
        foreach (var item in roots)
        {
            Fields(item, "member", "content", "action");
            var member = ReadKey(item.GetProperty("member"));
            var content = ReadKey(item.GetProperty("content"));
            if (Text(item, "action") is not ("activate-serving" or "make-available")) throw Refuse("unsupported_root_action");
            if (!selected.Add(member)) throw Refuse("duplicate_root");
            reachableRoots.Add(member); reachableRoots.Add(content);
        }
        var edgeItems = Items(root, "edges", limits.MaxEdges);
        var edges = new HashSet<Edge>();
        foreach (var item in edgeItems)
        {
            Fields(item, "from", "to", "relation", "occurrence");
            var relation = Text(item, "relation");
            if (relation is not ("dependency" or "association")) throw Refuse("unsupported_relation");
            var edge = new Edge(ReadKey(item.GetProperty("from")), ReadKey(item.GetProperty("to")), relation, Text(item, "occurrence", allowEmpty: true));
            if (!edges.Add(edge)) throw Refuse("duplicate_edge");
        }
        CheckGraph(descriptors.Keys.ToHashSet(), reachableRoots, edges, limits.MaxDependencyDepth);

        var prerequisites = Items(root, "prerequisites", limits.MaxEdges);
        var prerequisiteKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in prerequisites)
        {
            Fields(item, "key", "member", "kind", "identity", "scheme", "constraint");
            if (!prerequisiteKeys.Add(Text(item, "key"))) throw Refuse("duplicate_prerequisite");
            RequireMember(ReadKey(item.GetProperty("member")), descriptors);
            _ = Text(item, "kind"); _ = Text(item, "identity"); _ = Text(item, "constraint");
            if (Text(item, "scheme") is not ("exact" or "semver-range" or "integer-range" or "opaque")) throw Refuse("unsupported_constraint_scheme");
        }
        var requirements = Items(root, "resourceRequirements", limits.MaxEdges);
        var requirementKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in requirements)
        {
            Fields(item, "key", "schemaVersion");
            var key = item.GetProperty("key");
            Fields(key, "member", "origin", "reference", "kind");
            RequireMember(ReadKey(key.GetProperty("member")), descriptors);
            var origins = Items(key, "origin", limits.MaxDependencyDepth);
            foreach (var origin in origins) _ = String(origin);
            _ = Text(key, "reference"); _ = Text(key, "kind"); _ = Text(item, "schemaVersion");
            if (!requirementKeys.Add(CanonicalText(key))) throw Refuse("duplicate_resource_requirement");
        }

        // Only manifest set arrays are normalized. Owner blobs are never passed through this writer.
        var normalized = JsonSerializer.SerializeToElement(new
        {
            format = "elsa.workflow-release", formatVersion = 1, profile = Profile,
            roots = roots.OrderBy(x => ReadKey(x.GetProperty("member")), KeyComparer).ToArray(),
            members = members.OrderBy(x => ReadKey(x.GetProperty("key")), KeyComparer).ToArray(),
            edges = edgeItems.OrderBy(x => ReadKey(x.GetProperty("from")), KeyComparer)
                .ThenBy(x => Text(x, "relation"), StringComparer.Ordinal).ThenBy(x => Text(x, "occurrence", true), StringComparer.Ordinal)
                .ThenBy(x => ReadKey(x.GetProperty("to")), KeyComparer).ToArray(),
            prerequisites = prerequisites.OrderBy(x => Text(x, "key"), StringComparer.Ordinal).ToArray(),
            resourceRequirements = requirements.OrderBy(x => x.GetProperty("key"), Comparer<JsonElement>.Create(CompareRequirementKeys)).ToArray()
        });
        return (normalized, descriptors.OrderBy(x => x.Key, KeyComparer).Select(x => x.Value).ToArray());
    }

    private static void CheckVersion(JsonElement version)
    {
        if (version.ValueKind == JsonValueKind.Null) return;
        Fields(version, "scheme", "value");
        if (Text(version, "scheme") == "integer")
        {
            if (Integer(version, "value") <= 0) throw Refuse("invalid_owner_version");
        }
        else if (Text(version, "scheme") is "semver" or "opaque") _ = Text(version, "value");
        else throw Refuse("unsupported_version_scheme");
    }

    private static void CheckGraph(HashSet<Key> members, List<Key> roots, HashSet<Edge> edges, int maxDepth)
    {
        foreach (var key in roots.Concat(edges.SelectMany(x => new[] { x.From, x.To })))
            if (!members.Contains(key)) throw Refuse("missing_member");
        var all = edges.GroupBy(x => x.From).ToDictionary(x => x.Key, x => x.Select(e => e.To).ToArray());
        var pending = new Stack<Key>(roots);
        var reachable = new HashSet<Key>();
        while (pending.TryPop(out var key))
        {
            if (!reachable.Add(key)) continue;
            if (all.TryGetValue(key, out var children)) foreach (var child in children) pending.Push(child);
        }
        if (reachable.Count != members.Count) throw Refuse("unrelated_member");
        var dependencies = edges.Where(x => x.Relation == "dependency").GroupBy(x => x.From)
            .ToDictionary(x => x.Key, x => x.Select(e => e.To).ToArray());
        var active = new HashSet<Key>();
        var heights = new Dictionary<Key, int>();
        foreach (var key in members) _ = Height(key, 0);
        return;

        int Height(Key key, int depth)
        {
            if (depth > maxDepth) throw Refuse("dependency_depth_exceeded");
            if (active.Contains(key)) throw Refuse("dependency_cycle");
            if (heights.TryGetValue(key, out var height)) return height;
            active.Add(key);
            height = dependencies.TryGetValue(key, out var children) && children.Length != 0 ? 1 + children.Max(child => Height(child, depth + 1)) : 0;
            active.Remove(key);
            if (height > maxDepth) throw Refuse("dependency_depth_exceeded");
            heights.Add(key, height);
            return height;
        }
    }

    internal static void CheckJson(byte[] bytes, int maxDepth)
    {
        try
        {
            _ = StrictUtf8.GetCharCount(bytes);
            if (bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) throw Refuse("utf8_bom");
            var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = maxDepth, CommentHandling = JsonCommentHandling.Disallow });
            var scopes = new Stack<HashSet<string>? >();
            var any = false;
            while (reader.Read())
            {
                any = true;
                if (reader.TokenType == JsonTokenType.StartObject) scopes.Push(new(StringComparer.Ordinal));
                else if (reader.TokenType == JsonTokenType.StartArray) scopes.Push(null);
                else if (reader.TokenType is JsonTokenType.EndObject or JsonTokenType.EndArray) scopes.Pop();
                else if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    var property = reader.GetString()!; CheckUnicode(property);
                    if (!scopes.Peek()!.Add(property)) throw Refuse("duplicate_json_property");
                }
                else if (reader.TokenType == JsonTokenType.String) CheckUnicode(reader.GetString()!);
            }
            if (!any) throw Refuse("invalid_json");
        }
        catch (Exception exception) when (exception is JsonException or DecoderFallbackException or InvalidOperationException)
        {
            throw Refuse("invalid_json");
        }
    }

    internal static string Hash(ReadOnlySpan<byte> bytes) => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes));
    internal static string HashManifest(JsonElement element) => Hash(Encoding.UTF8.GetBytes(CanonicalText(element)));
    internal static string CanonicalText(JsonElement element)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) WriteCanonical(writer, element);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    // Identical recursive recipe to ExecutableActivityTemplateBehaviorHasher: ordinal object properties,
    // ordered arrays and JsonElement.WriteTo for scalars. Kept private to avoid a Design-bearing dependency.
    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (var property in element.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal))
            { writer.WritePropertyName(property.Name); WriteCanonical(writer, property.Value); }
            writer.WriteEndObject();
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray(); foreach (var value in element.EnumerateArray()) WriteCanonical(writer, value); writer.WriteEndArray();
        }
        else element.WriteTo(writer);
    }

    internal static void CheckDigest(string digest)
    {
        if (digest.Length != 71 || !digest.StartsWith("sha256:", StringComparison.Ordinal) || digest.AsSpan(7).ContainsAnyExcept("0123456789abcdef")) throw Refuse("invalid_digest");
    }
    private static void RequireMember(Key key, Dictionary<Key, ReleaseBundleMember> members)
    { if (!members.ContainsKey(key)) throw Refuse("missing_member"); }
    private static readonly IComparer<Key> KeyComparer = Comparer<Key>.Create((left, right) =>
    {
        var kind = StringComparer.Ordinal.Compare(left.Kind, right.Kind);
        return kind != 0 ? kind : StringComparer.Ordinal.Compare(left.Identity, right.Identity);
    });
    private static int CompareRequirementKeys(JsonElement left, JsonElement right)
    {
        var member = KeyComparer.Compare(ReadKey(left.GetProperty("member")), ReadKey(right.GetProperty("member")));
        if (member != 0) return member;
        var a = left.GetProperty("origin").EnumerateArray().Select(x => String(x)).ToArray();
        var b = right.GetProperty("origin").EnumerateArray().Select(x => String(x)).ToArray();
        for (var index = 0; index < Math.Min(a.Length, b.Length); index++)
        {
            var origin = StringComparer.Ordinal.Compare(a[index], b[index]);
            if (origin != 0) return origin;
        }
        if (a.Length != b.Length) return a.Length.CompareTo(b.Length);
        var reference = StringComparer.Ordinal.Compare(Text(left, "reference"), Text(right, "reference"));
        return reference != 0 ? reference : StringComparer.Ordinal.Compare(Text(left, "kind"), Text(right, "kind"));
    }
    private static Key ReadKey(JsonElement element)
    { Fields(element, "kind", "identity"); return new(Text(element, "kind"), Text(element, "identity")); }
    private static long Integer(JsonElement obj, string property)
    {
        var element = obj.GetProperty(property);
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt64(out var value) || element.GetRawText() != value.ToString(CultureInfo.InvariantCulture)) throw Refuse("invalid_integer");
        return value;
    }
    private static string Text(JsonElement obj, string property, bool allowEmpty = false) => String(obj.GetProperty(property), allowEmpty);
    private static string String(JsonElement value, bool allowEmpty = false)
    {
        try
        {
            if (value.ValueKind != JsonValueKind.String || value.GetString() is not { } text || !allowEmpty && text.Length == 0) throw Refuse("invalid_string");
            CheckUnicode(text);
            return text;
        }
        catch (InvalidOperationException) { throw Refuse("invalid_json"); }
    }
    private static void CheckUnicode(string text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            if (char.IsHighSurrogate(text[index]))
            {
                if (++index >= text.Length || !char.IsLowSurrogate(text[index])) throw Refuse("invalid_json");
            }
            else if (char.IsLowSurrogate(text[index])) throw Refuse("invalid_json");
        }
    }
    private static JsonElement[] Items(JsonElement obj, string property, int maximum)
    {
        var value = obj.GetProperty(property);
        if (value.ValueKind != JsonValueKind.Array) throw Refuse("invalid_array");
        if (value.GetArrayLength() > maximum) throw Refuse("item_limit_exceeded");
        return value.EnumerateArray().ToArray();
    }
    private static void Fields(JsonElement element, params string[] expected)
    {
        if (element.ValueKind != JsonValueKind.Object) throw Refuse("invalid_object");
        var actual = element.EnumerateObject().Select(x => x.Name).ToHashSet(StringComparer.Ordinal);
        if (!actual.SetEquals(expected)) throw Refuse("unknown_or_missing_field");
    }
    internal static ReleaseBundleFormatException Refuse(string code) => new(code);
    private readonly record struct Key(string Kind, string Identity);
    private sealed record Edge(Key From, Key To, string Relation, string Occurrence);
}
