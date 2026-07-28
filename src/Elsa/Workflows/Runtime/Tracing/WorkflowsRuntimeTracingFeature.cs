using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Workflows.Runtime.Core.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Runtime.Tracing;

/// <summary>
/// Opt-in host feature that turns on ENGINE tracing for the workflow runtime. It replaces the no-op
/// <see cref="IWorkflowEngineTracer"/> default (registered by the runtime core) with
/// <see cref="ActivitySourceWorkflowEngineTracer"/>, which emits <see cref="System.Diagnostics.Activity"/>
/// spans from the <c>Elsa.Workflows.Runtime</c> <see cref="System.Diagnostics.ActivitySource"/> for the four
/// engine phases: drain cycle, dispatch, activity execution, and checkpoint commit.
/// </summary>
/// <remarks>
/// This is engine self-observability, not a telemetry backend. The spans are surfaced to whatever
/// <see cref="System.Diagnostics.ActivityListener"/> the host attaches — for example the OpenTelemetry SDK, if the
/// host also composes the <c>Elsa.Diagnostics.OpenTelemetry</c> ingestion feature and subscribes the
/// <c>Elsa.Workflows.Runtime</c> source. With no listener attached the tracer allocates nothing per phase
/// (<see cref="System.Diagnostics.ActivitySource.StartActivity(string)"/> returns null), so composing this feature is
/// safe by default. See <c>docs/reference/engine-telemetry.md</c> for the engine-vs-ingestion distinction.
/// </remarks>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Runtime")]
[ManifestFeatureCategory("Diagnostics")]
[ShellFeature(
    name: "WorkflowsRuntimeTracing",
    DisplayName = "Workflows Runtime Tracing",
    Description = "Emits engine ActivitySource spans (drain, dispatch, activity execution, checkpoint commit) from the Elsa.Workflows.Runtime source for whatever ActivityListener the host attaches. Zero per-phase allocation when no listener is attached. This is engine self-observability, distinct from the OpenTelemetry ingestion feature that exports telemetry to a backend.")]
public sealed class WorkflowsRuntimeTracingFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        // Swap the no-op default for the real ActivitySource-backed tracer. Replace (rather than TryAdd) so this
        // wins regardless of whether the runtime core's TryAdd default ran before or after this feature: if the
        // no-op was already registered it is removed first; if the core registers afterwards its TryAdd no-ops
        // against our descriptor. The tracer is IDisposable and owns its ActivitySource — the container disposes it.
        //
        // The explicit factory (not a Type-based descriptor) is load-bearing: the tracer also has an
        // ActivitySource-accepting constructor, and ASP.NET Core hosts register an ActivitySource singleton
        // (named "Microsoft.AspNetCore"), so type activation would greedily inject that source and the engine
        // spans would be emitted on the wrong source name — invisible to any "Elsa.Workflows.Runtime" listener.
        services.Replace(ServiceDescriptor.Singleton<IWorkflowEngineTracer>(_ => new ActivitySourceWorkflowEngineTracer()));
    }
}
