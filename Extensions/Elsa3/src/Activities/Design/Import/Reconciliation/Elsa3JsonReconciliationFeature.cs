using CShells.Features;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Tasks.Core;
using Elsa3.Activities.Design.Import.Reconciliation.Options;
using Elsa3.Activities.Design.Import.Reconciliation.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa3.Activities.Design.Import.Reconciliation;

/// <summary>
/// Imports a folder (or files) of Elsa 3 workflow definitions at engine start through the Elsa 3 collection import,
/// and publishes them: reusable workflows as graph activities, the other workflows and one wrapper per reusable
/// workflow as published workflow definitions that keep their Elsa 3 definition ids (spec 235). Opt-in.
/// </summary>
/// <remarks>
/// Compose it with the import's persistence provider (today <c>Elsa3ImportActivitiesEntityFrameworkCore</c>).
/// Removing a file removes nothing. Versions are <c>{n}.0.0</c>; before Elsa 4 is in production, a host that imported
/// with an earlier converter rebuilds its store after a converter upgrade.
/// </remarks>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Elsa3")]
[ManifestFeatureCategory("Import")]
[ManifestFeatureCategory("Reconciliation")]
[ShellFeature(
    name: "Elsa3JsonReconciliation",
    DisplayName = "Elsa 3 JSON Reconciliation",
    Description = "Imports Elsa 3 workflow definitions from files at engine start and publishes them.",
    // The import publishes reusables through Elsa3ImportActivitiesPublishing (and so WorkflowsPublishingApi, whose
    // authorization context this feature decorates); the imported workflows are published through WorkflowsPublishing.
    DependsOn = new object[] { "Elsa3ImportActivitiesPublishing", "WorkflowsPublishing" })]
public class Elsa3JsonReconciliationFeature : IShellFeature
{
    public Elsa3JsonReconciliationOptions Options { get; set; } = new();

    public virtual void ConfigureServices(IServiceCollection services)
    {
        ValidateOptions(Options);

        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(Options));
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<Elsa3JsonFileReader>();
        services.AddSingleton<Elsa3ImportSelection>();
        services.AddScoped<ImportedWorkflowPublisher>();
        services.AddScoped<Elsa3StartupPublishingGrant>();
        Elsa3StartupPublishingAuthorization.Decorate(services);
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IStartupTask, Elsa3FolderImportTask>());
    }

    /// <summary>A required <c>SourceId</c>, and exactly one of <c>FilePath</c>, <c>Files</c> or <c>FolderPath</c>.</summary>
    private static void ValidateOptions(Elsa3JsonReconciliationOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.SourceId))
            throw new InvalidOperationException(
                $"{nameof(Elsa3JsonReconciliationFeature)} requires a non-empty {nameof(Elsa3JsonReconciliationOptions.SourceId)}.");

        var configuredPathOptions = new[]
        {
            !string.IsNullOrWhiteSpace(options.FilePath),
            options.Files.Any(),
            !string.IsNullOrWhiteSpace(options.FolderPath),
        }.Count(configured => configured);

        if (configuredPathOptions != 1)
            throw new InvalidOperationException(
                $"{nameof(Elsa3JsonReconciliationFeature)} (SourceId '{options.SourceId}') requires exactly one of "
                + $"{nameof(Elsa3JsonReconciliationOptions.FilePath)}, {nameof(Elsa3JsonReconciliationOptions.Files)}, "
                + $"or {nameof(Elsa3JsonReconciliationOptions.FolderPath)} to be configured, not several and not none.");
    }
}
