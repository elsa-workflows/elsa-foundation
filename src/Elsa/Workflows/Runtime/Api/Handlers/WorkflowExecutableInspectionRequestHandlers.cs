using Elsa.Mediator.Core.Contracts;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Api.Services;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Api.Handlers;

public sealed class ListWorkflowExecutablesRequestHandler(WorkflowExecutableInspector inspector)
    : IRequestHandler<ListWorkflowExecutables, WorkflowExecutablesListView>
{
    public async Task<WorkflowExecutablesListView> Handle(ListWorkflowExecutables request, CancellationToken cancellationToken) =>
        await inspector.ListAsync(request.Scope, request.IncludeRetired, cancellationToken);
}

public sealed class GetWorkflowExecutableRequestHandler(WorkflowExecutableInspector inspector)
    : IRequestHandler<GetWorkflowExecutable, WorkflowExecutableDetailsView>
{
    public async Task<WorkflowExecutableDetailsView> Handle(GetWorkflowExecutable request, CancellationToken cancellationToken) =>
        await inspector.GetAsync(request.ArtifactId, request.Ref, cancellationToken)
        ?? throw EntityNotFoundException.ForEntity(typeof(WorkflowExecutable), request.ArtifactId);
}

public sealed class GetWorkflowExecutableInputSourcesRequestHandler(WorkflowExecutableInspector inspector)
    : IRequestHandler<GetWorkflowExecutableInputSources, WorkflowExecutableInputSourcesView>
{
    public async Task<WorkflowExecutableInputSourcesView> Handle(GetWorkflowExecutableInputSources request, CancellationToken cancellationToken) =>
        await inspector.GetInputSourcesAsync(request.ArtifactId, request.SourceReferenceId, cancellationToken)
        ?? throw EntityNotFoundException.ForEntity(typeof(WorkflowExecutableSourceReference), request.SourceReferenceId);
}

public sealed class GetWorkflowExecutableProvenanceRequestHandler(WorkflowExecutableInspector inspector)
    : IRequestHandler<GetWorkflowExecutableProvenance, ExecutableProvenanceView>
{
    public async Task<ExecutableProvenanceView> Handle(GetWorkflowExecutableProvenance request, CancellationToken cancellationToken) =>
        await inspector.GetProvenanceAsync(request.ArtifactId, cancellationToken)
        ?? throw EntityNotFoundException.ForEntity(typeof(WorkflowExecutable), request.ArtifactId);
}
