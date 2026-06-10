using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api.Models;

namespace Elsa.Workflows.Runtime.Api.Requests;

public sealed record ExecuteWorkflow(string ArtifactId) : IRequest<WorkflowExecutionView>;
