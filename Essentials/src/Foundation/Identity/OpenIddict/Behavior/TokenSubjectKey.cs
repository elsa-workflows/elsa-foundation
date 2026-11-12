using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Elsa.Foundation.Identity.Core.Authentication;

namespace Elsa.Foundation.Identity.OpenIddict;

/// <summary>
/// The string a <see cref="TokenSubject"/> is stored under in the OpenIddict token store, so issuance, refresh and
/// revocation all find the same entries. The parts are hashed, length-prefixed, so the key is bounded whatever a
/// tenant, provider or provider subject contains, and no delimiter inside one part can make two subjects read alike.
/// The kind stays readable so store queries can tell user tokens from provider-subject tokens.
/// </summary>
internal static class TokenSubjectKey
{
    public static string Create(TokenSubject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);

        var kind = subject.UserId is not null ? "user" : "provider";
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "elsa.token-subject.v1");
        Append(hash, kind);
        Append(hash, subject.TenantId);
        if (subject.UserId is { } userId)
        {
            Append(hash, userId);
        }
        else
        {
            Append(hash, subject.Provider!);
            Append(hash, subject.ProviderSubject!);
        }

        return $"elsa:v1:{kind}:{Convert.ToHexStringLower(hash.GetHashAndReset())}";
    }

    private static void Append(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }
}
