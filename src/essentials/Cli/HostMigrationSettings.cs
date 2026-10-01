using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace Elsa.Cli;

/// <summary>
/// The one migration setting <c>persistence apply</c> takes from a host: how long a SQLite database's EF migration lock is waited
/// for before it is reported as stale (#2196). Read from the host's own <c>appsettings.json</c> plus its <c>--environment</c>
/// overlay, the way <see cref="HostMembershipSettings"/> reads the skew allowance, and nothing else is lifted out of them: a scalar
/// time span cannot be a connection string.
/// </summary>
/// <remarks>
/// Only those two files are read. A host that sets the value through its own process environment is invisible here, exactly as it
/// is for the skew allowance.
/// </remarks>
internal static class HostMigrationSettings
{
    /// <summary>
    /// The key, spelled as <c>EfMigrateOptions.SectionName</c> and <c>SqliteMigrationLockStaleAfter</c> spell it. This assembly
    /// cannot reference the options, so a test holds the two to each other.
    /// </summary>
    public const string SqliteMigrationLockStaleAfterKey = "Elsa:Persistence:EntityFramework:Migrate:SqliteMigrationLockStaleAfter";

    /// <summary>The host's configured bound for <paramref name="environment"/>, or <see langword="null"/> when it configures none.</summary>
    public static TimeSpan? SqliteMigrationLockStaleAfter(string hostDirectory, string environment)
    {
        string? value;
        try
        {
            value = new ConfigurationBuilder()
                .AddJsonFile(Path.Join(hostDirectory, "appsettings.json"), optional: true, reloadOnChange: false)
                .AddJsonFile(Path.Join(hostDirectory, $"appsettings.{environment}.json"), optional: true, reloadOnChange: false)
                .Build()[SqliteMigrationLockStaleAfterKey];
        }
        catch (Exception failure) when (failure is FormatException or InvalidDataException or IOException)
        {
            throw CliRefusal.Resolution(
                "host-configuration-unreadable",
                $"The host's appsettings beside '{hostDirectory}' could not be read for '{SqliteMigrationLockStaleAfterKey}': {failure.Message}");
        }

        return value is null ? null : Parse(value);
    }

    /// <summary>A positive time span, read as the host's own configuration binder reads one.</summary>
    public static TimeSpan Parse(string value) =>
        TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var staleAfter) && staleAfter > TimeSpan.Zero
            ? staleAfter
            : throw CliRefusal.Usage("invalid-migration-lock-bound", $"'{SqliteMigrationLockStaleAfterKey}' in the host's appsettings is '{value}', which is not a positive time span such as 00:10:00.");
}
