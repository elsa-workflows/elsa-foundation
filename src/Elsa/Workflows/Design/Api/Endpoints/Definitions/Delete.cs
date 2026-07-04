using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Api.Commands;
using Elsa.Workflows.Design.Api.Constants;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Design.Api.Endpoints.Definitions;

/// <summary>
/// <c>DELETE design/workflows/definitions/{id}</c> — permanently deletes a definition and everything it owns
/// (versions, current Draft, layout). Secured by construction through <c>ConfigurePermissions()</c> (W4); it
/// never calls <c>AllowAnonymous</c>. Individual published versions are immutable and have no delete endpoint.
/// </summary>
internal sealed class Delete(ICommandSender commandSender, ILogger<Delete> logger)
    : ElsaCommandHandlerEndpoint<DeleteDefinition>(commandSender, logger)
{
    public override void Configure()
    {
        Delete(RouteConstants.GetRoute("definitions/{id}"));
        ConfigurePermissions();
    }
}
