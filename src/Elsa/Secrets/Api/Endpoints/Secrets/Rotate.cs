using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Secrets.Api.Constants;
using Elsa.Secrets.Api.Requests;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Core.Permissions;
using FastEndpoints;

namespace Elsa.Secrets.Api.Endpoints.Secrets;

internal sealed class Rotate(ISecretManager secretManager) : ElsaEndpoint<RotateSecretApiRequest, SecretMetadata>
{
    public override void Configure()
    {
        Post(RouteConstants.GetRoute("{name}/rotate"));
        ConfigurePermissions(SecretsPermissions.UpdateValue);
    }

    public override async Task HandleAsync(RotateSecretApiRequest request, CancellationToken cancellationToken)
    {
        var tenantId = SecretEndpointTenant.Resolve(User);
        if (tenantId is null)
        {
            await Send.ForbiddenAsync(cancellationToken);
            return;
        }

        var secret = await secretManager.RotateAsync(tenantId, request.Name, new RotateSecretRequest
        {
            Value = request.Value,
            ConfigurationKey = request.ConfigurationKey,
            ExpiresAt = request.ExpiresAt,
            Metadata = request.Metadata
        }, cancellationToken);
        await Send.OkAsync(secret, cancellationToken);
    }
}
