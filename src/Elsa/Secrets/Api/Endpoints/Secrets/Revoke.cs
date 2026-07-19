using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Secrets.Api.Constants;
using Elsa.Secrets.Api.Requests;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Core.Permissions;
using FastEndpoints;

namespace Elsa.Secrets.Api.Endpoints.Secrets;

internal sealed class Revoke(ISecretManager secretManager) : ElsaEndpoint<RevokeSecretRequest, SecretMetadata>
{
    public override void Configure()
    {
        Post(RouteConstants.GetRoute("{name}/revoke"));
        ConfigurePermissions(SecretsPermissions.Delete);
    }

    public override async Task HandleAsync(RevokeSecretRequest request, CancellationToken cancellationToken)
    {
        var tenantId = SecretEndpointTenant.Resolve(User);
        if (tenantId is null)
        {
            await Send.ForbiddenAsync(cancellationToken);
            return;
        }

        var secret = await secretManager.RevokeAsync(tenantId, request.Name, cancellationToken);

        if (secret is null)
        {
            await Send.NotFoundAsync(cancellationToken);
            return;
        }

        await Send.OkAsync(secret, cancellationToken);
    }
}
