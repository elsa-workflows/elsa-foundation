using Elsa.Primitives.Contracts;
using Elsa.Server.Constants;
using Elsa.Server.Shells.Endpoints.Models;
using FastEndpoints;

namespace Elsa.Server.Shells.Endpoints;

internal abstract class ShellReloadEndpoint : EndpointWithoutRequest
{
    protected Task SendErrorResponseAsync(string? shellId, string errorMessage, CancellationToken cancellationToken)
    {
        return SendResponseAsync(
            new ShellReloadResponse
            {
                Status = ShellReloadStatus.Failed,
                RequestedShellId = shellId,
                Timestamp = DateTimeOffset.UtcNow,
                Message = errorMessage
            },
            StatusCodes.Status500InternalServerError,
            cancellationToken
        );
    }

    protected Task SendSuccessResponse(CancellationToken cancellationToken)
    {
        var response = new ShellReloadResponse
        {
            Status = ShellReloadStatus.Completed,
            Timestamp = DateTimeOffset.UtcNow
        };

        return SendResponseAsync(response, StatusCodes.Status200OK, cancellationToken);
    }

    protected Task SendNotFoundResponse(string shellId, string errorMessage, CancellationToken cancellationToken)
    {
        var notFoundResponse = new ShellReloadResponse
        {
            Status = ShellReloadStatus.NotFound,
            RequestedShellId = shellId,
            Timestamp = DateTimeOffset.UtcNow,
            Message = errorMessage
        };

        return SendResponseAsync(notFoundResponse, StatusCodes.Status404NotFound, cancellationToken);
    }

    protected async Task SendResponseAsync(ShellReloadResponse response, int statusCode, CancellationToken cancellationToken)
    {
        var serializerOptions = JsonSerializerOptionsCache.Instance;
        HttpContext.Response.StatusCode = statusCode;
        await HttpContext.Response.WriteAsJsonAsync(response, serializerOptions, cancellationToken);
    }
}
