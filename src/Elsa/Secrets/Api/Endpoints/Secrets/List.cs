using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Secrets.Api.Constants;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Core.Permissions;
using FastEndpoints;

namespace Elsa.Secrets.Api.Endpoints.Secrets;

internal sealed class List(ISecretManager secretManager) : ElsaEndpoint<SecretQuery, ListSecretsResponse>
{
    public override void Configure()
    {
        Get(RouteConstants.GetRoute(""));
        ConfigurePermissions(SecretsPermissions.Read);
    }

    public override async Task HandleAsync(SecretQuery request, CancellationToken cancellationToken)
    {
        var tenantId = SecretEndpointTenant.Resolve(User);
        if (tenantId is null)
        {
            await Send.ForbiddenAsync(cancellationToken);
            return;
        }

        var page = await secretManager.ListAsync(tenantId, request, cancellationToken);
        await Send.OkAsync(ListSecretsResponse.FromPage(page), cancellationToken);
    }
}
