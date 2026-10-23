using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Elsa.Etl.Runtime.Http;

/// <summary>One RFC 6901 pointer compiled from a bounded artifact declaration.</summary>
internal sealed class EtlJsonPointer
{
    private readonly string[] _segments;

    public EtlJsonPointer(string pointer)
    {
        ArgumentNullException.ThrowIfNull(pointer);
        if (pointer.Length == 0)
        {
            _segments = [];
            return;
        }
        if (pointer[0] != '/')
            throw new ArgumentException("The JSON Pointer is invalid.", nameof(pointer));
        _segments = pointer[1..].Split('/').Select(Decode).ToArray();
    }

    public bool TryResolve(JsonElement document, out JsonElement value)
    {
        value = document;
        foreach (var segment in _segments)
        {
            if (value.ValueKind == JsonValueKind.Object)
            {
                if (!value.TryGetProperty(segment, out value))
                    return false;
            }
            else if (value.ValueKind == JsonValueKind.Array)
            {
                if (segment.Length == 0 || segment.Length > 1 && segment[0] == '0' ||
                    !segment.All(char.IsAsciiDigit) ||
                    !int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out var index) ||
                    index >= value.GetArrayLength())
                    return false;
                value = value[index];
            }
            else
                return false;
        }
        return true;
    }

    private static string Decode(string segment)
    {
        if (!segment.Contains('~'))
            return segment;
        var decoded = new StringBuilder(segment.Length);
        for (var index = 0; index < segment.Length; index++)
        {
            var character = segment[index];
            if (character == '~')
            {
                if (++index == segment.Length || segment[index] is not ('0' or '1'))
                    throw new ArgumentException("The JSON Pointer escape is invalid.");
                character = segment[index] == '0' ? '~' : '/';
            }
            decoded.Append(character);
        }
        return decoded.ToString();
    }
}
