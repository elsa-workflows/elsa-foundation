using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Constants;
using Elsa.Activities.Design.Api.Models;
using Elsa.FastEndpoints.Abstractions;
using Elsa.Mediator.Core.Contracts;
using Microsoft.Extensions.Logging;

namespace Elsa.Activities.Design.Api.Endpoints.Versions;

internal sealed class Add(ICommandSender commandSender, ILogger<Add> logger) : ElsaCommandHandlerEndpoint<AddVersion, ActivityDefinitionVersionDetailsView>(commandSender, logger)
{
    public override void Configure()
    {
        Post(RouteConstants.Versions);
        AllowAnonymous();
    }
}