using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Secrets.Api.Constants;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Core.Permissions;
using FastEndpoints;

namespace Elsa.Secrets.Api.Endpoints.Secrets;

internal sealed class Descriptors(ISecretTypeRegistry typeRegistry, ISecretStoreRegistry storeRegistry) : ElsaEndpointWithoutRequest<SecretDescriptorsResponse>
{
    public override void Configure()
    {
        Get(RouteConstants.GetRoute("descriptors"));
        ConfigurePermissions(SecretsPermissions.Read);
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        await Send.OkAsync(new SecretDescriptorsResponse
        {
            Types = typeRegistry.List().ToArray(),
            Stores = storeRegistry.List().Select(x => x.Descriptor).ToArray()
        }, cancellationToken);
    }
}
