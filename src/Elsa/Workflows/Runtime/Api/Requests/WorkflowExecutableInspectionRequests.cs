using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api.Models;

namespace Elsa.Workflows.Runtime.Api.Requests;

public sealed record ListWorkflowExecutables(
    WorkflowExecutableListScope Scope = WorkflowExecutableListScope.Published,
    bool IncludeRetired = false) : IRequest<WorkflowExecutablesListView>;

public sealed record GetWorkflowExecutable(string ArtifactId) : IRequest<WorkflowExecutableDetailsView>;

public sealed record GetWorkflowExecutableProvenance(string ArtifactId) : IRequest<ExecutableProvenanceView>;
