using CShells.Lifecycle;
using Elsa.FastEndpoints.Abstractions;
using Elsa.Serialization.Core;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Elsa.Shells.FastEndpoints.Endpoints.ReloadAll;

[PublicAPI]
internal class ReloadAll(IShellRegistry shellRegistry, IPayloadSerializer serializer, ILogger<ReloadAll> logger) : ElsaEndpointWithoutRequest
{
    public override void Configure()
    {
        Post("/shells/reload");
        ConfigurePermissions("actions:shells:reload");
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
            var failedResponse = new ShellReloadResponse
            {
                Status = ShellReloadStatus.Failed,
                Timestamp = DateTimeOffset.UtcNow,
                Message = ex.Message
            };
            await SendResponseAsync(failedResponse, StatusCodes.Status503ServiceUnavailable, cancellationToken);
            return;
        }

        var response = new ShellReloadResponse
        {
            Status = ShellReloadStatus.Completed,
            Timestamp = DateTimeOffset.UtcNow
        };
        await SendResponseAsync(response, StatusCodes.Status200OK, cancellationToken);
    }

    private async Task SendResponseAsync(ShellReloadResponse response, int statusCode, CancellationToken cancellationToken)
    {
        var serializerOptions = serializer.GetOptions();
        HttpContext.Response.StatusCode = statusCode;
        await HttpContext.Response.WriteAsJsonAsync(response, serializerOptions, cancellationToken);
    }
}