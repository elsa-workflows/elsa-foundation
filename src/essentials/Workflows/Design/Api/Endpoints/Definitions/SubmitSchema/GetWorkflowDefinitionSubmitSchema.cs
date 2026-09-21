using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Api.Models;

namespace Elsa.Workflows.Design.Api.Endpoints.Definitions.SubmitSchema;

/// <summary>
/// Requests the versioned JSON Schema of the <c>POST design/workflows/definitions/submit</c> body.
/// Backs <c>GET design/workflows/definitions/submit/schema</c> for headless authoring clients.
/// </summary>
public sealed record GetWorkflowDefinitionSubmitSchema : IRequest<WorkflowDefinitionSubmitSchemaView>;
