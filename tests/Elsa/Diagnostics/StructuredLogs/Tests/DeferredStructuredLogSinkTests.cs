using Elsa.Diagnostics.StructuredLogs.Core.Contracts;
using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Elsa.Diagnostics.StructuredLogs.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Elsa.Diagnostics.StructuredLogs.Tests;

/// <summary>
/// The capture sink is constructed on the first captured event, which arrives while a shell is still activating. A
/// construction that fails then must not disable capture for the life of the process, as the cached seed in
/// <c>StructuredLogSink</c> did before #1778. These tests run the feature's own composition, with a hook on the store
/// resolution that every sink construction performs.
/// </summary>
public sealed class DeferredStructuredLogSinkTests : IAsyncDisposable
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(30);

    private readonly ServiceProvider _provider;
    private readonly ILogger _logger;
    private Action<IServiceProvider> _onStoreConstruction = _ => { };
    private int _storeConstructions;

    public DeferredStructuredLogSinkTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        // Registered ahead of the feature, whose own store registration is a TryAdd.
        services.AddSingleton<IStructuredLogStore>(provider =>
        {
            Interlocked.Increment(ref _storeConstructions);
            _onStoreConstruction(provider);
            return provider.GetRequiredService<InMemoryStructuredLogStore>();
        });
        new StructuredLogsFeature().ConfigureServices(services);
        _provider = services.BuildServiceProvider();
        _logger = _provider.GetRequiredService<ILoggerFactory>().CreateLogger("Shell.Activation");
    }

    [Fact]
    public async Task A_failed_construction_is_retried_by_the_next_event()
    {
        _onStoreConstruction = _ =>
        {
            if (_storeConstructions == 1)
                throw new InvalidOperationException("no such table: elsa_structured_log_stream_states");
        };

        _logger.LogInformation("before the store is ready");
        _logger.LogInformation("after the store is ready");

        Assert.Equal(["after the store is ready"], await CapturedMessagesAsync());
        Assert.Equal(2, _storeConstructions);
    }

    [Fact]
    public async Task An_event_logged_by_the_construction_is_dropped_instead_of_constructing_again()
    {
        var reentered = false;
        _onStoreConstruction = provider =>
        {
            if (_storeConstructions > 1)
            {
                // Throwing ends what would otherwise recurse until the stack overflows.
                reentered = true;
                throw new InvalidOperationException("The sink construction re-entered itself.");
            }

            // A durable store logs through the pipeline being captured while it is resolved, as EF does.
            provider.GetRequiredService<ILoggerFactory>().CreateLogger("Store.Construction").LogInformation("resolving the store");
        };

        _logger.LogInformation("first event");

        Assert.False(reentered);
        Assert.Equal(["first event"], await CapturedMessagesAsync());
    }

    [Fact]
    public async Task Events_waiting_on_a_failed_construction_drop_their_entries_instead_of_each_retrying()
    {
        using var constructing = new ManualResetEventSlim();
        using var failConstruction = new ManualResetEventSlim();
        _onStoreConstruction = _ =>
        {
            if (_storeConstructions > 1)
                return;
            constructing.Set();
            failConstruction.Wait();
            throw new InvalidOperationException("no such table: elsa_structured_log_stream_states");
        };
        var first = Task.Factory.StartNew(() => _logger.LogInformation("first event"), TaskCreationOptions.LongRunning);
        Assert.True(constructing.Wait(WaitLimit));
        var waiter = new Thread(() => _logger.LogInformation("waiting event"));
        waiter.Start();
        Assert.True(
            SpinWait.SpinUntil(() => (waiter.ThreadState & ThreadState.WaitSleepJoin) != 0, WaitLimit),
            "The second event never blocked on the construction in progress.");

        failConstruction.Set();
        await first.WaitAsync(WaitLimit);
        Assert.True(waiter.Join(WaitLimit));

        Assert.Equal(1, _storeConstructions);
        _logger.LogInformation("later event");
        Assert.Equal(["later event"], await CapturedMessagesAsync());
        Assert.Equal(2, _storeConstructions);
    }

    public ValueTask DisposeAsync() => _provider.DisposeAsync();

    private async Task<string[]> CapturedMessagesAsync() =>
        (await _provider.GetRequiredService<InMemoryStructuredLogStore>().GetRecentAsync(StructuredLogFilter.None))
        .Select(entry => entry.Message).ToArray();
}
