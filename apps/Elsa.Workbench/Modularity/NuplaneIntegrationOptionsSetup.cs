using CShells.Nuplane;
using Elsa.Foundation.Hosting;
using Microsoft.Extensions.Options;

namespace Elsa.Workbench;

/// <summary>Maps the Workbench's legacy reload switch and Elsa diagnostics to the shared integration profile.</summary>
public sealed class NuplaneIntegrationOptionsSetup(
    IConfiguration configuration,
    ILogger<NuplaneIntegrationOptionsSetup> logger) : IConfigureOptions<NuplaneIntegrationOptions>
{
    public const string ReloadOnPackageChangeKey = "Elsa:Shells:ReloadOnPackageChange";

    public void Configure(NuplaneIntegrationOptions options)
    {
        options.Enabled = true;
        options.RefreshTrigger = NuplaneRefreshTrigger.ChangedOrPending;
        options.AutoReload = bool.TryParse(configuration[ReloadOnPackageChangeKey], out var reload) && reload;
        options.OnReloadResults = (results, _) => ShellReloadFailure.LogNuplaneReloadResults(results, logger);
    }
}
