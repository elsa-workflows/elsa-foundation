using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Api.Commands;
using Elsa.Workflows.Design.Api.Constants;
using Elsa.Workflows.Design.Api.Models;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Design.Api.Endpoints.Versions;

internal sealed class Add(ICommandSender commandSender, ILogger<Add> logger) : ElsaCommandHandlerEndpoint<AddVersion, WorkflowDefinitionVersionDetailsView>(commandSender, logger)
{
    public override void Configure()
    {
        Post(RouteConstants.Versions);
        AllowAnonymous();
    }
}