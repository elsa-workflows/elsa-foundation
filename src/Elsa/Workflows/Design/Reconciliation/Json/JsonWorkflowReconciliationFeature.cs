using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Workflows.Design.Reconciliation.Contracts;
using Elsa.Workflows.Design.Reconciliation.Json.Contracts;
using Elsa.Workflows.Design.Reconciliation.Json.Options;
using Elsa.Workflows.Design.Reconciliation.Json.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Design.Reconciliation.Json;

/// <summary>
/// Reconciles workflow-definition versions from JSON files on disk into the catalog. A concrete
/// reconciliation feature that reads an author-authored JSON catalog (an array of
/// <see cref="WorkflowVersionReconciliationModel"/>) and contributes it as an
/// <see cref="IWorkflowReconciliationSource"/>.
/// </summary>
/// <remarks>
/// Because <see cref="WorkflowsDesignReconciliationFeature"/> is abstract (framework §2.5: inheritance is
/// the sanctioned cross-feature coupling for source-variant features), this feature <b>extends</b> it and
/// calls <c>base.ConfigureServices</c> so the reconciler, startup task, and universal handler are wired by
/// the base; it then contributes the reader and the JSON source. Enabling this feature therefore also arms
/// the reconcile lifecycle. It mirrors the Activities-side <c>JsonActivityReconciliation</c> source
/// (single <c>FilePath</c> XOR ordered <c>Files</c>, required <c>SourceId</c>). Opt-in — not enabled in any
/// default shell.
/// </remarks>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Design")]
[ManifestFeatureCategory("Reconciliation")]
[ShellFeature(
    name: "JsonWorkflowReconciliation",
    DisplayName = "Workflow Design JSON Reconciliation",
    Description = "Reconciliation source that reads workflow definitions from JSON files."
)]
public class JsonWorkflowReconciliationFeature : WorkflowsDesignReconciliationFeature
{
    public JsonWorkflowReconciliationOptions Options { get; set; } = new();

    public override void ConfigureServices(IServiceCollection services)
    {
        ValidateOptions();

        base.ConfigureServices(services);

        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(Options));
        services.AddScoped<IJsonWorkflowCatalogReader, JsonWorkflowCatalogReader>();
        services.AddScoped<IWorkflowReconciliationSource, JsonWorkflowReconciliationSource>();
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
                $"{nameof(JsonWorkflowReconciliationFeature)} requires a non-empty {nameof(JsonWorkflowReconciliationOptions.SourceId)}.");

        var hasSingleFile = !string.IsNullOrWhiteSpace(Options.FilePath);
        var hasFileList = Options.Files.Any();

        if (hasSingleFile == hasFileList)
            throw new InvalidOperationException(
                $"{nameof(JsonWorkflowReconciliationFeature)} requires exactly one of "
                + $"{nameof(JsonWorkflowReconciliationOptions.FilePath)} or {nameof(JsonWorkflowReconciliationOptions.Files)} "
                + "to be configured — not both, and not neither.");
    }
}
