using CShells.Lifecycle;

namespace Elsa.Migration.Host;

/// <summary>
/// Activates every configured shell when the host starts, so the migrated folder is reconciled and published before the first
/// request. Foundation's eager activation service is internal to <c>Elsa.Foundation.Hosting</c> and arrives with Nuplane, which
/// this host does not compose.
/// </summary>
/// <remarks>
/// A shell that fails to activate is logged with the reason (an unknown feature, a missing workflows folder or activities file, a
/// file that can't be imported) and the host keeps running: <c>/health/ready</c> stays 503. That is what the spec calls refusing
/// to start. CShells tries the activation again on the shell's next request.
/// </remarks>
internal sealed class DefaultShellActivation(
    IShellRegistry registry,
    IConfiguration configuration,
    ILogger<DefaultShellActivation> logger) : IHostedService
{
    private const string ConfiguredShellsSection = "CShells:Shells";

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var shellNames = configuration.GetSection(ConfiguredShellsSection).GetChildren()
            .Select(child => child.Key)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToArray();

        foreach (var shellName in shellNames)
        {
            try
            {
                await registry.GetOrActivateAsync(shellName, cancellationToken);
                logger.LogInformation("Activated shell '{Shell}'.", shellName);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogCritical(exception,
                    "The Elsa 3 migration host could not activate shell '{Shell}' and is not ready: {Reason}",
                    shellName, Describe(exception));
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>The exception chain's messages, so the one log line names the feature, setting or file at fault.</summary>
    internal static string Describe(Exception exception)
    {
        var messages = new List<string>();
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is AggregateException aggregate && aggregate.InnerExceptions.Count > 1)
            {
                messages.AddRange(aggregate.InnerExceptions.Select(inner => $"{inner.GetType().Name}: {inner.Message}"));
                break;
            }

            messages.Add($"{current.GetType().Name}: {current.Message}");
        }

        return string.Join(" -> ", messages.Distinct());
    }
}
