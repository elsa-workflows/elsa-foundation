using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Workflows.Runtime.Reconciliation.Contracts;
using Elsa.Workflows.Runtime.Reconciliation.Core.Contracts;
using Elsa.Workflows.Runtime.Reconciliation.Core.Options;
using Elsa.Workflows.Runtime.Reconciliation.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

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
/// <para>
/// <b>A locking feature must also be composed.</b> The reconcile pass is a <c>[SingleNodeTask]</c> guarded by
/// <c>IDistributedLockProvider</c>, and <b>no default is registered anywhere in the framework — deliberately</b>.
/// A process-local stand-in would satisfy DI, behave perfectly on one node, and then silently let two nodes
/// reconcile the same mount concurrently, which is the exact condition the single-node guard exists to prevent.
/// Absence of a default is the safety property, not an oversight: composing without a locking feature fails at
/// shell activation, when DI constructs this feature's startup task, and cannot be shipped past. (No production
/// path sets <c>ValidateOnBuild</c>, so the refusal is a resolution failure at activation rather than at container
/// build — loud and unstartable either way, but it is not caught earlier than that.)
/// </para>
/// <para>
/// <b>The requirement is inherited, not introduced.</b> <c>Elsa.Tasks</c> — which this feature already declares in
/// <c>DependsOn</c> — resolves <c>IDistributedLockProvider</c> in <c>TaskExecutor</c>'s constructor, so every Tasks
/// consumer carries it. That is why no guard is added here: a fail-fast of our own would sit behind Tasks' identical
/// failure and never be reached. It is deliberately not a <c>DependsOn</c> on a locking <em>feature</em>, because
/// naming one would pin a provider choice — <c>FileSystemDistributedLocking</c> coordinates through the file system,
/// so hard-wiring it would silently hand a multi-node deployment single-host locking. Depending on the abstraction
/// and letting composition choose the provider is the seam; the design-side reconcilers carry it the same way.
/// <c>Elsa.Locking.FileSystem</c> is the only provider this repository ships and is sufficient for a single host.
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

        // Capture this feature instance's options in the source registration. Registering IOptions<T> globally
        // makes two JSON features share the last options registration, so both sources read the same mount and
        // claim the same owner. A snapshot also prevents later feature mutation from changing a built container.
        var options = SnapshotOptions();
        // TryAdd for the reader (§2.6.2: single implementation, first-wins) — plain Add for the
        // source, which is a fan-in contribution: several mounts legitimately contribute several sources.
        services.TryAddScoped<IWorkflowArtifactClosureReader, JsonWorkflowArtifactClosureReader>();
        services.AddScoped<IWorkflowArtifactReconciliationSource>(serviceProvider =>
            new JsonWorkflowArtifactReconciliationSource(
                serviceProvider.GetRequiredService<IWorkflowArtifactClosureReader>(),
                Microsoft.Extensions.Options.Options.Create(options),
                serviceProvider.GetRequiredService<ILogger<JsonWorkflowArtifactReconciliationSource>>()));
    }

    private JsonWorkflowArtifactReconciliationOptions SnapshotOptions() =>
        new()
        {
            FilePath = Options.FilePath,
            Files = Options.Files.ToArray(),
            FolderPath = Options.FolderPath,
            SourceId = Options.SourceId,
            TenantId = Options.TenantId,
        };

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
