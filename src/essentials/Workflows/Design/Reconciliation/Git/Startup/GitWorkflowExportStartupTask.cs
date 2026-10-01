using Elsa.Tasks.Core;
using Elsa.Tasks.Core.Attributes;
using Elsa.Workflows.Design.Reconciliation.Git.Contracts;

namespace Elsa.Workflows.Design.Reconciliation.Git.Startup;

/// <summary>
/// Runs the Writer-only export reconciler once at shell start. Ordered after the import reconcile task ([Order(2)]) so
/// a bootstrap import seeds a fresh catalog before the first export. Registered only when Role=Writer.
/// </summary>
/// <remarks>
/// It takes no lock and is not a <c>[SingleNodeTask]</c> (#2197): every Writer node runs it. One writer per repository
/// branch is kept by the remote, not by a lock: the exporter's push is a fast-forward or nothing, and a writer that loses
/// that race rebuilds onto the winner's commits (see <see cref="Services.GitWorkflowExporter"/>). A lock could take the
/// nodes in turn but not keep their clones in line, since each node exports from its own.
/// </remarks>
[Order(3)]
public sealed class GitWorkflowExportStartupTask(IGitWorkflowExporter exporter) : IStartupTask
{
    public Task ExecuteAsync(CancellationToken cancellationToken) => exporter.ExportAsync(cancellationToken);
}
