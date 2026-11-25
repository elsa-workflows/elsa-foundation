using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Serialization.Core;

// The carried value, including its whitespace and number/escape spellings, is the preservation boundary.
internal sealed class RawJsonElementConverter : JsonConverter<JsonElement>
{
    public override JsonElement Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        JsonElement.ParseValue(ref reader);

    public override void Write(Utf8JsonWriter writer, JsonElement value, JsonSerializerOptions options) =>
        WriteRaw(writer, value, options);

    internal static void WriteRaw(Utf8JsonWriter writer, JsonElement value, JsonSerializerOptions options)
    {
        // WriteRawValue does not visit the carried containers. Its optional validation also
        // treats the fragment as a root, ignoring the enclosing writer's depth. Check the DOM
        // before writing, then retain its exact tokens without a second parse or re-encoding.
        if (IsContainer(value))
        {
            var serializerLimit = options.MaxDepth == 0 ? 64 : options.MaxDepth;
            var writerLimit = writer.Options.MaxDepth == 0 ? 1000 : writer.Options.MaxDepth;
            var limit = Math.Min(serializerLimit, writerLimit);
            ValidateDepth(value, limit - writer.CurrentDepth, limit, writer.CurrentDepth);
        }
        writer.WriteRawValue(value.GetRawText(), skipInputValidation: true);
    }

    private static void ValidateDepth(JsonElement value, int remaining, int limit, int enclosingDepth)
    {
        if (remaining < 1) throw DepthExceeded(limit, enclosingDepth);
        var current = new ContainerEnumerator(value);
        Stack<ContainerEnumerator>? parents = null;
        var depth = 1;
        while (true)
        {
            if (current.MoveNext(out var child))
            {
                if (!IsContainer(child)) continue;
                if (depth >= remaining) throw DepthExceeded(limit, enclosingDepth);
                (parents ??= new()).Push(current);
                current = new(child);
                depth++;
            }
            else
            {
                if (parents is null || !parents.TryPop(out current)) return;
                depth--;
            }
        }
    }

    private static bool IsContainer(JsonElement value) => value.ValueKind is JsonValueKind.Object or JsonValueKind.Array;

    private static JsonConversionException DepthExceeded(int limit, int enclosingDepth) =>
        new("json-depth-exceeded", $"The JSON value exceeds the maximum depth of {limit} when written at depth {enclosingDepth}.", "$");

    // Keep only the current ancestry, not every sibling; wide JSON needs no width-sized stack.
    // Flat carried objects/arrays do not allocate a stack at all.
    private struct ContainerEnumerator
    {
        private readonly bool _isObject;
        private JsonElement.ObjectEnumerator _properties;
        private JsonElement.ArrayEnumerator _items;

        public ContainerEnumerator(JsonElement value)
        {
            _isObject = value.ValueKind == JsonValueKind.Object;
            _properties = _isObject ? value.EnumerateObject() : default;
            _items = _isObject ? default : value.EnumerateArray();
        }

        public bool MoveNext(out JsonElement value)
        {
            var found = _isObject ? _properties.MoveNext() : _items.MoveNext();
            value = found ? (_isObject ? _properties.Current.Value : _items.Current) : default;
            return found;
        }
    }
}
