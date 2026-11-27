using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.EntityFramework;

// The issuer proves the address was allocated under the same fence that can publish the parent checkpoint.
// Creator fingerprints include the immutable Start envelope, including the executable selected by the model.
// Authentication is deterministic so retries of the same prepared birth have the same Runtime pin identity.
internal sealed record FoundationChildIssuance(string Tenant, string Parent, string ParentFingerprint, long ParentRevision,
    string Child, string ChildFingerprint)
{
    private const string Prefix = "child-issuance/1.";
    private const int MaximumLength = 2_048;

    internal static string Issue(byte[] key, FoundationChildIssuance birth, PartitionFence fence)
    {
        if (!birth.IsValid(out var partition, out var epoch) || fence.Partition != partition || fence.Epoch != epoch)
            throw new InvalidOperationException("A child issuance must match its parent checkpoint's exact partition and epoch.");
        var payload = Prefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(DurableKeyJson.Write(birth)));
        var token = payload + "." + Convert.ToHexString(HMACSHA256.HashData(key, Encoding.ASCII.GetBytes(payload)));
        if (token.Length > MaximumLength) throw new InvalidOperationException("The child issuance exceeds its wire limit.");
        return token;
    }

    internal static FoundationChildIssuance? Verify(byte[] key, string tenant, Contract.ActorRef reference, string token)
    {
        if (token is not { Length: <= MaximumLength } || !token.StartsWith(Prefix, StringComparison.Ordinal)) return null;
        var macAt = token.LastIndexOf('.');
        if (macAt <= Prefix.Length || !IsDigest(token.AsSpan(macAt + 1))) return null;
        var mac = HMACSHA256.HashData(key, Encoding.ASCII.GetBytes(token[..macAt]));
        if (!CryptographicOperations.FixedTimeEquals(mac, Convert.FromHexString(token[(macAt + 1)..]))) return null;
        try
        {
            var birth = DurableKeyJson.Read<FoundationChildIssuance>(
                Encoding.UTF8.GetString(Convert.FromBase64String(token[Prefix.Length..macAt])));
            return birth is not null && birth.Tenant == tenant && birth.IsValid(out _, out _) &&
                FoundationRootCreator.ReferenceFor(key, tenant, new(birth.Child, 1)) == reference.Id ? birth : null;
        }
        catch (Exception error) when (error is System.Text.Json.JsonException or FormatException)
        {
            return null;
        }
    }

    internal (int Partition, long Epoch) Fence()
    {
        if (!IsValid(out var partition, out var epoch)) throw new InvalidOperationException("Invalid child issuance.");
        return (partition, epoch);
    }

    private bool IsValid(out int partition, out long epoch)
    {
        partition = 0;
        epoch = 0;
        if (Tenant is not { Length: >= 1 and <= 64 } || Tenant.Any(character =>
                character is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '-')) ||
            ParentRevision <= 0 || !IsFingerprint(ParentFingerprint) || !IsFingerprint(ChildFingerprint) ||
            Parent is not { Length: > 0 and <= 256 } || Child is not { Length: > 0 and <= 96 } || Parent == Child ||
            !CreationProtocol.IsPhysicalAddress(Child)) return false;
        var slash = Child.IndexOf('/');
        if (Child[0] != 'g' || !Child.AsSpan(slash).StartsWith("/child/", StringComparison.Ordinal)) return false;
        var issuer = Child.AsSpan(slash + "/child/".Length);
        var endOfPartition = issuer.IndexOf('.');
        var epochText = issuer[(endOfPartition + 1)..];
        partition = int.Parse(issuer[..endOfPartition], CultureInfo.InvariantCulture);
        epoch = long.Parse(epochText[..epochText.IndexOf('.')], CultureInfo.InvariantCulture);
        return StoreBatchRules.AddressPartition(Parent) == partition && new InstanceRef(Child).Partition == partition;
    }

    private static bool IsFingerprint(string? fingerprint) => fingerprint is { Length: 67 } &&
        fingerprint.StartsWith("v1:", StringComparison.Ordinal) && IsDigest(fingerprint.AsSpan(3));

    private static bool IsDigest(ReadOnlySpan<char> value) =>
        value.Length == 64 && value.IndexOfAnyExcept("0123456789ABCDEF") < 0;
}

internal enum FoundationChildBirthProof
{
    Unresolved,
    BirthRetired,
    IssuanceFencedAbsent
}
