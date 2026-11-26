using Elsa.Studio.Api.Options;
using Elsa.Studio.Core.Contracts;
using Elsa.Studio.Core.Models;
using Microsoft.Extensions.Options;

namespace Elsa.Studio.Backends;

internal sealed class BackendsRuntimeConfigurationSource(IConfiguration configuration, IOptions<StudioApiOptions> options)
    : IStudioRuntimeConfigurationSource
{
    public IEnumerable<StudioRuntimeConfigurationEntry> GetConfiguration(string studioPathBase)
    {
        if (options.Value.DisabledModuleIds.Contains(typeof(BackendsStudioFeature).Assembly.GetName().Name!)) yield break;
        var path = configuration["WebRouting:Path"]?.Trim('/') ?? "";
        var prefix = studioPathBase.TrimEnd('/') + (path.Length == 0 ? "" : "/" + path);
        yield return new("backends", new
        {
            configuration = StudioBackendProfiles.ReadConfiguration(configuration),
            profileApiPath = prefix + "/_elsa/studio/backends/profiles"
        });
    }
}
