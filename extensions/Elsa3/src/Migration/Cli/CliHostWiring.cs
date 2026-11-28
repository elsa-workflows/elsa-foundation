using System.Reflection;
using Elsa.Migration.Host;
using Elsa3.Migration.Tooling;

namespace Elsa3.Migration.Cli;

/// <summary>
/// The CLI's way to the migration host (spec 1237 research R-5): the host assembly, the project folders written into
/// this assembly's metadata at build time, and the host's two wiring methods (T004a) as delegates.
/// </summary>
internal static class CliHostWiring
{
    public const string HostProjectFolderKey = "Elsa3Migration.HostProjectFolder";
    public const string CliProjectFolderKey = "Elsa3Migration.CliProjectFolder";

    public static HostWiring Create()
    {
        var hostAssembly = typeof(MigrationHost).Assembly;
        return new HostWiring(
            hostAssembly,
            Metadata(HostProjectFolderKey),
            Metadata(CliProjectFolderKey),
            // The CLI passes no command-line arguments to the host's configuration (FR-008).
            (builder, hostFolder, environment, includeLocalSources) =>
                MigrationHost.AddHostConfiguration(builder, hostFolder, environment, hostAssembly, [], includeLocalSources),
            MigrationHost.AddHostServices);
    }

    private static string Metadata(string key) =>
        typeof(CliHostWiring).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().SingleOrDefault(attribute => attribute.Key == key)?.Value
        ?? throw new InvalidOperationException($"The CLI was built without its '{key}' metadata.");
}
