using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Secrets.Api.Constants;
using Elsa.Secrets.Api.Requests;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Core.Permissions;
using FastEndpoints;

namespace Elsa.Secrets.Api.Endpoints.Secrets;

internal sealed class Update(ISecretManager secretManager) : ElsaEndpoint<UpdateSecretApiRequest, SecretMetadata>
{
    public override void Configure()
    {
        Put(RouteConstants.GetRoute("{name}"));
        ConfigurePermissions(SecretsPermissions.Write);
    }

    public override async Task HandleAsync(UpdateSecretApiRequest request, CancellationToken cancellationToken)
    {
        var tenantId = SecretEndpointTenant.Resolve(User);
        if (tenantId is null)
        {
            await Send.ForbiddenAsync(cancellationToken);
            return;
        }

        var secret = await secretManager.UpdateAsync(tenantId, request.Name, new UpdateSecretMetadataRequest
        {
            DisplayName = request.DisplayName,
            Description = request.Description
        }, cancellationToken);
        await Send.OkAsync(secret, cancellationToken);
    }
}
