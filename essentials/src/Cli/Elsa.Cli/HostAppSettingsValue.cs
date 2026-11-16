using Microsoft.Extensions.Configuration;

namespace Elsa.Cli;

/// <summary>
/// One key out of the host's own <c>appsettings.json</c> plus its <c>--environment</c> overlay, the way
/// <see cref="ShellConfiguration"/> reads the shell files. Only those two files are read, and only the one key is lifted out of
/// them, so a scalar setting is all that can cross: a host that sets it through its process environment is invisible here.
/// </summary>
internal static class HostAppSettingsValue
{
    /// <summary>The value of <paramref name="key"/>, or <see langword="null"/> when the host sets none.</summary>
    /// <param name="hint">What the operator can do instead, appended to the refusal for files that cannot be read.</param>
    public static string? Read(string hostDirectory, string environment, string key, string hint = "")
    {
        try
        {
            return new ConfigurationBuilder()
                .AddJsonFile(Path.Join(hostDirectory, "appsettings.json"), optional: true, reloadOnChange: false)
                .AddJsonFile(Path.Join(hostDirectory, $"appsettings.{environment}.json"), optional: true, reloadOnChange: false)
                .Build()[key];
        }
        catch (Exception failure) when (failure is FormatException or InvalidDataException or IOException)
        {
            throw CliRefusal.Resolution(
                "host-configuration-unreadable",
                $"The host's appsettings beside '{hostDirectory}' could not be read for '{key}': {failure.Message}{hint}");
        }
    }
}
