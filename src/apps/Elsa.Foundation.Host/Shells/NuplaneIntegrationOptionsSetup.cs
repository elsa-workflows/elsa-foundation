using CShells.Nuplane;
using CShells.Lifecycle;
using Microsoft.Extensions.Options;

namespace Elsa.Foundation.Host.Shells;

/// <summary>Maps the Foundation host's legacy reload switch to the shared Nuplane integration profile.</summary>
public sealed class NuplaneIntegrationOptionsSetup(
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
        options.OnReloadResults = LogReloadResults;
    }

    private ValueTask LogReloadResults(IReadOnlyList<ReloadResult> results, CancellationToken cancellationToken)
    {
        var failures = ShellReloadFailure.From(results, ShellReloadFailure.HostDirectory);
        logger.LogInformation("Reloaded {Count} active shell(s) after a Nuplane reconcile.", results.Count - failures.Count);
        foreach (var failure in failures)
        {
            if (failure.Refusal is null)
                logger.LogError(failure.Exception, "Reloading shell '{Shell}' after a Nuplane reconcile failed; its previous generation is still active.", failure.Shell);
            else
                logger.LogWarning("Reloading shell '{Shell}' after a Nuplane reconcile was refused; its previous generation is still active. {Error}", failure.Shell, failure.Error);
        }

        return ValueTask.CompletedTask;
    }
}
