using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Store for <see cref="WorkflowExecutableSourceReference"/> records — the per-publish pointers into the single
/// content-addressed artifact store (ADR 0038/0039/0040). Provides the query and retirement primitives the future
/// GC sweep (a two-query prune of expired/retired references then unreferenced artifacts) is built from; the GC
/// service itself is a separate slice.
/// </summary>
public interface IWorkflowExecutableSourceReferenceStore :
    IWorkflowExecutableSourceReferenceReader,
    IWorkflowExecutableSourceReferenceWriter
{
}
