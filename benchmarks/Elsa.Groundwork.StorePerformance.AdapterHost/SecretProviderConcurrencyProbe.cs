using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using Elsa.Secrets.Core.Models;

namespace Elsa.Groundwork.StorePerformance.AdapterHost;

/// <summary>
/// Instruments the actual command-execution seam for the two frozen create contenders. The adapter
/// rendezvous only ensures that both independent public clients are live. Providers which permit
/// concurrent commands hold both command-start callbacks until both arrive. A provider-enforced
/// single-connection lifetime is recorded as serialized-by-design and must still expose two real command
/// starts. EF additionally reports the distinct physical connections which execute the contender inserts.
/// </summary>
internal sealed class SecretProviderConcurrencyProbe(bool providerCommandsSerializedByDesign = false)
{
    private readonly object gate = new();
    private readonly TaskCompletionSource<bool> bothClientsReady =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ManualResetEventSlim bothProviderCommandsStarted = new(initialState: false);
    private readonly AsyncLocal<Lease?> currentAttempt = new();
    private readonly HashSet<object> clientIdentities = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Lease> commandStarts = new(ReferenceEqualityComparer.Instance);
    private readonly List<bool> commandDeltas = [];
    private int completedContenders;
    private bool providerCommandOverlapObserved;

    internal async ValueTask<Lease?> EnterAsync(
        object clientIdentity,
        Secret secret,
        IProviderRoundTripObserver observer,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(secret.Id, SecretCreateReadListWorkload.WinnerSecretId, StringComparison.Ordinal))
            return null;

        var before = observer.Snapshot();
        lock (gate)
        {
            if (!clientIdentities.Add(clientIdentity))
                throw new PerformanceContractException(
                    "Each Secret create contender must use a distinct public client identity exactly once.");
            if (clientIdentities.Count > SecretCreateReadListWorkload.ConcurrentContenders)
                throw new PerformanceContractException(
                    "Secret workload observed more provider-command contender clients than the frozen contract permits.");
            if (clientIdentities.Count == SecretCreateReadListWorkload.ConcurrentContenders)
                bothClientsReady.TrySetResult(true);
        }

        await bothClientsReady.Task.WaitAsync(cancellationToken);
        return new Lease(this, before);
    }

    /// <summary>
    /// Called synchronously from EF's command-executing interceptor or Groundwork's provider command
    /// observer. Groundwork providers invoke that observer immediately before the native command call.
    /// Pausing here makes overlap an executable fact at the command seam, not an adapter timing guess;
    /// the explicit serialized mode records the provider lifetime invariant without fabricating overlap.
    /// </summary>
    internal void ProviderCommandStarting()
    {
        var attempt = currentAttempt.Value;
        if (attempt is null)
            return;
        lock (gate)
        {
            if (!commandStarts.Add(attempt))
                return;
            if (commandStarts.Count == SecretCreateReadListWorkload.ConcurrentContenders)
            {
                providerCommandOverlapObserved = !providerCommandsSerializedByDesign;
                bothProviderCommandsStarted.Set();
            }
        }

        if (providerCommandsSerializedByDesign)
            return;
        if (!bothProviderCommandsStarted.Wait(TimeSpan.FromSeconds(10)))
            throw new PerformanceContractException(
                "Secret concurrent-create provider commands did not overlap at the actual command-execution seam within the bounded proof window.");
    }

    internal SecretProviderConcurrencyEvidence RequireProven(
        int distinctPhysicalConnectionCount = 0,
        bool requireDistinctPhysicalConnections = false)
    {
        SecretProviderConcurrencyEvidence evidence;
        lock (gate)
        {
            evidence = new SecretProviderConcurrencyEvidence(
                clientIdentities.Count,
                completedContenders,
                commandStarts.Count,
                providerCommandOverlapObserved,
                providerCommandsSerializedByDesign,
                commandDeltas.Count == SecretCreateReadListWorkload.ConcurrentContenders && commandDeltas.All(value => value),
                distinctPhysicalConnectionCount);
        }

        if (evidence.IndependentClientCount != SecretCreateReadListWorkload.ConcurrentContenders ||
            evidence.CompletedContenders != SecretCreateReadListWorkload.ConcurrentContenders ||
            (!evidence.ProviderCommandOverlapObserved && !evidence.ProviderCommandsSerializedByDesign) ||
            evidence.ProviderCommandStartCount != SecretCreateReadListWorkload.ConcurrentContenders ||
            !evidence.EveryContenderIssuedProviderCommands ||
            requireDistinctPhysicalConnections &&
            evidence.DistinctPhysicalConnectionCount != SecretCreateReadListWorkload.ConcurrentContenders)
            throw new PerformanceContractException(
                "Secret concurrent-create evidence must prove two independent clients entered actual provider commands, either overlapped there or were explicitly serialized by provider design, both issued provider commands, and every required physical connection was distinct " +
                $"(clients={evidence.IndependentClientCount}, completed={evidence.CompletedContenders}, command-starts={evidence.ProviderCommandStartCount}, command-overlap={evidence.ProviderCommandOverlapObserved}, serialized-by-design={evidence.ProviderCommandsSerializedByDesign}, command-deltas={evidence.EveryContenderIssuedProviderCommands}, physical-connections={evidence.DistinctPhysicalConnectionCount}).");
        return evidence;
    }

    private void Complete(long before, long after)
    {
        lock (gate)
        {
            completedContenders++;
            commandDeltas.Add(after > before);
        }
    }

    internal sealed class Lease(SecretProviderConcurrencyProbe owner, long before)
    {
        private int completed;

        internal IDisposable BeginProviderCall()
        {
            if (owner.currentAttempt.Value is not null)
                throw new PerformanceContractException("Secret provider-command concurrency scopes cannot be nested.");
            owner.currentAttempt.Value = this;
            return new ProviderCallScope(owner, this);
        }

        internal void Complete(long after)
        {
            if (Interlocked.Exchange(ref completed, 1) == 0)
                owner.Complete(before, after);
        }

        private sealed class ProviderCallScope(SecretProviderConcurrencyProbe owner, Lease attempt) : IDisposable
        {
            public void Dispose()
            {
                if (ReferenceEquals(owner.currentAttempt.Value, attempt))
                    owner.currentAttempt.Value = null;
            }
        }
    }
}
