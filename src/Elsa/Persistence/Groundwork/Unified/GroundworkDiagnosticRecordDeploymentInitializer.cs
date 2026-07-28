using CShells.Lifecycle;
using Groundwork.DiagnosticRecords;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Elsa.Persistence.Groundwork.Unified;

/// <summary>
/// Applies missing diagnostic-record streams during unified Groundwork startup when auto-apply is
/// enabled. Drift remains an admission failure and runtime store factories remain read-only.
/// </summary>
public sealed class GroundworkDiagnosticRecordDeploymentInitializer(
    bool autoApplyOnStartup,
    IEnumerable<IDiagnosticRecordDeploymentManifestSource> deploymentSources,
    IDiagnosticRecordDeploymentApplier deploymentApplier,
    ILogger<GroundworkDiagnosticRecordDeploymentInitializer> logger) : IHostedService, IShellInitializer
{
    private readonly IDiagnosticRecordDeploymentManifestSource[] deploymentSources =
        deploymentSources?.ToArray() ?? throw new ArgumentNullException(nameof(deploymentSources));
    private readonly SemaphoreSlim initializationLock = new(1, 1);
    private bool initialized;

    /// <inheritdoc />
    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        EnsureInitializedAsync(cancellationToken);

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken) =>
        EnsureInitializedAsync(cancellationToken);

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (initialized)
            return;

        await initializationLock.WaitAsync(cancellationToken);
        try
        {
            if (initialized)
                return;

            if (!autoApplyOnStartup || deploymentSources.Length == 0)
            {
                initialized = true;
                return;
            }

            if (deploymentSources.Length > 1)
            {
                throw new InvalidOperationException(
                    $"Unified Groundwork persistence requires exactly one diagnostic-record deployment manifest source, " +
                    $"but {deploymentSources.Length} were registered.");
            }

            var deployment = deploymentSources[0].CreateDeploymentManifest();
            logger.LogInformation(
                "Ensuring {StreamCount} diagnostic-record streams for Groundwork provider '{Provider}': {StreamIds}.",
                deployment.Streams.Count,
                deploymentApplier.Provider,
                string.Join(", ", deployment.Streams.Select(stream => stream.Stream.Value)));

            await deploymentApplier.ApplyAsync(deployment, cancellationToken);
            initialized = true;
        }
        finally
        {
            initializationLock.Release();
        }
    }
}
