using CShells.Lifecycle;

namespace Elsa.Server.Shells.Endpoints;

internal sealed class Reload(
    IShellRegistry shellRegistry)
    : ShellReloadEndpoint
{
    public override void Configure()
    {
        Post("shells/admin/{shellId}/reload");
        //ConfigurePermissions("actions:shells:reload");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var shellId = Route<string>("shellId")!;

        try
        {
            var reloadResult = await shellRegistry.ReloadAsync(shellId, cancellationToken);
            if (reloadResult.Error is not null)
            {
                await SendErrorResponseAsync(shellId, reloadResult.Error.Message, cancellationToken);
                return;
            }
        }
        catch (InvalidOperationException ex)
        {
            await SendNotFoundResponse(shellId, ex.Message, cancellationToken);
            return;
        }

        await SendSuccessResponse(cancellationToken);
    }
}
