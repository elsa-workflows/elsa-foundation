using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Secrets.Api.Constants;
using Elsa.Secrets.Api.Requests;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Core.Permissions;
using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace Elsa.Secrets.Api.Endpoints.Secrets;

internal sealed class Delete(ISecretManager secretManager) : ElsaEndpoint<DeleteSecretRequest, EmptyResponse>
{
    public override void Configure()
    {
        Delete(RouteConstants.GetRoute("{name}"));
        ConfigurePermissions(SecretsPermissions.Delete);
    }

    public override async Task HandleAsync(DeleteSecretRequest request, CancellationToken cancellationToken)
    {
        var tenantId = SecretEndpointTenant.Resolve(User);
        if (tenantId is null)
        {
            await Send.ForbiddenAsync(cancellationToken);
            return;
        }

        var deleted = await secretManager.DeleteAsync(tenantId, request.Name, cancellationToken);

        if (!deleted)
        {
            await Send.NotFoundAsync(cancellationToken);
            return;
        }

        await Send.NoContentAsync(cancellationToken);
    }
}
