using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Models;
using Elsa.FastEndpoints.Abstractions;
using Elsa.Mapping.Contracts;
using Elsa.Mediator.Core.Contracts;

namespace Elsa.Activities.Design.Api.Endpoints.Definitions;

internal sealed class Add : ElsaEndpoint<AddDefinitionCommand>
{
    private readonly ICommandSender commandSender;

    public Add(ICommandSender commandSender)
    {
        Post("design/activities/definitions");
        AllowAnonymous();
        this.commandSender = commandSender;
    }

    public override async Task HandleAsync(AddDefinitionCommand req, CancellationToken ct)
    {
        try
        {
            var result = await commandSender.Send(req, ct);
            await Send.OkAsync(result, ct);
        }
        catch(Exception e)
        {
            await SendError(e, cancellationToken: ct);
        }
    }
}

