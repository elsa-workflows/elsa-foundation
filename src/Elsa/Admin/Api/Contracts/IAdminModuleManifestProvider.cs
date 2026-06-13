using Elsa.Admin.Api.Models;

namespace Elsa.Admin.Api.Contracts;

public interface IAdminModuleManifestProvider
{
    Task<AdminModulesResponse> GetModules(CancellationToken cancellationToken);
}
