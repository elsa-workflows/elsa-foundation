using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Workflows.Runtime.Reconciliation.Contracts;
using Elsa.Workflows.Runtime.Reconciliation.Core.Contracts;
using Elsa.Workflows.Runtime.Reconciliation.Core.Options;
using Elsa.Workflows.Runtime.Reconciliation.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Reconciliation;

/// <summary>
/// Imports portable workflow-executable closures from JSON files on disk and activates them — the feature that
/// lets a runtime with no design or publishing surface run workflows it was handed rather than ones it compiled.
/// </summary>
/// <remarks>
/// <para>
/// Extends <see cref="WorkflowsArtifactReconciliationFeature"/> and calls <c>base.ConfigureServices</c>, so the
/// reconciler, its startup pass and the runtime spine are wired by the base; this feature contributes only the
/// envelope reader and the JSON source. Opt-in — not enabled in any default shell.
/// </para>
/// <para>
/// It depends on <c>WorkflowsRuntimeTriggers</c> and not merely on <c>AddWorkflowRuntime()</c> because the trigger
/// binding/schedule/indexer spine is registered by that feature. Without it the activation coordinator refuses to
/// activate anything — correctly: an imported timer- or HTTP-started workflow with no trigger projection is a
/// definition that is live and can never fire, which is worse than a loud refusal at boot.
/// </para>
/// </remarks>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Runtime")]
[ManifestFeatureCategory("Reconciliation")]
[ShellFeature(
    name: "JsonWorkflowArtifactReconciliation",
    DisplayName = "Workflow Artifact JSON Reconciliation",
    Description = "Imports portable workflow-executable artifact closures from JSON files on disk and activates them, so a design-free runtime can execute artifacts built elsewhere.",
    DependsOn = new object[] { "Tasks", "WorkflowsRuntimeTriggers" })]
public class JsonWorkflowArtifactReconciliationFeature : WorkflowsArtifactReconciliationFeature
{
    public JsonWorkflowArtifactReconciliationOptions Options { get; set; } = new();

    public override void ConfigureServices(IServiceCollection services)
    {
        ValidateOptions();

        base.ConfigureServices(services);

        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(Options));
        services.AddScoped<IWorkflowArtifactClosureReader, JsonWorkflowArtifactClosureReader>();
        services.AddScoped<IWorkflowArtifactReconciliationSource, JsonWorkflowArtifactReconciliationSource>();
    }

    /// <summary>
    /// Fails fast at registration on an invalid composition: a required <c>SourceId</c>, and exactly one of
    /// <c>FilePath</c>, <c>Files</c>, or <c>FolderPath</c>.
    /// </summary>
    /// <remarks>
    /// <c>SourceId</c> is required rather than derived because it is the activation <b>ownership</b> descriptor —
    /// a generated or path-derived value would change when the mount moves, and the definitions this source owns
    /// would suddenly look foreign to it.
    /// </remarks>
    private void ValidateOptions()
    {
        if (string.IsNullOrWhiteSpace(Options.SourceId))
            throw new InvalidOperationException(
                $"{nameof(JsonWorkflowArtifactReconciliationFeature)} requires a non-empty {nameof(JsonWorkflowArtifactReconciliationOptions.SourceId)}.");

        var configuredPathOptions = new[]
        {
            !string.IsNullOrWhiteSpace(Options.FilePath),
            Options.Files.Any(),
            !string.IsNullOrWhiteSpace(Options.FolderPath),
        }.Count(configured => configured);

        if (configuredPathOptions != 1)
            throw new InvalidOperationException(
                $"{nameof(JsonWorkflowArtifactReconciliationFeature)} requires exactly one of "
                + $"{nameof(JsonWorkflowArtifactReconciliationOptions.FilePath)}, {nameof(JsonWorkflowArtifactReconciliationOptions.Files)}, "
                + $"or {nameof(JsonWorkflowArtifactReconciliationOptions.FolderPath)} to be configured — not several, and not none.");
    }
}
