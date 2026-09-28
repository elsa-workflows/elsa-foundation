using System.Globalization;
using Elsa.Cluster.Core.Exceptions;
using Elsa.Cluster.Core.Options;
using Elsa.Cluster.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Cluster.Hosting;

/// <summary>
/// Selects a host's membership provider from its configuration, once, on the host container (spec 183, FR-024; ADR
/// 0078, amended). Nothing is registered unless configuration enables the EF provider, so a host that configures nothing
/// keeps the in-process default: a cluster of one that costs nothing (FR-017, FR-018). A cluster declares itself only
/// this way (FR-018a).
/// </summary>
/// <remarks>
/// <para>
/// The keys live under <see cref="ClusterMembershipOptions.SectionName"/>: <c>HostId</c>, <c>HeartbeatInterval</c>,
/// <c>ExpiryPeriod</c> and <c>SkewAllowance</c> for every provider, and under its <c>EntityFrameworkCore</c> subsection
/// <c>Enabled</c>, <c>Provider</c>, <c>ConnectionString</c>, <c>ConnectionName</c>, <c>Schema</c>, <c>Pooling</c> and
/// <c>CleanupPeriod</c> for this one.
/// </para>
/// <para>
/// It fails loudly rather than falling back. A value that does not parse is refused, naming its key, and so is an
/// <c>EntityFrameworkCore</c> subsection that carries settings but no <c>Enabled</c> switch: a host that meant to join a
/// cluster and silently stayed a cluster of one is exactly the failure that looks like success.
/// </para>
/// <para>
/// This lives in a provider-neutral namespace so a host selects membership without naming an EF type in its own source.
/// </para>
/// </remarks>
public static class ClusterMembershipConfigurationExtensions
{
    public const string EnabledKey = "Enabled";

    public static IServiceCollection AddConfiguredClusterMembership(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var membership = configuration.GetSection(ClusterMembershipOptions.SectionName);
        var ef = membership.GetSection(EfClusterMembershipOptions.SectionKey);
        switch (ReadBool(ef, EnabledKey))
        {
            case null when ef.GetChildren().Any():
                throw new ClusterMembershipConfigurationException(
                    $"{ef.Path} carries settings but no {EnabledKey} switch. Set {ef.Path}:{EnabledKey} to true to join a cluster " +
                    "through the EF membership provider, or to false to stay a cluster of one.");
            case null or false:
                return services;
        }

        var hostId = membership[nameof(ClusterMembershipOptions.HostId)];
        var heartbeatInterval = ReadTimeSpan(membership, nameof(ClusterMembershipOptions.HeartbeatInterval));
        var expiryPeriod = ReadTimeSpan(membership, nameof(ClusterMembershipOptions.ExpiryPeriod));
        var skewAllowance = ReadTimeSpan(membership, nameof(ClusterMembershipOptions.SkewAllowance));
        services.Configure<ClusterMembershipOptions>(options =>
        {
            options.HostId = hostId ?? options.HostId;
            options.HeartbeatInterval = heartbeatInterval ?? options.HeartbeatInterval;
            options.ExpiryPeriod = expiryPeriod ?? options.ExpiryPeriod;
            options.SkewAllowance = skewAllowance ?? options.SkewAllowance;
        });

        var settings = new EfClusterMembershipOptions();
        settings.Provider = ef[nameof(settings.Provider)] ?? settings.Provider;
        settings.ConnectionString = ef[nameof(settings.ConnectionString)];
        settings.ConnectionName = ef[nameof(settings.ConnectionName)];
        settings.Schema = ef[nameof(settings.Schema)];
        settings.Pooling = ReadBool(ef, nameof(settings.Pooling)) ?? settings.Pooling;
        settings.CleanupPeriod = ReadTimeSpan(ef, nameof(settings.CleanupPeriod)) ?? settings.CleanupPeriod;
        return services.AddEfClusterMembership(settings);
    }

    private static bool? ReadBool(IConfigurationSection section, string key) =>
        section[key] is not { } value || string.IsNullOrWhiteSpace(value) ? null
        : bool.TryParse(value, out var parsed) ? parsed
        : throw Invalid(section, key, value, "true or false");

    private static TimeSpan? ReadTimeSpan(IConfigurationSection section, string key) =>
        section[key] is not { } value || string.IsNullOrWhiteSpace(value) ? null
        : TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var parsed) ? parsed
        : throw Invalid(section, key, value, "a time span such as 00:00:10");

    private static ClusterMembershipConfigurationException Invalid(IConfigurationSection section, string key, string value, string expected) =>
        new($"{section.Path}:{key} is '{value}', which is not {expected}.");
}
