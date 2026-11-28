using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;

namespace Elsa.Workflows.Runtime.Api.Services;

/// <summary>
/// The executable inspection operations the runtime endpoints dispatch to. Each method takes the endpoint's
/// wire contract; a missing artifact or source reference surfaces as an <see cref="EntityNotFoundException"/>.
/// </summary>
public interface IWorkflowExecutableInspector
{
    Task<WorkflowExecutablesListView> ListAsync(ListWorkflowExecutables request, CancellationToken cancellationToken);
    Task<WorkflowExecutableDetailsView> GetAsync(GetWorkflowExecutable request, CancellationToken cancellationToken);
    Task<WorkflowExecutableInputSourcesView> GetInputSourcesAsync(GetWorkflowExecutableInputSources request, CancellationToken cancellationToken);
    Task<ExecutableProvenanceView> GetProvenanceAsync(GetWorkflowExecutableProvenance request, CancellationToken cancellationToken);
}
