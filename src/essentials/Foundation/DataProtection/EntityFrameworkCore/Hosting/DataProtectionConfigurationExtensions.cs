using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Elsa.Foundation.DataProtection.EntityFrameworkCore;
using Elsa.Persistence.EntityFramework;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Foundation.DataProtection;

/// <summary>
/// Composes a host's ASP.NET Core Data Protection from its configuration, once, on the host container (#2191). The key ring
/// signs and encrypts the sign-in cookie, the antiforgery tokens and every other protected payload of every shell, so it
/// belongs to the host: CShells builds each shell container from copies of these registrations.
/// </summary>
/// <remarks>
/// <para>
/// The application name is <c>ApplicationName</c> under <see cref="SectionName"/>, <see cref="DefaultApplicationName"/> when
/// none is configured, never the default derived from the host's content root, so the hosts of one deployment, wherever they
/// are installed, read each other's payloads when they share a key ring. It is also the one boundary between deployments
/// that share a key store, a database or a machine's default key directory: the key table records no application, so two
/// deployments that must not read each other's payloads need distinct names, distinct key stores, or both.
/// </para>
/// <para>
/// The keys stay where ASP.NET Core keeps them by default unless configuration enables the EF key store: <c>Enabled</c>,
/// <c>Provider</c>, <c>ConnectionString</c>, <c>ConnectionName</c> (with <c>ConnectionStrings:Elsa</c> as the fallback),
/// <c>Schema</c> and <c>Pooling</c> under <see cref="SectionName"/>'s <c>EntityFrameworkCore</c> subsection. A certificate
/// under its <c>Certificate</c> subsection, <c>Path</c> to a PKCS#12 file and its <c>Password</c>, encrypts every key at rest.
/// A clustered host without a shared key store, and a key store without a certificate, are warned about as the host starts.
/// </para>
/// <para>
/// It fails loudly rather than falling back, through <see cref="EfHostConfigurationReader"/> as cluster membership does. A
/// value that does not parse is refused, naming its key, and so is an <c>EntityFrameworkCore</c> subsection that carries
/// settings but no <c>Enabled</c> switch, a certificate that cannot be loaded or holds no private key, and a certificate with
/// no key store to protect: a host that meant to share its keys and silently kept them to itself signs its users out on the
/// next node they reach.
/// </para>
/// <para>
/// This lives in a provider-neutral namespace so a host composes it without naming an EF type in its own source.
/// </para>
/// </remarks>
public static class DataProtectionConfigurationExtensions
{
    public const string SectionName = "Elsa:DataProtection";
    public const string ApplicationNameKey = "ApplicationName";
    public const string CertificateSectionKey = "Certificate";
    public const string CertificatePathKey = "Path";
    public const string CertificatePasswordKey = "Password";
    public const string EnabledKey = EfHostConfigurationReader.EnabledKey;

    /// <summary>The application name a host's key ring is isolated under when its configuration names none.</summary>
    public const string DefaultApplicationName = "Elsa";

    private static readonly EfHostConfigurationReader Reader = new();

    public static IServiceCollection AddConfiguredDataProtection(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(SectionName);
        var ef = section.GetSection(EfDataProtectionKeyStoreOptions.SectionKey);
        var keyStore = Reader.IsEnabled(ef, "to share the key ring through the platform database", "to keep this host's keys where ASP.NET Core keeps them by default")
            ? Reader.ReadStore(ef, new EfDataProtectionKeyStoreOptions())
            : null;
        var applicationName = section[ApplicationNameKey] is { } configured && !string.IsNullOrWhiteSpace(configured) ? configured : DefaultApplicationName;
        var dataProtection = services.AddDataProtection().SetApplicationName(applicationName);
        if (keyStore is not null)
            dataProtection.PersistKeysToElsaDatabase(keyStore);

        var certificateSection = section.GetSection(CertificateSectionKey);
        if (ReadCertificate(certificateSection, configuration[HostDefaults.ContentRootKey]) is { } certificate)
        {
            if (keyStore is null)
            {
                certificate.Dispose();
                throw new EfHostConfigurationException(
                    $"{certificateSection.Path} protects the keys of a key store, but {ef.Path}:{EnabledKey} is not true, so there is none. " +
                    "Enable the key store, or remove the certificate.");
            }

            dataProtection.ProtectKeysWithCertificate(certificate);
        }

        services.AddHostedService<DataProtectionStartupCheck>();
        return services;
    }

    /// <summary>
    /// The certificate under <paramref name="section"/>, or <see langword="null"/> when none is configured. A relative path is
    /// read from the host's content root, which is where its own settings were read from, whatever directory it was started in.
    /// </summary>
    private static X509Certificate2? ReadCertificate(IConfigurationSection section, string? contentRoot)
    {
        var path = section[CertificatePathKey];
        if (string.IsNullOrWhiteSpace(path))
            return section.GetChildren().Any(child => !string.IsNullOrEmpty(child.Value))
                ? throw new EfHostConfigurationException($"{section.Path} carries settings but no {CertificatePathKey}. Set {section.Path}:{CertificatePathKey} to the PKCS#12 file of the certificate that encrypts the keys.")
                : null;

        var file = Path.IsPathRooted(path) || string.IsNullOrEmpty(contentRoot) ? path : Path.Join(contentRoot, path);
        X509Certificate2 certificate;
        try
        {
            certificate = X509CertificateLoader.LoadPkcs12FromFile(file, section[CertificatePasswordKey]);
        }
        catch (Exception exception) when (exception is CryptographicException or IOException or UnauthorizedAccessException)
        {
            throw new EfHostConfigurationException($"{section.Path}:{CertificatePathKey} is '{file}', which could not be loaded as a PKCS#12 certificate with the configured password: {exception.Message}", exception);
        }

        if (certificate.HasPrivateKey)
            return certificate;
        certificate.Dispose();
        throw new EfHostConfigurationException($"{section.Path}:{CertificatePathKey} is '{file}', whose certificate holds no private key, so it could not decrypt the keys it encrypts.");
    }
}
