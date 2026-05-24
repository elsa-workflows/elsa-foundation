using Elsa.FastEndpoints.Abstractions;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Api.Commands;
using Elsa.Workflows.Design.Api.Constants;
using Elsa.Workflows.Design.Api.Models;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Design.Api.Endpoints.Versions;

internal sealed class Add : ElsaCommandHandlerEndpoint<AddVersion, WorkflowDefinitionVersionDetailsView>
{
    public Add(ICommandSender commandSender, ILogger<Add> logger) : base(commandSender, logger)
    {
        Post(RouteConstants.Versions);
        AllowAnonymous();
    }
}