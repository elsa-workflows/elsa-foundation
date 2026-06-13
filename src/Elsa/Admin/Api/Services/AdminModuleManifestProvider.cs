using Elsa.Admin.Api.Contracts;
using Elsa.Admin.Api.Models;
using Elsa.Admin.Api.Options;
using Elsa.Admin.Core.Events;
using Elsa.Admin.Core.Models;
using Elsa.Events.Core.Contracts;
using Microsoft.Extensions.Options;

namespace Elsa.Admin.Api.Services;

public sealed class AdminModuleManifestProvider(
    IEventPublisher eventPublisher,
    IOptions<AdminApiOptions> options) : IAdminModuleManifestProvider
{
    public async Task<AdminModulesResponse> GetModules(CancellationToken cancellationToken)
    {
        var collection = new OnAdminModuleManifestsCollecting();
        await eventPublisher.Publish(collection, cancellationToken: cancellationToken);

        var orderedManifests = collection.Manifests
            .OrderBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var activeModules = new List<AdminModuleManifest>();
        var diagnostics = new List<AdminModuleDiagnostic>();

        foreach (var manifest in orderedManifests)
        {
            if (options.Value.DisabledModuleIds.Contains(manifest.Id))
            {
                diagnostics.Add(new(
                    manifest.Id,
                    AdminModuleDiagnosticStatuses.Disabled,
                    "The module was disabled by server configuration."));
                continue;
            }

            activeModules.Add(manifest);
            diagnostics.Add(new(
                manifest.Id,
                AdminModuleDiagnosticStatuses.Available,
                "The module is available for browser activation."));
        }

        return new(
            options.Value.HostVersion,
            options.Value.SdkVersion,
            activeModules,
            diagnostics);
    }
}
