using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Secrets.Api.Constants;
using Elsa.Secrets.Api.Requests;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Core.Permissions;
using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace Elsa.Secrets.Api.Endpoints.Secrets;

internal sealed class Get(ISecretManager secretManager) : ElsaEndpoint<GetSecretRequest, SecretMetadata>
{
    public override void Configure()
    {
        Get(RouteConstants.GetRoute("{name}"));
        ConfigurePermissions(SecretsPermissions.Read);
    }

    public override async Task HandleAsync(GetSecretRequest request, CancellationToken cancellationToken)
    {
        var secret = await secretManager.FindAsync(request.Name, cancellationToken);

        if (secret is null)
        {
            await Send.NotFoundAsync(cancellationToken);
            return;
        }

        await Send.OkAsync(secret, cancellationToken);
    }
}
