using System.Text.Json;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Canonical identities for the new source protocol; historical checkpoint identities are unchanged.</summary>
internal static class RuntimeDeliveryFingerprint
{
    internal static string Compute(string domain, object value)
    {
        var element = Elsa.Serialization.Core.JsonHashInput.SerializeToElement(value);
        Validate(element);
        return Elsa.Serialization.Core.JsonHashInput.Hash(new object[] { domain, element });
    }

    private static void Validate(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw new ArgumentException("Canonical delivery JSON cannot contain duplicate property names.");
                Text(property.Name, nameof(value), allowEmpty: true);
                Validate(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var child in value.EnumerateArray()) Validate(child);
        else if (value.ValueKind == JsonValueKind.String) Text(value.GetString()!, nameof(value), allowEmpty: true);
        else if (value.ValueKind == JsonValueKind.Undefined) throw new ArgumentException("Delivery JSON must be defined.", nameof(value));
    }

    internal static void Text(string value, string name, bool allowEmpty = false)
    {
        if (allowEmpty) ArgumentNullException.ThrowIfNull(value, name);
        else ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        for (var index = 0; index < value.Length; index++)
        {
            if (!char.IsSurrogate(value[index])) continue;
            if (!char.IsHighSurrogate(value[index]) || index + 1 == value.Length || !char.IsLowSurrogate(value[++index]))
                throw new ArgumentException("Delivery identities must preserve their exact Unicode representation.", name);
        }
    }

    internal static void Hash(string value, string name)
    {
        if (value is null || value.Length != 64 || value.Any(character => character is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new ArgumentException("A complete lowercase SHA-256 fingerprint is required.", name);
    }
}
