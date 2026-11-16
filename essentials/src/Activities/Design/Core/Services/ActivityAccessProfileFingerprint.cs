using System.Security.Cryptography;
using System.Text;

namespace Elsa.Activities.Design.Core.Services;

public static class ActivityAccessProfileFingerprint
{
    public static string Create(string authorizationProfile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authorizationProfile);
        return $"sha256-{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(authorizationProfile))).ToLowerInvariant()}";
    }
}
