using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace Elsa.Cli;

/// <summary>
/// The one cluster membership setting <c>persistence status</c> judges liveness with: the skew allowance, which decides
/// whether a member whose heartbeat has lapsed is still counted. Read from the host's own <c>appsettings.json</c> plus its
/// <c>--environment</c> overlay, the way <see cref="ShellConfiguration"/> reads the shell files, and nothing else is lifted
/// out of them: a scalar time span cannot be a connection string.
/// </summary>
/// <remarks>
/// Only those two files are read. A host that sets the value through its own process environment
/// (<c>Elsa__Cluster__Membership__SkewAllowance</c>) is invisible here, exactly as <c>--environment</c> is a flag and never
/// this process's own environment, so the operator names it with <c>--skew-allowance</c>.
/// </remarks>
internal static class HostMembershipSettings
{
    /// <summary>
    /// The key, spelled as <c>ClusterMembershipOptions.SectionName</c> and <c>SkewAllowance</c> spell it. This assembly cannot
    /// reference the options, so an architecture test holds the two to each other.
    /// </summary>
    public const string SkewAllowanceKey = "Elsa:Cluster:Membership:SkewAllowance";

    /// <summary>The host's configured skew allowance for <paramref name="environment"/>, or <see langword="null"/> when it configures none.</summary>
    public static TimeSpan? SkewAllowance(string hostDirectory, string environment)
    {
        string? value;
        try
        {
            value = new ConfigurationBuilder()
                .AddJsonFile(Path.Join(hostDirectory, "appsettings.json"), optional: true, reloadOnChange: false)
                .AddJsonFile(Path.Join(hostDirectory, $"appsettings.{environment}.json"), optional: true, reloadOnChange: false)
                .Build()[SkewAllowanceKey];
        }
        catch (Exception failure) when (failure is FormatException or InvalidDataException or IOException)
        {
            throw CliRefusal.Resolution(
                "host-configuration-unreadable",
                $"The host's appsettings beside '{hostDirectory}' could not be read for '{SkewAllowanceKey}': {failure.Message} Pass --skew-allowance to judge with a value of your own.");
        }

        return value is null ? null : Parse(value, $"'{SkewAllowanceKey}' in the host's appsettings");
    }

    /// <summary>A non-negative time span, read as the host's own configuration binder reads one.</summary>
    public static TimeSpan Parse(string value, string source) =>
        TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var skew) && skew >= TimeSpan.Zero
            ? skew
            : throw CliRefusal.Usage("invalid-skew-allowance", $"{source} is '{value}', which is not a non-negative time span such as 00:00:05.");
}
