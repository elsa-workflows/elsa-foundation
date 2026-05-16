using CShells.Features;
using CShells.Lifecycle;
using CShells.Management;
using Elsa.Abstractions;
using Elsa.FastEndpoints.Abstractions;
using Elsa.Serialization.Core;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Http;

namespace Elsa.Shells.FastEndpoints.Endpoints.Reload;

[PublicAPI]
internal class Reload(IShellRegistry shellRegistry, IPayloadSerializer serializer) : ElsaEndpointWithoutRequest
{
    public override void Configure()
    {
        Post("/shells/{shellId}/reload");
        ConfigurePermissions("actions:shells:reload");
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var shellId = Route<string>("shellId")!;

        try
        {
            await shellRegistry.ReloadAsync(shellId, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            // Shell not found by the provider.
            var notFoundResponse = new ShellReloadResponse
            {
                Status = ShellReloadStatus.NotFound,
                RequestedShellId = shellId,
                Timestamp = DateTimeOffset.UtcNow,
                Message = ex.Message
            };
            await SendResponseAsync(notFoundResponse, StatusCodes.Status404NotFound, cancellationToken);
            return;
        }

        var response = new ShellReloadResponse
        {
            Status = ShellReloadStatus.Completed,
            RequestedShellId = shellId,
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
