using CShells.Features;
using Elsa.Specifications.PackageManifest.Generator.Hints;
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
/// Because <see cref="WorkflowsDesignReconciliationFeature"/> is abstract (inheritance is
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
    Description = "Reconciliation source that reads workflow definitions from JSON files.",
    // The reconciler admits each item through the credential-literal rule.
    DependsOn = new object[] { "WorkflowDesignValidations" }
)]
public class JsonWorkflowReconciliationFeature : WorkflowsDesignReconciliationFeature
{
    public JsonWorkflowReconciliationOptions Options { get; set; } = new();

    /// <summary>
    /// Additional JSON catalogs with independent source identities, paths and publication flags.
    /// Each entry follows the same rules as <see cref="Options"/>; source identities must be distinct.
    /// </summary>
    public IEnumerable<JsonWorkflowReconciliationOptions> AdditionalSources { get; set; } = [];

    public override void ConfigureServices(IServiceCollection services)
    {
        var additionalSources = AdditionalSources.ToArray();
        var sourceIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var options in new[] { Options }.Concat(additionalSources))
        {
            ValidateOptions(options);
            if (!sourceIds.Add(options.SourceId))
                throw new InvalidOperationException($"{nameof(JsonWorkflowReconciliationFeature)} requires distinct source identities; duplicate {nameof(JsonWorkflowReconciliationOptions.SourceId)} '{options.SourceId}'.");
        }

        base.ConfigureServices(services);

        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(Options));
        services.AddScoped<IJsonWorkflowCatalogReader, JsonWorkflowCatalogReader>();
        services.AddScoped<IWorkflowReconciliationSource, JsonWorkflowReconciliationSource>();
        foreach (var options in additionalSources)
            services.AddScoped<IWorkflowReconciliationSource>(provider =>
                ActivatorUtilities.CreateInstance<JsonWorkflowReconciliationSource>(provider, Microsoft.Extensions.Options.Options.Create(options)));
    }

    /// <summary>
    /// Fails fast at registration on an invalid composition: a required <c>SourceId</c>, and exactly one
    /// of <c>FilePath</c> (single-file shorthand), <c>Files</c> (ordered list), or <c>FolderPath</c>
    /// (scanned directory) — never more than one, never none. Centralising the check here keeps the
    /// source itself free of configuration policy.
    /// </summary>
    private static void ValidateOptions(JsonWorkflowReconciliationOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.SourceId))
            throw new InvalidOperationException(
                $"{nameof(JsonWorkflowReconciliationFeature)} requires a non-empty {nameof(JsonWorkflowReconciliationOptions.SourceId)}.");

        var configuredPathOptions = new[]
        {
            !string.IsNullOrWhiteSpace(options.FilePath),
            options.Files.Any(),
            !string.IsNullOrWhiteSpace(options.FolderPath),
        }.Count(configured => configured);

        if (configuredPathOptions != 1)
            throw new InvalidOperationException(
                $"{nameof(JsonWorkflowReconciliationFeature)} requires exactly one of "
                + $"{nameof(JsonWorkflowReconciliationOptions.FilePath)}, {nameof(JsonWorkflowReconciliationOptions.Files)}, "
                + $"or {nameof(JsonWorkflowReconciliationOptions.FolderPath)} to be configured — not several, and not none.");
    }
}
