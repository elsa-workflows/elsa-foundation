using System.Text.Json.Nodes;
using Elsa.Workbench.Tests.BackgroundActivities;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Microsoft.Extensions.DependencyInjection;

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

    private ServiceProvider CreateHost(string role)
    {
        var (services, _) = NativeHostingWitnessTestServices.Create();
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

    public void Dispose() => Directory.Delete(_journal, recursive: true);
}
