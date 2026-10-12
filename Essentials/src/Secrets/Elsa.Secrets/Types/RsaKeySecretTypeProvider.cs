using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;

namespace Elsa.Secrets.Types;

public sealed class RsaKeySecretTypeProvider : DelegatingSecretTypeProvider
{
    public RsaKeySecretTypeProvider()
        : base(new(
            SecretTypeNames.RsaKey,
            "RSA key",
            "RSA private key or configuration-backed RSA key material.",
            "textarea",
            [SecretStoreNames.Encrypted, SecretStoreNames.Configuration]))
    {
    }
}
