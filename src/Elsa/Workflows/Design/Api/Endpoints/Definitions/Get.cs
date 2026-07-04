using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Api.Constants;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Requests;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Design.Api.Endpoints.Definitions;

/// <summary>
/// <c>GET design/workflows/definitions/{id}</c> — returns a definition with its current Draft state and version
/// summaries. Secured by construction through <c>ConfigurePermissions()</c> (W4); it never calls
/// <c>AllowAnonymous</c>.
/// </summary>
internal sealed class Get(IRequestSender requestSender, ILogger<Get> logger)
    : ElsaRequestHandlerEndpoint<GetDefinition, WorkflowDefinitionDetailsView>(requestSender, logger)
{
    public override void Configure()
    {
        Get(RouteConstants.GetRoute("definitions/{id}"));
        ConfigurePermissions();
    }
}
