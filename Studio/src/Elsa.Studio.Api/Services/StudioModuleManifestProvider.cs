using Elsa.Studio.Api.Contracts;
using Elsa.Studio.Api.Models;
using Elsa.Studio.Api.Options;
using Elsa.Studio.Core.Events;
using Elsa.Studio.Core.Models;
using Microsoft.Extensions.Options;

namespace Elsa.Studio.Api.Services;

public sealed class StudioModuleManifestProvider(
    IEnumerable<IStudioEventHandler<OnStudioModuleManifestsCollecting>> handlers,
    IOptions<StudioApiOptions> options) : IStudioModuleManifestProvider
{
    public async Task<StudioModulesResponse> GetModules(CancellationToken cancellationToken)
    {
        var collection = new OnStudioModuleManifestsCollecting();

        foreach (var handler in handlers)
            await handler.Handle(collection, cancellationToken);

        var studioOptions = options.Value;
        var modules = new List<StudioModuleManifest>();
        var diagnostics = new List<StudioModuleDiagnostic>();

        foreach (var manifest in collection.Manifests.OrderBy(x => x.Id, StringComparer.OrdinalIgnoreCase))
        {
            if (studioOptions.DisabledModuleIds.Contains(manifest.Id))
            {
                diagnostics.Add(new StudioModuleDiagnostic(
                    manifest.Id,
                    StudioModuleDiagnosticStatuses.Disabled,
                    "Module disabled by host configuration."));
                continue;
            }

            modules.Add(manifest);
            diagnostics.Add(new StudioModuleDiagnostic(
                manifest.Id,
                StudioModuleDiagnosticStatuses.Available,
                "Module manifest accepted."));
        }

        return new StudioModulesResponse(studioOptions.HostVersion, studioOptions.SdkVersion, modules, diagnostics);
    }
}

