using System.Diagnostics;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.Diagnostics.OpenTelemetry.Core.Contracts;
using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Diagnostics.OpenTelemetry.Workflows;

/// <summary>
/// Bridges the workflow engine's self-instrumentation into the OpenTelemetry ingestion domain, so Studio's timing view
/// is populated in a host that runs no OpenTelemetry SDK exporter.
/// </summary>
/// <remarks>
/// <para>
/// The two telemetry surfaces are independent by design (see <c>docs/reference/engine-telemetry.md</c>): the engine
/// <b>emits</b> <see cref="ActivitySource"/> spans on <see cref="WorkflowEngineTelemetry.ActivitySourceName"/>, while
/// <c>DiagnosticsOpenTelemetry</c> is a <b>backend</b> that only receives OTLP telemetry pushed by other processes. With
/// neither an OpenTelemetry SDK exporter nor a listener wired up, nothing subscribes the engine source and nothing lands
/// in the ingestion store, so <c>POST /diagnostics/opentelemetry/traces/search</c> stays empty even after workflow runs.
/// </para>
/// <para>
/// This feature closes that gap the way the docs prescribe — "a host wires ... a bare listener to the source name" —
/// without adding an OpenTelemetry SDK or an HTTP loopback exporter (which would fight the dev TLS cert). It attaches an
/// in-process <see cref="ActivityListener"/> that samples the engine source and, when the outermost drain span of a
/// trace completes, folds its span tree into an <see cref="OpenTelemetryBatch"/> and feeds it to <see cref="IOpenTelemetryIngestor"/> — the same
/// contract the OTLP/HTTP receiver uses — so the batch flows through redaction, the store, and the live feed unchanged.
/// </para>
/// </remarks>
[ShellFeature(
    name: "DiagnosticsOpenTelemetryEngineBridge",
    DisplayName = "Diagnostics: OpenTelemetry Engine Bridge",
    Description = "Forwards the workflow engine's ActivitySource spans into the OpenTelemetry ingestion store so the timing view is populated in a self-contained host.",
    DependsOn = new object[] { "DiagnosticsOpenTelemetry", "WorkflowsRuntimeTracing" })]
// Must be public: CShells feature discovery scans exported types only, so an internal feature class never
// enters the runtime feature catalog and is silently dropped from every shell that requests it.
public sealed class EngineTracingBridgeFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<EngineTracingBridge>();
        services.AddScoped<IStartupTask, BridgeStartupTask>();
        services.AddShellTerminator<BridgeTerminator>(LifecyclePhase.Default, 0);
    }
}

/// <summary>Attaches the bridge listener once the shell has started.</summary>
internal sealed class BridgeStartupTask(EngineTracingBridge bridge) : IStartupTask
{
    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        bridge.Start();
        return Task.CompletedTask;
    }
}

/// <summary>Detaches the bridge listener on graceful shutdown.</summary>
internal sealed class BridgeTerminator(EngineTracingBridge bridge) : IShellTerminator
{
    public Task TerminateAsync(CancellationToken cancellationToken = default)
    {
        bridge.Stop();
        return Task.CompletedTask;
    }
}
