using Microsoft.Extensions.Configuration;
using System.Runtime.CompilerServices;

namespace Elsa.Cli.Worker;

/// <summary>
/// The host's own <c>appsettings.json</c> plus its <c>--environment</c> overlay, layered the way the host
/// itself layers them — the configuration root <c>--restore</c> hands to Nuplane.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately the same mechanism the front end already uses for <c>shells.json</c>
/// (<see cref="Elsa.Cli.ShellConfiguration"/>): two optional JSON files through the same configuration
/// providers a host composes at startup, so the two cannot disagree about what a file means. Nothing else
/// is layered — no environment variables and no command line — because both would be facts about this
/// process rather than about the host being restored for, exactly as <c>--environment</c> is read from the
/// flag and never from this process's own <c>ASPNETCORE_ENVIRONMENT</c>.
/// </para>
/// <para>
/// This is read in the <em>worker</em>, not the front end, and that placement is the point: a feed's
/// <c>Credentials</c> value lives in this file, so it must never be lifted into the request that crosses
/// the process boundary. The worker reads the file itself and hands the root straight to Nuplane.
/// </para>
/// <para>
/// <see cref="Read"/> is the only method here that names a configuration type. The worker ships no
/// <c>Microsoft.Extensions.Configuration.*</c> of its own — the host's closure supplies them, the same way
/// it supplies Nuplane — so it is <see cref="MethodImplOptions.NoInlining"/> and is called only once a
/// caller has decided this host is one that carries them.
/// </para>
/// </remarks>
internal static class HostAppSettings
{
    public const string BaseFileName = "appsettings.json";

    /// <summary>The <c>appsettings.&lt;environment&gt;.json</c> overlay's file name for <paramref name="environment"/>.</summary>
    public static string OverlayFileName(string environment) => $"appsettings.{environment}.json";

    /// <summary>Whether either file is present beside the host. Names no configuration type, so any caller may ask.</summary>
    public static bool Exist(string hostDirectory, string environment) =>
        File.Exists(Path.Join(hostDirectory, BaseFileName)) ||
        File.Exists(Path.Join(hostDirectory, OverlayFileName(environment)));

    /// <summary>
    /// The host's configuration root — the one that nests Nuplane's keys under a <c>Nuplane</c> section.
    /// The root is handed on as-is: Nuplane resolves that section itself and passes the resolved section to
    /// a module registration callback, so nothing here has to know where Nuplane's own keys begin.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static IConfigurationRoot Read(string hostDirectory, string environment) =>
        new ConfigurationBuilder()
            .AddJsonFile(Path.Join(hostDirectory, BaseFileName), optional: true, reloadOnChange: false)
            .AddJsonFile(Path.Join(hostDirectory, OverlayFileName(environment)), optional: true, reloadOnChange: false)
            .Build();
}
