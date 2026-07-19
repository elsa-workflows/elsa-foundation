using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Secrets.Api.Constants;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Core.Permissions;
using FastEndpoints;

namespace Elsa.Secrets.Api.Endpoints.Secrets;

internal sealed class Picker(ISecretManager secretManager) : ElsaEndpoint<SecretPickerRequest, SecretPickerResponse>
{
    public override void Configure()
    {
        Post(RouteConstants.GetRoute("picker"));
        ConfigurePermissions(SecretsPermissions.Read);
    }

    public override async Task HandleAsync(SecretPickerRequest request, CancellationToken cancellationToken)
    {
        var tenantId = SecretEndpointTenant.Resolve(User);
        if (tenantId is null)
        {
            await Send.ForbiddenAsync(cancellationToken);
            return;
        }

        var page = await secretManager.ListAsync(tenantId, new SecretQuery
        {
            Search = request.Search,
            TypeNames = request.TypeNames,
            StoreNames = request.StoreNames,
            Scope = request.Scope,
            ActiveOnly = request.ActiveOnly,
            PageSize = 50
        }, cancellationToken);

        await Send.OkAsync(new SecretPickerResponse
        {
            Items = page.Items,
            CanCreateInline = true
        }, cancellationToken);
    }
}
