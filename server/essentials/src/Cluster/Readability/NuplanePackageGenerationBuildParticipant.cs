using CShells.Features;
using CShells.Lifecycle;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Cluster.Readability;

/// <summary>
/// Owns the root-only CShells package-generation leases and the optional feature-catalog commit subscription.
/// </summary>
/// <remarks>
/// The shared <see cref="NuplanePackageGenerations"/> source is intentionally not a build participant: CShells excludes
/// participant registrations from copied shell providers. This adapter is resolved only from the root provider.
/// </remarks>
public sealed class NuplanePackageGenerationBuildParticipant(
    NuplanePackageGenerations generations,
    IServiceProvider rootProvider) : IShellGenerationBuildParticipant, IDisposable
{
    private IRuntimeFeatureCatalogCommitSource? _commitSource;
    private readonly object _lifecycleGate = new();
    private bool _started;
    private bool _stopped;

    /// <inheritdoc />
    public ValueTask<IShellGenerationBuildLease> BeginAsync(
        ShellGenerationBuildContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureStarted();
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(generations.Begin(context));
    }

    /// <summary>Starts root tracking once, before the first catalog read.</summary>
    internal void EnsureStarted()
    {
        lock (_lifecycleGate)
        {
            ObjectDisposedException.ThrowIf(_stopped, this);
            if (_started)
                return;

            generations.BindTo(rootProvider);
            if (rootProvider.GetService<IRuntimeFeatureCatalog>() is IRuntimeFeatureCatalogCommitSource commitSource)
            {
                _commitSource = commitSource;
                try
                {
                    commitSource.SnapshotCommitted += OnSnapshotCommitted;
                    generations.EnableCommitNotifications();
                    // Notifications are not replayed; this fresh evaluation reconciles the committed state after subscription.
                    generations.CatalogCommitted();
                }
                catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException and not AccessViolationException)
                {
                    generations.CommitSubscriptionFailed(exception);
                }
            }
            _started = true;
        }
    }

    private void OnSnapshotCommitted(RuntimeFeatureCatalogSnapshot snapshot)
    {
        _ = snapshot;
        generations.CatalogCommitted();
    }

    /// <summary>DI may dispose the same singleton through both its canonical and participant registrations.</summary>
    public void Dispose()
    {
        lock (_lifecycleGate)
        {
            if (_stopped)
                return;
            _stopped = true;
            try
            {
                if (_commitSource is { } commitSource)
                    commitSource.SnapshotCommitted -= OnSnapshotCommitted;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException and not AccessViolationException)
            {
                generations.CommitUnsubscriptionFailed(exception);
            }
            finally
            {
                generations.StopCatalogTracking();
            }
        }
    }
}
