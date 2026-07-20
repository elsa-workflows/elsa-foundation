using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// Drain-path pinned-executable read helper (spec 111). Routes the read through the burst-scoped cache
/// (<see cref="IWorkflowExecutableReader"/>) when available and falls back to the durable store otherwise. The reader
/// is registered by the runtime in production, so the fallback exists only for direct-construction unit tests that wire
/// a handler without the reader — both paths return byte-identical results (the artifact is immutable, ADR 0038), so
/// the fallback changes read count only, never behavior.
/// </summary>
public static class PinnedExecutableRead
{
    /// <summary>Constructor-injected form: prefer the injected reader, else the injected store.</summary>
    public static ValueTask<WorkflowExecutable?> FindAsync(
        IWorkflowExecutableReader? reader,
        IWorkflowExecutableStore store,
        string artifactId,
        CancellationToken cancellationToken) =>
        reader is not null
            ? reader.FindAsync(artifactId, cancellationToken)
            : store.FindAsync(artifactId, cancellationToken);

    /// <summary>Service-provider form for handlers that resolve per-invocation (Invoke/Parent/Resume).</summary>
    public static ValueTask<WorkflowExecutable?> FindAsync(
        IServiceProvider serviceProvider,
        string artifactId,
        CancellationToken cancellationToken)
    {
        var reader = serviceProvider.GetService<IWorkflowExecutableReader>();
        return reader is not null
            ? reader.FindAsync(artifactId, cancellationToken)
            : serviceProvider.GetRequiredService<IWorkflowExecutableStore>().FindAsync(artifactId, cancellationToken);
    }
}
