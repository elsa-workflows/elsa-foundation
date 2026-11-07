using System.Text.Json.Nodes;
using Elsa.Workbench.Tests.BackgroundActivities;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workbench.BackgroundActivities.Tests;

public sealed class NativeHostingConsumptionGateTests : IDisposable
{
    private readonly string _journal = Directory.CreateTempSubdirectory("native-consumption-gate-").FullName;
    private static readonly TimeSpan HangGuard = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Hold_is_per_host_and_acknowledges_the_current_token_without_reading_the_store()
    {
        using var owner = new NativeHostingConsumptionGate(_journal, "owner");
        using var survivor = new NativeHostingConsumptionGate(_journal, "survivor");
        var reads = 0;
        ValueTask<int> Read() => ValueTask.FromResult(++reads);

        Assert.Equal(1, await owner.ReadAsync(Read, 0, CancellationToken.None));
        Assert.False(File.Exists(AcknowledgementPath));

        Hold("first");
        Assert.Equal(0, await owner.ReadAsync(Read, 0, CancellationToken.None));
        Assert.Equal(2, await survivor.ReadAsync(Read, 0, CancellationToken.None));
        Assert.Equal(2, reads);
        AssertAcknowledgement("first");

        Hold("second");
        Assert.Equal(0, await owner.ReadAsync(Read, 0, CancellationToken.None));
        AssertAcknowledgement("second");
        File.Delete(HoldPath);
        Assert.Equal(3, await owner.ReadAsync(Read, 0, CancellationToken.None));
    }

    [Fact]
    public async Task Hold_acknowledgement_waits_for_the_read_already_in_flight()
    {
        using var gate = new NativeHostingConsumptionGate(_journal, "owner");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = gate.ReadAsync(async () =>
        {
            entered.SetResult();
            await release.Task;
            return 1;
        }, 0, CancellationToken.None).AsTask();

        try
        {
            await entered.Task.WaitAsync(HangGuard);
            Hold("drained");
            var held = gate.ReadAsync(() => throw new InvalidOperationException("A held read reached the store."),
                0, CancellationToken.None).AsTask();
            Assert.False(held.IsCompleted);
            Assert.False(File.Exists(AcknowledgementPath));

            release.SetResult();
            Assert.Equal(1, await active.WaitAsync(HangGuard));
            Assert.Equal(0, await held.WaitAsync(HangGuard));
            AssertAcknowledgement("drained");
        }
        finally
        {
            release.TrySetResult();
            await active.WaitAsync(HangGuard);
        }
    }

    [Fact]
    public async Task Caller_can_cancel_while_an_earlier_read_owns_the_gate()
    {
        using var gate = new NativeHostingConsumptionGate(_journal, "owner");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = gate.ReadAsync(async () =>
        {
            entered.SetResult();
            await release.Task;
            return 1;
        }, 0, CancellationToken.None).AsTask();

        try
        {
            await entered.Task.WaitAsync(HangGuard);
            Hold("cancelled");
            using var cancelled = new CancellationTokenSource();
            var waiting = gate.ReadAsync(() => throw new InvalidOperationException("A cancelled read reached the store."),
                0, cancelled.Token).AsTask();
            cancelled.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting.WaitAsync(HangGuard));
            Assert.False(File.Exists(AcknowledgementPath));
        }
        finally
        {
            release.TrySetResult();
            await active.WaitAsync(HangGuard);
        }
    }

    [Fact]
    public async Task Already_cancelled_caller_neither_reads_nor_acknowledges_a_hold()
    {
        using var gate = new NativeHostingConsumptionGate(_journal, "owner");
        Hold("cancelled");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => gate.ReadAsync(
            () => throw new InvalidOperationException("A cancelled read reached the store."), 0, cancelled.Token).AsTask());
        Assert.False(File.Exists(AcknowledgementPath));
    }

    [Theory]
    [InlineData("receipts")]
    [InlineData("leases")]
    [InlineData("pending")]
    [InlineData("paged-pending")]
    public async Task Real_fixture_registration_holds_consumption_across_scopes_but_not_the_other_host(string operation)
    {
        using var owner = CreateHost("owner");
        using var survivor = CreateHost("survivor");
        using var firstOwnerScope = owner.CreateScope();
        using var secondOwnerScope = owner.CreateScope();
        using var survivorScope = survivor.CreateScope();
        Assert.Same(firstOwnerScope.ServiceProvider.GetRequiredService<NativeHostingConsumptionGate>(),
            secondOwnerScope.ServiceProvider.GetRequiredService<NativeHostingConsumptionGate>());

        Hold("registered");
        Assert.Equal(0, await ReadAsync(firstOwnerScope.ServiceProvider, operation));
        Assert.Equal(0, await ReadAsync(secondOwnerScope.ServiceProvider, operation));
        AssertAcknowledgement("registered");
        // These real fixture decorators wrap throwing stores. Reaching the survivor store proves that the owner's
        // marker did not suppress the survivor; reaching either owner store would fail above.
        await Assert.ThrowsAsync<NotSupportedException>(() => ReadAsync(survivorScope.ServiceProvider, operation));
    }

    [Fact]
    public async Task Held_owner_acquisition_is_cancelled_before_the_backend_while_the_survivor_can_acquire()
    {
        var ownerBackend = new OwnershipBackend();
        var survivorBackend = new OwnershipBackend();
        using var owner = CreateHost("owner", ownerBackend);
        using var survivor = CreateHost("survivor", survivorBackend);
        using var ownerScope = owner.CreateScope();
        using var survivorScope = survivor.CreateScope();
        Hold("admission");
        using var cancellation = new CancellationTokenSource();
        var blocked = ownerScope.ServiceProvider.GetRequiredService<IRuntimeExecutionOwnershipService>()
            .AcquireAsync("execution", cancellation.Token).AsTask();
        var acknowledged = WaitForAcknowledgementAsync(cancellation.Token);
        try
        {
            // Bypassing the real ownership decorator must fail immediately on backend completion, rather than
            // merely exhausting an acknowledgement watchdog without proving which boundary was crossed.
            Assert.Same(acknowledged, await Task.WhenAny(blocked, acknowledged).WaitAsync(HangGuard));
            await acknowledged;
            Assert.False(blocked.IsCompleted);
            Assert.Equal(0, ownerBackend.AcquireCalls);
            var survivorOwnership = survivorScope.ServiceProvider.GetRequiredService<IRuntimeExecutionOwnershipService>();
            var lease = await survivorOwnership.AcquireAsync("execution");
            Assert.Equal(1, survivorBackend.AcquireCalls);
            await survivorOwnership.ReleaseAsync(lease);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => blocked.WaitAsync(HangGuard));
            Assert.Equal(0, ownerBackend.AcquireCalls);
        }
        finally
        {
            await CancelAndObserveAsync(cancellation, blocked);
            await CancelAndObserveAsync(cancellation, acknowledged);
        }
    }

    [Theory]
    [InlineData(RuntimeExecutionOwnershipTransitionStatus.Applied)]
    [InlineData(RuntimeExecutionOwnershipTransitionStatus.AlreadyApplied)]
    public async Task Actual_release_finishes_before_acknowledgement_and_is_not_blocked_by_a_new_held_acquisition(
        RuntimeExecutionOwnershipTransitionStatus status)
    {
        var releaseEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCompletes = new TaskCompletionSource<RuntimeExecutionOwnershipTransitionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var backend = new OwnershipBackend
        {
            OnRelease = (_, _) =>
            {
                releaseEntered.TrySetResult();
                return new ValueTask<RuntimeExecutionOwnershipTransitionResult>(releaseCompletes.Task);
            }
        };
        using var owner = CreateHost("owner", backend);
        using var firstScope = owner.CreateScope();
        using var secondScope = owner.CreateScope();
        var firstOwnership = firstScope.ServiceProvider.GetRequiredService<IRuntimeExecutionOwnershipService>();
        var secondOwnership = secondScope.ServiceProvider.GetRequiredService<IRuntimeExecutionOwnershipService>();
        var lease = await firstOwnership.AcquireAsync("execution");
        Hold("released");
        Assert.Equal(0, await ReadAsync(firstScope.ServiceProvider, "receipts"));
        Assert.False(File.Exists(AcknowledgementPath));
        using var acquisitionCancellation = new CancellationTokenSource();
        var blocked = secondOwnership.AcquireAsync("execution", acquisitionCancellation.Token).AsTask();
        using var releaseCancellation = new CancellationTokenSource();
        var release = secondOwnership.ReleaseAsync(lease, releaseCancellation.Token).AsTask();
        var result = new RuntimeExecutionOwnershipTransitionResult(status, lease.FencingToken);
        try
        {
            await releaseEntered.Task.WaitAsync(HangGuard);
            Assert.Same(lease, backend.LastReleased);
            Assert.Equal(releaseCancellation.Token, backend.LastReleaseToken);
            Assert.False(File.Exists(AcknowledgementPath));
            Assert.False(blocked.IsCompleted);
            releaseCompletes.SetResult(result);
            Assert.Same(result, await release.WaitAsync(HangGuard));
            AssertAcknowledgement("released");
            Assert.Equal(1, backend.AcquireCalls);
        }
        finally
        {
            releaseCompletes.TrySetResult(result);
            await release.WaitAsync(HangGuard);
            await CancelAndObserveAsync(acquisitionCancellation, blocked);
        }
    }

    [Theory]
    [InlineData(RuntimeExecutionOwnershipTransitionStatus.NotFound)]
    [InlineData(RuntimeExecutionOwnershipTransitionStatus.Stale)]
    [InlineData(RuntimeExecutionOwnershipTransitionStatus.Expired)]
    public async Task Refused_backend_release_does_not_acknowledge_a_still_tracked_drain(
        RuntimeExecutionOwnershipTransitionStatus status)
    {
        var backend = new OwnershipBackend();
        using var owner = CreateHost("owner", backend);
        using var scope = owner.CreateScope();
        var ownership = scope.ServiceProvider.GetRequiredService<IRuntimeExecutionOwnershipService>();
        var lease = await ownership.AcquireAsync("execution");
        Hold("refused");
        var refused = new RuntimeExecutionOwnershipTransitionResult(status, lease.FencingToken);
        backend.OnRelease = (_, _) => ValueTask.FromResult(refused);
        Assert.Same(refused, await ownership.ReleaseAsync(lease));
        Assert.Equal(0, await ReadAsync(scope.ServiceProvider, "receipts"));
        Assert.False(File.Exists(AcknowledgementPath));
        backend.OnRelease = (held, _) => ValueTask.FromResult(Applied(held));
        await ownership.ReleaseAsync(lease);
        AssertAcknowledgement("refused");
    }

    [Fact]
    public async Task Cancelled_backend_release_propagates_its_token_without_acknowledging_or_blocking_a_later_release()
    {
        var backend = new OwnershipBackend();
        using var owner = CreateHost("owner", backend);
        using var scope = owner.CreateScope();
        var ownership = scope.ServiceProvider.GetRequiredService<IRuntimeExecutionOwnershipService>();
        var lease = await ownership.AcquireAsync("execution");
        Hold("cancelled-release");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ownership.ReleaseAsync(lease, cancellation.Token).AsTask());
        Assert.Equal(cancellation.Token, backend.LastReleaseToken);
        Assert.False(File.Exists(AcknowledgementPath));
        await ownership.ReleaseAsync(lease);
        AssertAcknowledgement("cancelled-release");
    }

    [Fact]
    public async Task Pre_hold_backend_acquisition_is_tracked_until_its_actual_lease_is_released()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completes = new TaskCompletionSource<RuntimeExecutionLease>(TaskCreationOptions.RunContinuationsAsynchronously);
        var backend = new OwnershipBackend
        {
            OnAcquire = (_, _) =>
            {
                entered.TrySetResult();
                return new ValueTask<RuntimeExecutionLease>(completes.Task);
            }
        };
        using var owner = CreateHost("owner", backend);
        using var scope = owner.CreateScope();
        var ownership = scope.ServiceProvider.GetRequiredService<IRuntimeExecutionOwnershipService>();
        var acquisition = ownership.AcquireAsync("execution").AsTask();
        var lease = NewLease("execution");
        try
        {
            await entered.Task.WaitAsync(HangGuard);
            Hold("acquiring");
            Assert.Equal(0, await ReadAsync(scope.ServiceProvider, "receipts"));
            Assert.False(File.Exists(AcknowledgementPath));
            completes.SetResult(lease);
            Assert.Same(lease, await acquisition.WaitAsync(HangGuard));
            Assert.False(File.Exists(AcknowledgementPath));
            await ownership.ReleaseAsync(lease);
            AssertAcknowledgement("acquiring");
        }
        finally
        {
            completes.TrySetResult(lease);
            await acquisition.WaitAsync(HangGuard);
        }
    }

    [Fact]
    public async Task Failed_pre_hold_acquisition_clears_only_its_pending_admission()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completes = new TaskCompletionSource<RuntimeExecutionLease>(TaskCreationOptions.RunContinuationsAsynchronously);
        var backend = new OwnershipBackend
        {
            OnAcquire = (_, _) =>
            {
                entered.TrySetResult();
                return new ValueTask<RuntimeExecutionLease>(completes.Task);
            }
        };
        using var owner = CreateHost("owner", backend);
        using var scope = owner.CreateScope();
        var ownership = scope.ServiceProvider.GetRequiredService<IRuntimeExecutionOwnershipService>();
        var acquisition = ownership.AcquireAsync("execution").AsTask();
        try
        {
            await entered.Task.WaitAsync(HangGuard);
            Hold("failed-acquire");
            Assert.Equal(0, await ReadAsync(scope.ServiceProvider, "receipts"));
            Assert.False(File.Exists(AcknowledgementPath));
            completes.SetException(new InvalidOperationException("controlled acquisition failure"));
            var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => acquisition.WaitAsync(HangGuard));
            Assert.Equal("controlled acquisition failure", failure.Message);
            AssertAcknowledgement("failed-acquire");
        }
        finally
        {
            completes.TrySetException(new InvalidOperationException("controlled acquisition failure"));
            try { await acquisition.WaitAsync(HangGuard); }
            catch (InvalidOperationException) { }
        }
    }

    [Fact]
    public async Task Repeated_release_of_one_identity_cannot_hide_another_active_drain()
    {
        var backend = new OwnershipBackend();
        using var owner = CreateHost("owner", backend);
        using var scope = owner.CreateScope();
        var ownership = scope.ServiceProvider.GetRequiredService<IRuntimeExecutionOwnershipService>();
        var first = await ownership.AcquireAsync("first");
        var second = await ownership.AcquireAsync("second");
        Hold("identities");
        await ownership.ReleaseAsync(first);
        await ownership.ReleaseAsync(first);
        Assert.False(File.Exists(AcknowledgementPath));
        await ownership.ReleaseAsync(second);
        AssertAcknowledgement("identities");
    }

    private ServiceProvider CreateHost(string role, OwnershipBackend? ownershipBackend = null)
    {
        var (services, _) = NativeHostingWitnessTestServices.Create();
        if (ownershipBackend is not null)
        {
            services.RemoveAll<IRuntimeExecutionOwnershipService>();
            services.AddScoped<IRuntimeExecutionOwnershipService>(_ => ownershipBackend);
        }
        services.AddScoped<IBackgroundActivityWorkStore>(_ =>
            NativeHostingWitnessTestProxy<IBackgroundActivityWorkStore>.Create());
        var feature = new BackgroundActivityWitnessFeature
        {
            JournalDirectory = _journal,
            Role = role,
            NativeHostingObservation = true
        };
        feature.ConfigureServices(services);
        feature.PostConfigureServices(services);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static async Task<int> ReadAsync(IServiceProvider services, string operation)
    {
        if (operation == "receipts")
            return (await services.GetRequiredService<IBackgroundActivityWorkStore>().ListResultReadyAsync(10)).Count;
        var transport = services.GetRequiredService<IExecutionCommandTransport>();
        return operation switch
        {
            "leases" => (await transport.LeaseAsync("execution", "owner", DateTimeOffset.UtcNow,
                TimeSpan.FromSeconds(30), 10)).Count,
            "pending" => (await transport.ListPendingExecutionIdsAsync(DateTimeOffset.UtcNow, 10)).Count,
            "paged-pending" => (await transport.ListPendingExecutionIdsAsync(DateTimeOffset.UtcNow, 10, 0)).Count,
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
    }

    private string HoldPath => Path.Combine(_journal, "hold-consumption-owner");
    private string AcknowledgementPath => Path.Combine(_journal, "consumption-held.owner.json");
    private void Hold(string token) => File.WriteAllText(HoldPath, token);

    private void AssertAcknowledgement(string token)
    {
        var acknowledgement = JsonNode.Parse(File.ReadAllText(AcknowledgementPath))!.AsObject();
        Assert.Equal("owner", acknowledgement["Role"]!.GetValue<string>());
        Assert.Equal(Environment.ProcessId, acknowledgement["ProcessId"]!.GetValue<int>());
        Assert.Equal(token, acknowledgement["HoldToken"]!.GetValue<string>());
    }

    private async Task WaitForAcknowledgementAsync(CancellationToken cancellationToken)
    {
        using var guard = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        guard.CancelAfter(HangGuard);
        while (!File.Exists(AcknowledgementPath))
            await Task.Delay(10, guard.Token);
    }

    private static async Task CancelAndObserveAsync(CancellationTokenSource cancellation, Task task)
    {
        cancellation.Cancel();
        try { await task.WaitAsync(HangGuard); }
        catch (OperationCanceledException) { }
    }

    private static RuntimeExecutionLease NewLease(string executionId)
    {
        var now = DateTimeOffset.UtcNow;
        return new RuntimeExecutionLease(Guid.NewGuid().ToString("N"), executionId, "owner", now,
            now.AddMinutes(1), fencingToken: 11);
    }

    private static RuntimeExecutionOwnershipTransitionResult Applied(RuntimeExecutionLease lease) =>
        new(RuntimeExecutionOwnershipTransitionStatus.Applied, lease.FencingToken);

    private sealed class OwnershipBackend : IRuntimeExecutionOwnershipService
    {
        public int AcquireCalls { get; private set; }
        public RuntimeExecutionLease? LastReleased { get; private set; }
        public CancellationToken LastReleaseToken { get; private set; }
        public Func<string, CancellationToken, ValueTask<RuntimeExecutionLease>> OnAcquire { get; init; } =
            (executionId, _) => ValueTask.FromResult(NewLease(executionId));
        public Func<RuntimeExecutionLease, CancellationToken, ValueTask<RuntimeExecutionOwnershipTransitionResult>> OnRelease { get; set; } =
            (lease, _) => ValueTask.FromResult(Applied(lease));

        public ValueTask<RuntimeExecutionLease> AcquireAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
        {
            AcquireCalls++;
            cancellationToken.ThrowIfCancellationRequested();
            return OnAcquire(workflowExecutionId, cancellationToken);
        }

        public ValueTask<RuntimeExecutionOwnershipTransitionResult> ReleaseAsync(RuntimeExecutionLease lease,
            CancellationToken cancellationToken = default)
        {
            LastReleased = lease;
            LastReleaseToken = cancellationToken;
            cancellationToken.ThrowIfCancellationRequested();
            return OnRelease(lease, cancellationToken);
        }

        public ValueTask<RuntimeExecutionOwnershipTransitionResult> HeartbeatAsync(RuntimeExecutionLease lease,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask EnsureCurrentAsync(string workflowExecutionId, long fencingToken,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    public void Dispose() => Directory.Delete(_journal, recursive: true);
}
