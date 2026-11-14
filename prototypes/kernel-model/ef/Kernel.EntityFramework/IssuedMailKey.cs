using Kernel.Durable;

namespace Kernel.EntityFramework;

internal static class IssuedMailKey
{
    // Actor-issued (−1), job/issuer-length scopes (at most 2^32), and the child creator (Int64.MaxValue)
    // are the kernel's issued origins. Event rows (≤−2) and ordinary ingress (0) always keep native text.
    // The escape discriminator lives outside those origins: no caller string is reserved or widened.
    private const long Escape = 1L << 33;
    private const long EscapedChild = long.MaxValue - 1;

    internal static InboxKey ToTextKey(InboxKey key)
    {
        if (!key.HasIssuedOrigin || !key.Id.Contains('\0') && !DurableKeyJson.HasUnpairedSurrogate(key.Id)) return key;
        var scope = key.Scope == long.MaxValue ? EscapedChild : checked(Escape + key.Scope);
        return new(Convert.ToBase64String(Encode(key.Id)), scope);
    }

    internal static InboxKey FromTextKey(InboxKey key)
    {
        if (key.Scope != EscapedChild && (key.Scope < Escape - 1 || key.Scope > Escape + (1L << 32))) return key;
        return new(Decode(Convert.FromBase64String(key.Id)), key.Scope == EscapedChild ? long.MaxValue : key.Scope - Escape);
    }

    internal static byte[] Encode(string value) => DurableKeyJson.CodeUnits(value);
    internal static string Decode(byte[] value) => DurableKeyJson.FromCodeUnits(value);
}
