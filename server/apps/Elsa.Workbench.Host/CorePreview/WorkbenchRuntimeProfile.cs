using Elsa.Workbench.Readiness;
using Elsa.Workflows.Runtime.KernelHosting;

namespace Elsa.Workbench.CorePreview;

/// <summary>Resolves Workbench's local Actor store and the explicit developer preview composition.</summary>
internal static class WorkbenchRuntimeProfile
{
    internal static void Configure(ConfigurationManager configuration, IHostEnvironment environment, string[] args)
    {
        var features = configuration.GetSection("CShells:Shells:default:Features");
        var hosting = features.GetSection(WorkflowsRuntimeKernelHostingFeature.FeatureName);
        var actors = IsSelected(hosting);
        var readinessShell = configuration[$"{ShellReadinessOptions.SectionName}:{nameof(ShellReadinessOptions.DefaultShellName)}"];
        var preview = actors && (readinessShell is null or "default") && configuration.GetValue<bool>(WorkbenchCorePreview.EnabledKey);
        if (preview)
            configuration.AddJsonFile("shells.CorePreview.json", optional: false, reloadOnChange: true);

        if (environment.IsProduction())
        {
            if (preview || !actors && features.GetChildren().Any(feature =>
                    feature.Key is "WorkflowsRuntimeApi" or "WorkflowsRuntimeEntityFrameworkCore"))
                configuration.AddJsonFile("shells.CorePreview.Production.json", optional: false, reloadOnChange: true);
            else if (actors && IsSelected(features.GetSection("WorkflowsRuntimeArtifactsEntityFrameworkCorePersistence")))
                configuration.AddJsonFile("shells.Actor.Production.json", optional: false, reloadOnChange: true);
        }

        // Host-supplied credentials and explicit arguments take precedence over the selected profile.
        if (preview || environment.IsProduction())
            configuration.AddEnvironmentVariables().AddCommandLine(args);
        if (!actors || preview || !string.Equals(hosting["Provider"] ?? "SQLite", "SQLite", StringComparison.OrdinalIgnoreCase) ||
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
