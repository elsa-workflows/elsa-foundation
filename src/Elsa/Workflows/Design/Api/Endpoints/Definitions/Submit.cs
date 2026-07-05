using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Api.Commands;
using Elsa.Workflows.Design.Api.Constants;
using Elsa.Workflows.Design.Api.Models;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Design.Api.Endpoints.Definitions;

internal sealed class Submit(ICommandSender commandSender, ILogger<Submit> logger)
    : ElsaCommandHandlerEndpoint<SubmitDefinition, SubmittedWorkflowDefinitionView>(commandSender, logger)
{
    public override void Configure()
    {
        Post(RouteConstants.GetRoute("definitions/submit"));
        ConfigurePermissions();
    }
}
