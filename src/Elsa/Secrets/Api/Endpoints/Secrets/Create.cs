using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Secrets.Api.Constants;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Core.Permissions;
using FastEndpoints;

namespace Elsa.Secrets.Api.Endpoints.Secrets;

internal sealed class Create(ISecretManager secretManager) : ElsaEndpoint<CreateSecretRequest, SecretMetadata>
{
    public override void Configure()
    {
        Post(RouteConstants.GetRoute(""));
        ConfigurePermissions(SecretsPermissions.Write);
    }

    public override async Task HandleAsync(CreateSecretRequest request, CancellationToken cancellationToken)
    {
        var secret = await secretManager.CreateAsync(request, cancellationToken);
        await Send.ResponseAsync(secret, 201, cancellationToken);
    }
}
