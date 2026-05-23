using Elsa.Activities.Design.Api.Commands;
using Elsa.FastEndpoints.Abstractions;
using Elsa.Mediator.Core.Contracts;

namespace Elsa.Activities.Design.Api.Endpoints.Versions;

internal sealed class Add : ElsaEndpoint<AddVersionCommand>
{
    private readonly ICommandSender commandSender;

    public Add(ICommandSender commandSender)
    {
        Post("design/activities/versions");
        AllowAnonymous();
        this.commandSender = commandSender;
    }

    public override async Task HandleAsync(AddVersionCommand req, CancellationToken ct)
    {
        try
        {
            var result = await commandSender.Send(req, ct);
            await Send.OkAsync(result, ct);
        }
        catch (Exception e)
        {
            await SendError(e, cancellationToken: ct);
        }
    }
}