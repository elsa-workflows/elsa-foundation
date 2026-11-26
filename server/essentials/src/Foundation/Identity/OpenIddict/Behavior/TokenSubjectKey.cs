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
        // Existing canonical-user and issuer-less legacy keys retain their exact UTF-8 v1 bytes.
        // Issuer-qualified external subjects have a separate versioned domain.
        var version = subject.UserId is null && subject.Issuer is not null ? "v2" : "v1";
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Action<IncrementalHash, string> append = version == "v2" ? AppendUtf16 : Append;
        append(hash, $"elsa.token-subject.{version}");
        append(hash, kind);
        append(hash, subject.TenantId);
        if (subject.UserId is { } userId)
        {
            append(hash, userId);
        }
        else
        {
            append(hash, subject.Provider!);
            if (subject.Issuer is { } issuer)
                append(hash, issuer);
            append(hash, subject.ProviderSubject!);
        }

        return $"elsa:{version}:{kind}:{Convert.ToHexStringLower(hash.GetHashAndReset())}";
    }

    // Managed external subjects are opaque .NET strings. Encoding.UTF8's replacement fallback would
    // otherwise collapse distinct unpaired surrogates and U+FFFD into one scope. This v2 domain writes
    // the exact UTF-16 code units; it deliberately leaves the existing v1 encoder unchanged.
    private static void AppendUtf16(IncrementalHash hash, string value)
    {
        var bytes = new byte[checked(value.Length * sizeof(char))];
        for (var index = 0; index < value.Length; index++)
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(index * sizeof(char)), value[index]);
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
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
