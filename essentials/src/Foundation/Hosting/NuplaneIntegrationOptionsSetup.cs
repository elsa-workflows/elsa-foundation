using CShells.Nuplane;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Foundation.Hosting;

/// <summary>Maps the Foundation host's legacy reload switch to the shared Nuplane integration profile.</summary>
internal sealed class NuplaneIntegrationOptionsSetup(
    IConfiguration configuration,
    ILogger<NuplaneIntegrationOptionsSetup> logger) : IConfigureOptions<NuplaneIntegrationOptions>
{
    public const string ReloadOnPackageChangeKey = "Elsa:Shells:ReloadOnPackageChange";

    public void Configure(NuplaneIntegrationOptions options)
    {
        var enabled = !bool.TryParse(configuration[ReloadOnPackageChangeKey], out var configured) || configured;
        options.Enabled = enabled;
        options.RefreshTrigger = NuplaneRefreshTrigger.EveryEligibleCompletion;
        options.AutoReload = true;
        options.OnReloadResults = (results, _) => ShellReloadFailure.LogNuplaneReloadResults(results, logger);
    }
}
