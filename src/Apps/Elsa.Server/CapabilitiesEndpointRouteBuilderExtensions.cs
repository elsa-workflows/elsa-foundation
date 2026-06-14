using Elsa.Features.Abstractions;

namespace Elsa.Server;

internal static class CapabilitiesEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapElsaCapabilitiesApi(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/_elsa/capabilities", async (
            IElsaCapabilityProvider capabilityProvider,
            CancellationToken cancellationToken) =>
        {
            var capabilities = await capabilityProvider.GetCapabilitiesAsync(cancellationToken);
            return Results.Ok(new ElsaCapabilitiesResponse(capabilities));
        });

        return endpoints;
    }
}

internal sealed record ElsaCapabilitiesResponse(IReadOnlyCollection<ElsaCapability> Capabilities);
