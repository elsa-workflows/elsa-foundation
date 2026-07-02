using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Foundation.Identity.Api.Constants;

namespace Elsa.Foundation.Identity.Api.Endpoints;

internal sealed class Session(IAuthSessionService sessionService) : ElsaEndpointWithoutRequest<AuthSession>
{
    public override void Configure()
    {
        Get(IdentityRouteConstants.GetRoute("session"));
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var session = await sessionService.GetAsync(User, ct);
        await Send.OkAsync(session, ct);
    }
}
