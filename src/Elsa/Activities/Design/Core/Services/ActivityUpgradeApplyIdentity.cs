using System.Security.Cryptography;
using System.Text;

namespace Elsa.Activities.Design.Core.Services;

public static class ActivityUpgradeApplyIdentity
{
    public static string HashIdempotencyKey(string idempotencyKey) => Hash(idempotencyKey);

    public static string CreateRequestFingerprint(string planId, string stageId) =>
        Hash($"{planId}\u001f{stageId}");

    public static string CreateReceiptId(string planId, string idempotencyKeyHash) =>
        $"activity-upgrade-receipt-{Hash($"{planId}\u001f{idempotencyKeyHash}")}";

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
