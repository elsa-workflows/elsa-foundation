using Elsa.Studio.Api.Models;

namespace Elsa.Studio.Api.Contracts;

public interface IStudioModuleManifestProvider
{
    Task<StudioModulesResponse> GetModules(CancellationToken cancellationToken);
}

