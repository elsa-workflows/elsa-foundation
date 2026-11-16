using CShells.Lifecycle;
using CShells.Nuplane;
using Elsa.Persistence.Schema;
using Microsoft.Extensions.Options;

namespace Elsa.Workbench;

/// <summary>Maps the Workbench's legacy reload switch and Elsa diagnostics to the shared integration profile.</summary>
public sealed class NuplaneIntegrationOptionsSetup(
    IConfiguration configuration,
    ILogger<NuplaneIntegrationOptionsSetup> logger) : IConfigureOptions<NuplaneIntegrationOptions>
{
    public const string ReloadOnPackageChangeKey = "Elsa:Shells:ReloadOnPackageChange";

    private static readonly string HostDirectory = $"\"{Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory)}\"";

    public void Configure(NuplaneIntegrationOptions options)
    {
        options.Enabled = true;
        options.RefreshTrigger = NuplaneRefreshTrigger.ChangedOrPending;
        options.AutoReload = bool.TryParse(configuration[ReloadOnPackageChangeKey], out var reload) && reload;
        options.OnReloadResults = LogReloadResults;
    }

    private ValueTask LogReloadResults(IReadOnlyList<ReloadResult> results, CancellationToken cancellationToken)
    {
        var failures = results.Where(result => result.Error is not null).ToArray();
        logger.LogInformation("Reloaded {Count} active shell(s) after a Nuplane reconcile.", results.Count - failures.Length);
        foreach (var failure in failures)
        {
            if (Refusal(failure.Error) is { } refusal)
            {
                logger.LogWarning(
                    "Reloading shell '{Shell}' after a Nuplane reconcile was refused; its previous generation is still active. {Error}",
                    failure.Name,
                    refusal.Message.Replace(IEfModuleRefusal.HostPlaceholder, HostDirectory, StringComparison.Ordinal));
            }
            else
            {
                logger.LogError(failure.Error, "Reloading shell '{Shell}' after a Nuplane reconcile failed; its previous generation is still active.", failure.Name);
            }
        }

        return ValueTask.CompletedTask;
    }

    private static Exception? Refusal(Exception? error) => error switch
    {
        null => null,
        IEfModuleRefusal => error,
        AggregateException aggregate => aggregate.InnerExceptions.Select(Refusal).FirstOrDefault(found => found is not null),
        _ => Refusal(error.InnerException)
    };
}
