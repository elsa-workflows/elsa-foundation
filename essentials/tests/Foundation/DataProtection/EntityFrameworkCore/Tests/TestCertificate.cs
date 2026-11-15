using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Elsa.Foundation.DataProtection.EntityFrameworkCore.Tests;

/// <summary>A self-signed RSA certificate written to a PKCS#12 file of its own, as an operator mounts one; deleted on dispose.</summary>
internal sealed class TestCertificate : IDisposable
{
    public const string Password = "elsa-test-password";

    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("elsa-key-ring-certificate-");

    private TestCertificate(bool withPrivateKey)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=Elsa Data Protection test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        if (withPrivateKey)
            File.WriteAllBytes(Path, certificate.Export(X509ContentType.Pkcs12, Password));
        else
        {
            using var publicOnly = X509CertificateLoader.LoadCertificate(certificate.Export(X509ContentType.Cert));
            File.WriteAllBytes(Path, publicOnly.Export(X509ContentType.Pkcs12, Password));
        }
    }

    public const string FileName = "data-protection.pfx";

    public string Path => System.IO.Path.Join(_directory.FullName, FileName);

    /// <summary>The file's path from a sibling of its directory, such as a host's temporary content root.</summary>
    public string PathFromSibling => System.IO.Path.Join("..", _directory.Name, FileName);

    public static TestCertificate Create() => new(withPrivateKey: true);

    public static TestCertificate CreateWithoutPrivateKey() => new(withPrivateKey: false);

    /// <summary>The settings that encrypt the keys at rest with this certificate.</summary>
    public Dictionary<string, string?> Settings(string? path = null, string? password = Password) => new()
    {
        [$"{DataProtectionConfigurationExtensions.SectionName}:{DataProtectionConfigurationExtensions.CertificateSectionKey}:{DataProtectionConfigurationExtensions.CertificatePathKey}"] = path ?? Path,
        [$"{DataProtectionConfigurationExtensions.SectionName}:{DataProtectionConfigurationExtensions.CertificateSectionKey}:{DataProtectionConfigurationExtensions.CertificatePasswordKey}"] = password
    };

    public void Dispose() => _directory.Delete(recursive: true);
}
