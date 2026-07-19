namespace Elsa.Foundation.Identity.Persistence.Groundwork.Stores;

public readonly record struct IdentityRequestFingerprint(string Value)
{
    public static IdentityRequestFingerprint FromParts(params string?[] parts)
    {
        var framed = parts.Select(part => part ?? string.Empty);
        return new IdentityRequestFingerprint(IdentityHashFraming.Sha256Hex(framed));
    }

    public override string ToString() => Value;
}
