using Elsa.Workflows.Runtime.KernelHosting;

namespace Elsa.Workbench.Composition;

/// <summary>Resolves the Workbench store and hardens production runtime settings.</summary>
internal static class WorkbenchRuntimeProfile
{
    internal static void Configure(ConfigurationManager configuration, IHostEnvironment environment, string[] args)
    {
        if (configuration.GetValue<bool>("Elsa:CorePreview:Enabled"))
            throw new InvalidOperationException("The Workbench mixed Core Preview has been retired. Use its pinned image " +
                "to drain the existing stores. See the Workbench compatibility guidance in docs/docker.md.");

        var shells = configuration.GetSection("CShells:Shells");
        if (shells.GetChildren().SelectMany(shell => shell.GetSection("Features").GetChildren())
            .Any(feature => (feature.Key is "WorkflowsRuntimeApi" or "WorkflowsRuntimeEntityFrameworkCore") &&
                !string.Equals(feature.Value, "false", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("The classic Workbench workflow runtime has been retired. Use its pinned image " +
                "to open the existing classic stores. See the Workbench compatibility guidance in docs/docker.md.");

        var features = shells.GetSection("default:Features");
        var hosting = features.GetSection(WorkflowsRuntimeKernelHostingFeature.FeatureName);
        var actors = IsSelected(hosting);

        if (environment.IsProduction())
        {
            if (actors && IsSelected(features.GetSection("WorkflowsRuntimeArtifactsEntityFrameworkCorePersistence")))
                configuration.AddJsonFile("shells.Actor.Production.json", optional: false, reloadOnChange: true);

            // Host-supplied credentials and explicit arguments take precedence over the selected profile.
            configuration.AddEnvironmentVariables().AddCommandLine(args);
        }

        if (!actors || !string.Equals(hosting["Provider"] ?? "SQLite", "SQLite", StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrWhiteSpace(hosting["SqlitePath"]))
            return;

        configuration.AddInMemoryCollection(
        [
            new($"{hosting.Path}:SqlitePath", Path.GetFullPath("elsa-workbench-actors.db", environment.ContentRootPath))
        ]);
    }

    private static bool IsSelected(IConfigurationSection feature) =>
        feature.Exists() && !string.Equals(feature.Value, "false", StringComparison.OrdinalIgnoreCase);
}
