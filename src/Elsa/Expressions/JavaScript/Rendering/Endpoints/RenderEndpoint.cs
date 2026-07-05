using Elsa.Expressions.JavaScript.Rendering.Core.Contracts;
using Elsa.Api.FastEndpoints.Abstractions;

namespace Elsa.Expressions.JavaScript.Rendering.Endpoints;

internal sealed class RenderEndpoint(IJavaScriptDeclarationsDocumentRenderer renderer, IJavaScriptDeclarationsDocumentFactory documentFactory) : ElsaEndpointWithoutRequest
{
    public override void Configure()
    {
        ConfigurePermissions();
        Get("javascript/documents/render");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        try
        {
            var document = await documentFactory.Create(ct);
            var result = renderer.Render(document);

            await Send.OkAsync(new { success = true, document = result }, ct);
        }
        catch (Exception e)
        {
            await Send.ResponseAsync(
                new { success = false, message = e.Message },
                500,
                ct
            );
        }
    }
}
