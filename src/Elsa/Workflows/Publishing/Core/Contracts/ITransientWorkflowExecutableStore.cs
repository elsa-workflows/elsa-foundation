using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Core.Contracts;

/// <summary>
/// Facade over the single artifact store for expiring test-run executables. Scope/expiry are reference facts now
/// (ADR 0040), so the artifact itself no longer carries an expiry; the facade tracks it alongside. This is a
/// transitional shim — the reference-driven test-run rewiring is worker W3's slice.
/// </summary>
public interface ITransientWorkflowExecutableStore
{
    ValueTask SaveAsync(WorkflowExecutable executable, DateTimeOffset expiresAt, CancellationToken cancellationToken = default);

    ValueTask<WorkflowExecutable?> FindAsync(string artifactId, CancellationToken cancellationToken = default);

    ValueTask<int> CleanupExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
}
