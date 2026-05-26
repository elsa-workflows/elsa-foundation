using Elsa.FastEndpoints.Abstractions;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Api.Commands;
using Elsa.Workflows.Design.Api.Constants;
using Elsa.Workflows.Design.Core.Models;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Design.Api.Endpoints.Definitions;

internal sealed class Add(ICommandSender commandSender, ILogger<Add> logger) : ElsaCommandHandlerEndpoint<AddDefinition, WorkflowDefinitionVersionInfo>(commandSender, logger)
{
    public override void Configure()
    {
        Post(RouteConstants.Definitions);
        AllowAnonymous();
    }
}