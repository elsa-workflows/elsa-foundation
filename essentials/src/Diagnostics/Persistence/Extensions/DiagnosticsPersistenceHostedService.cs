using CShells.Lifecycle;
using Elsa.Diagnostics.Persistence.Draining;
using Microsoft.Extensions.Hosting;
using System.Runtime.ExceptionServices;

namespace Elsa.Diagnostics.Persistence.Extensions;

internal sealed class DiagnosticsPersistenceHostedService(
    IEnumerable<IDiagnosticsPersistenceDrain> drains,
    IEnumerable<IDiagnosticsPersistenceStartupResource> resources) :
    IHostedService,
    IShellInitializer,
    IShellTerminator
{
    private readonly IReadOnlyList<IDiagnosticsPersistenceDrain> _drains =
        drains.Distinct<IDiagnosticsPersistenceDrain>(ReferenceEqualityComparer.Instance).ToArray();
    private readonly IReadOnlyList<IDiagnosticsPersistenceStartupResource> _resources =
        resources.Distinct<IDiagnosticsPersistenceStartupResource>(ReferenceEqualityComparer.Instance).ToArray();
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private IReadOnlyList<IDiagnosticsPersistenceResourceLease> _leases = [];
    private ExceptionDispatchInfo? _startupFailure;
    private bool _started;
    private bool _stopped;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            if (_started)
                return;
            _startupFailure?.Throw();
            if (_stopped)
                throw new InvalidOperationException("The diagnostics persistence lifecycle has already stopped.");

            var leases = new List<IDiagnosticsPersistenceResourceLease>(_resources.Count);
            var startedDrains = new List<IDiagnosticsPersistenceDrain>(_drains.Count);
            try
            {
                foreach (var resource in _resources)
                {
                    var lease = await resource.AcquireAsync(cancellationToken);
                    leases.Add(lease ?? throw new InvalidOperationException(
                        $"Diagnostics startup resource '{resource.GetType().FullName}' returned a null lease."));
                }

                foreach (var drain in _drains)
                {
                    drain.Start();
                    startedDrains.Add(drain);
                }

                _leases = leases;
                _started = true;
            }
            catch (Exception startupFailure)
            {
                await CleanupAfterStartupFailureAsync(startedDrains, leases, startupFailure);
                var failureDispatchInfo = ExceptionDispatchInfo.Capture(startupFailure);
                _startupFailure = failureDispatchInfo;
                _stopped = true;
                failureDispatchInfo.Throw();
                throw;
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public Task InitializeAsync(CancellationToken cancellationToken) =>
        StartAsync(cancellationToken);

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // Each drain owns a bounded shutdown timeout. Do not forward the host token: cancellation of the host's
        // wait must not let provider sessions dispose while accepted captures are still being settled.
        await _lifecycleGate.WaitAsync(CancellationToken.None);
        try
        {
            if (_stopped)
                return;

            var failures = new List<Exception>();
            try
            {
                await StopDrainsAsync(_drains);
            }
            catch (Exception exception)
            {
                CollectFailures(failures, exception);
            }

            try
            {
                await DisposeLeasesAsync(_leases);
            }
            catch (Exception exception)
            {
                CollectFailures(failures, exception);
            }

            _leases = [];
            _started = false;
            _stopped = true;
            ThrowFailures(failures);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public Task TerminateAsync(CancellationToken cancellationToken = default) =>
        StopAsync(cancellationToken);

    private static async Task StopDrainsAsync(IEnumerable<IDiagnosticsPersistenceDrain> drains)
    {
        var stops = drains
            .Reverse()
            .Select(StopWithoutAbandoningOtherDrains)
            .ToArray();
        try
        {
            await Task.WhenAll(stops);
        }
        catch
        {
            var failures = stops
                .Where(stop => stop.IsFaulted)
                .SelectMany(stop => stop.Exception!.InnerExceptions)
                .ToArray();
            ThrowFailures(failures);
        }
    }

    private static async Task DisposeLeasesAsync(IEnumerable<IDiagnosticsPersistenceResourceLease> leases)
    {
        var failures = new List<Exception>();
        foreach (var lease in leases.Reverse())
        {
            try
            {
                await lease.DisposeAsync();
            }
            catch (Exception exception)
            {
                CollectFailures(failures, exception);
            }
        }
        ThrowFailures(failures);
    }

    private static async Task CleanupAfterStartupFailureAsync(
        IReadOnlyList<IDiagnosticsPersistenceDrain> startedDrains,
        IReadOnlyList<IDiagnosticsPersistenceResourceLease> leases,
        Exception startupFailure)
    {
        var cleanupFailures = new List<Exception>();
        try
        {
            await StopDrainsAsync(startedDrains);
        }
        catch (Exception exception)
        {
            CollectFailures(cleanupFailures, exception);
        }

        try
        {
            await DisposeLeasesAsync(leases);
        }
        catch (Exception exception)
        {
            CollectFailures(cleanupFailures, exception);
        }

        if (cleanupFailures.Count != 0)
            startupFailure.Data["DiagnosticsPersistenceCleanupFailures"] = cleanupFailures.ToArray();
    }

    private static Task StopWithoutAbandoningOtherDrains(IDiagnosticsPersistenceDrain drain)
    {
        try
        {
            return drain.StopAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            return Task.FromException(exception);
        }
    }

    private static void CollectFailures(ICollection<Exception> failures, Exception exception)
    {
        if (exception is AggregateException aggregate)
        {
            foreach (var failure in aggregate.Flatten().InnerExceptions)
                failures.Add(failure);
            return;
        }
        failures.Add(exception);
    }

    private static void ThrowFailures(IReadOnlyCollection<Exception> failures)
    {
        if (failures.Count == 1)
            ExceptionDispatchInfo.Capture(failures.Single()).Throw();
        if (failures.Count > 1)
            throw new AggregateException("One or more diagnostics persistence lifecycle operations failed.", failures);
    }
}
