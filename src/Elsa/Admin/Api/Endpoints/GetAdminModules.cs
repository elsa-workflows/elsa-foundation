using Elsa.Admin.Api.Contracts;
using Elsa.Admin.Api.Models;
using Elsa.Api.FastEndpoints.Abstractions;

namespace Elsa.Admin.Api.Endpoints;

internal sealed class GetAdminModules(IAdminModuleManifestProvider manifestProvider) : ElsaEndpointWithoutRequest<AdminModulesResponse>
{
    public override void Configure()
    {
        Get("/_elsa/admin/modules");
        ConfigurePermissions();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        await Send.OkAsync(await manifestProvider.GetModules(ct), ct);
    }
}
