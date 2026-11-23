using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Models;
using Elsa.Foundation.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Foundation.DataProtection;

/// <summary>
/// Warns, as the host starts, about the two compositions whose failure looks like success (#2191): a clustered host whose
/// key ring is its own, and a shared key store that keeps the keys unencrypted.
/// </summary>
/// <remarks>
/// <para>
/// A host is clustered when it composed a durable membership provider. Its keys are shared when anything set a key repository:
/// the EF key store, or one a host composed in code. A clustered host without one still starts, and a payload one host
/// protects is refused by the next: a sign-in that does not survive the load balancer, a form post that fails its
/// antiforgery check. It is a warning rather than a refusal because the key ring matters only to a shell that protects
/// something, and which shells do is not known while the host starts: a cluster of hosts whose features sign nobody in
/// shares nothing through it, and would be refused for a problem it does not have.
/// </para>
/// <para>
/// Without a certificate, the EF key store holds each key's secret as written, and ASP.NET Core says so only when it creates a
/// key, once in its lifetime. Whoever can read the table can then forge a sign-in, so this says it at every start.
/// </para>
/// </remarks>
internal sealed class DataProtectionStartupCheck(
    IEnumerable<ClusterMembershipProviderRegistration> memberships,
    IOptions<KeyManagementOptions> keys,
    ILogger<DataProtectionStartupCheck> logger) : IHostedService
{
    private const string Section = DataProtectionConfigurationExtensions.SectionName;
    private const string KeyStoreSwitch = $"{Section}:{EfDataProtectionKeyStoreOptions.SectionKey}:{DataProtectionConfigurationExtensions.EnabledKey}";
    private const string CertificatePath = $"{Section}:{DataProtectionConfigurationExtensions.CertificateSectionKey}:{DataProtectionConfigurationExtensions.CertificatePathKey}";
    private const string CertificatePassword = $"{Section}:{DataProtectionConfigurationExtensions.CertificateSectionKey}:{DataProtectionConfigurationExtensions.CertificatePasswordKey}";

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var options = keys.Value;
        if (options.XmlRepository is null && memberships.FirstOrDefault(membership => membership.Kind == ClusterProviderKind.Durable) is { } durable)
            logger.LogWarning(
                "This host is clustered through the {MembershipProvider} membership provider, but its Data Protection key ring is its own, so a " +
                "sign-in cookie or antiforgery token another host issued is refused here. Share the key ring by setting {KeyStoreSwitch}=true " +
                "on every host, with the same database.",
                durable.Name, KeyStoreSwitch);
        if (options.XmlRepository is EfDataProtectionKeyRepository && options.XmlEncryptor is null)
            logger.LogWarning(
                "The Data Protection keys in {Table} are stored unencrypted, so whoever can read that table can forge a sign-in. Encrypt them " +
                "at rest with a certificate, the same on every host: {CertificatePath} and {CertificatePassword}.",
                DataProtectionKeysEfModule.TableName, CertificatePath, CertificatePassword);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
