using CShells.Lifecycle;

namespace Elsa.Server.Shells.Endpoints;

internal sealed class ReloadAll(
    IShellRegistry shellRegistry,
    ILogger<ReloadAll> logger)
    : ShellReloadEndpoint
{
    public override void Configure()
    {
        Post("shells/admin/reload-all");
        //ConfigurePermissions("actions:shells:reload");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        try
        {
            var activeShells = shellRegistry.GetActiveShells().ToArray();
            foreach (var shell in activeShells)
            {
                await shellRegistry.ReloadAsync(shell.Descriptor.Name, cancellationToken);
            }
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError(ex, "Failed to reload all shells");
            await SendErrorResponseAsync(null, ex.Message, cancellationToken);
            return;
        }

        await SendSuccessResponse(cancellationToken);
    }
}
