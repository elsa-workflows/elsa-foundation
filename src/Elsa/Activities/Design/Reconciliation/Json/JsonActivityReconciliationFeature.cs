using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Activities.Design.Reconciliation.Core;
using Elsa.Activities.Design.Reconciliation.Json.Contracts;
using Elsa.Activities.Design.Reconciliation.Json.Options;
using Elsa.Activities.Design.Reconciliation.Json.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Design.Reconciliation.Json;

/// <summary>
/// Registers the JSON-file reconciliation source. This is a standalone source feature (§2.6.1): it
/// does NOT derive from <c>ActivitiesDesignReconciliationFeature</c> — it only contributes an
/// <see cref="IActivityReconciliationSource"/> that the reconciliation feature's universal handler
/// discovers from DI. Add both features to a shell to reconcile from a JSON file.
/// </summary>
/// <remarks>
/// Every collaborator is registered against its contract, so an inheriting feature can override a
/// single piece (e.g. a different file layout) by calling <c>base.ConfigureServices</c> and
/// re-registering just that contract afterwards. The options instance is the only singleton — it is
/// an application-wide static value; everything else "just executes" and is therefore scoped (§2.5.1).
/// </remarks>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Activities")]
[ManifestFeatureCategory("Design")]
[ManifestFeatureCategory("Reconciliation")]
[ShellFeature(
    name: "JsonActivityReconciliation",
    Description = "Reconciliation source that reads activity definitions from JSON files."
)]
public class JsonActivityReconciliationFeature : IShellFeature
{
    public JsonReconciliationOptions Options { get; set; } = new();

    public virtual void ConfigureServices(IServiceCollection services)
    {
        ValidateOptions();

        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(Options));
        services.AddScoped<IJsonActivityCatalogReader, JsonActivityCatalogReader>();
        services.AddScoped<IActivityReconciliationSource, JsonActivityReconciliationSource>();
    }

    /// <summary>
    /// Fails fast at registration on an invalid composition: a required <c>SourceId</c>, and exactly one
    /// of <c>FilePath</c> (single-file shorthand) or <c>Files</c> (ordered list) — never both, never
    /// neither. Centralising the check here keeps the source itself free of configuration policy.
    /// </summary>
    private void ValidateOptions()
    {
        if (string.IsNullOrWhiteSpace(Options.SourceId))
            throw new InvalidOperationException(
                $"{nameof(JsonActivityReconciliationFeature)} requires a non-empty {nameof(JsonReconciliationOptions.SourceId)}.");

        var hasSingleFile = !string.IsNullOrWhiteSpace(Options.FilePath);
        var hasFileList = Options.Files.Any();

        if (hasSingleFile == hasFileList)
            throw new InvalidOperationException(
                $"{nameof(JsonActivityReconciliationFeature)} requires exactly one of "
                + $"{nameof(JsonReconciliationOptions.FilePath)} or {nameof(JsonReconciliationOptions.Files)} "
                + "to be configured — not both, and not neither.");
    }
}
