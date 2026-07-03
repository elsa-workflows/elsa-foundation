using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Runtime.Api;

/// <summary>
/// Triggers + global stimulus routing (W7) — the service Elsa 4 was missing that closes the single largest
/// parity gap with Elsa 3. It registers the durable trigger index over published artifacts (E3-1), the
/// cross-execution bookmark stimulus index and lookup (E3-5), and the <see cref="IStimulusRouter"/> that turns
/// an external stimulus with no explicit execution id into new workflow starts and/or fan-in resumes. The
/// <c>POST runtime/workflows/stimuli</c> endpoint (in this assembly, discovered through the Runtime API feature
/// it depends on) is the reachable integration surface.
/// </summary>
/// <remarks>
/// <para>
/// Depends on <c>WorkflowsRuntimeApi</c> for the runtime stores and dispatchers it composes over: the bookmark
/// state store (which also serves the cross-execution <see cref="IBookmarkStimulusIndex"/>), the start
/// dispatcher, and the bookmark resume dispatcher. All dispatch is routed through those dispatchers so the
/// single-writer agent-mailbox invariant is preserved (W5); the router never bypasses them.
/// </para>
/// <para>
/// The <see cref="IBookmarkStimulusIndex"/> is bridged to the same <see cref="IBookmarkStateStore"/> singleton
/// the runtime already owns, so no separate index document is maintained: whichever store implementation is
/// active (in-memory here, Groundwork when a durable persistence feature is composed) also answers the
/// cross-execution stimulus query. The concrete <see cref="IActivityTriggerStimulusProvider"/> set that lets the
/// publish-time extractor recognize trigger activities is contributed by the activity features that own those
/// activities (e.g. the Event trigger by <c>ActivitiesPrimitives</c>), not here.
/// </para>
/// </remarks>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Runtime")]
[ManifestFeatureCategory("Triggers")]
[ShellFeature(
    name: "WorkflowsRuntimeTriggers",
    DisplayName = "Workflows Runtime Triggers",
    Description = "Trigger index over published workflows plus global stimulus routing: start new instances from an external stimulus with no explicit execution id, and fan a single stimulus in to every waiting instance across executions. Exposes POST runtime/workflows/stimuli. Compose alongside the Workflows Runtime API.",
    DependsOn = new object[] { "WorkflowsRuntimeApi" })]
public sealed class WorkflowsRuntimeTriggersFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        // Durable trigger index over PUBLISHED artifacts (E3-1). In-memory default; a durable persistence feature
        // (Groundwork) swaps the store via its own registration, so this stays a TryAdd.
        services.TryAddSingleton<IWorkflowTriggerBindingStore, InMemoryWorkflowTriggerBindingStore>();
        services.TryAddSingleton<IWorkflowTriggerBindingExtractor, WorkflowTriggerBindingExtractor>();
        services.TryAddSingleton<IWorkflowTriggerIndexer, WorkflowTriggerIndexer>();

        // Cross-execution bookmark stimulus index (E3-5), bridged onto the same bookmark state store the runtime
        // already owns so there is no second index document to keep consistent.
        services.TryAddSingleton<IBookmarkStimulusIndex>(serviceProvider =>
            (IBookmarkStimulusIndex)serviceProvider.GetRequiredService<IBookmarkStateStore>());
        services.TryAddSingleton<IGlobalBookmarkStimulusLookup, GlobalBookmarkStimulusLookup>();

        // Narrow, best-effort start-path dedup for at-least-once delivery (Condition A). Not durable by design.
        services.TryAddSingleton<IStimulusStartDeduplicator, InMemoryStimulusStartDeduplicator>();

        // The routing spine: start (E3-1) + fan-in resume (E3-5) over the two indexes and the runtime dispatchers.
        services.TryAddSingleton<IStimulusRouter, StimulusRouter>();
    }
}
