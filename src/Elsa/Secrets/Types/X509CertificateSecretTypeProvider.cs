using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;

namespace Elsa.Secrets.Types;

public sealed class X509CertificateSecretTypeProvider : DelegatingSecretTypeProvider
{
    public X509CertificateSecretTypeProvider()
        : base(new(
            SecretTypeNames.X509Certificate,
            "X.509 certificate",
            "X.509 certificate or configuration-backed certificate material.",
            "textarea",
            [SecretStoreNames.Encrypted, SecretStoreNames.Configuration]))
    {
    }
}
