using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Elsa.Foundation.Identity.OpenIddict.Groundwork.Stores;

/// <summary>
/// Produces a deterministic, secret-safe identity for replayable OpenIddict
/// mutations. Length framing prevents ambiguous component concatenation.
/// </summary>
public readonly record struct OpenIddictGroundworkOperationIdentity(string Value)
{
    public static OpenIddictGroundworkOperationIdentity Create(string operation, params string?[] components)
    {
        if (string.IsNullOrWhiteSpace(operation))
            throw new ArgumentException("An operation name is required.", nameof(operation));

        ArgumentNullException.ThrowIfNull(components);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, operation);
        foreach (var component in components)
            Append(hash, component ?? string.Empty);

        return new OpenIddictGroundworkOperationIdentity(
            $"oidc:{operation}:{Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant()}");
    }

    private static void Append(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }

    public override string ToString() => Value;
}
