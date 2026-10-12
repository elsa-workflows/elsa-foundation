using Elsa.Workflows.Runtime.Http.Contracts;
using Elsa.Workflows.Runtime.Http.Options;
using Elsa.Workflows.Runtime.Http.Tasks;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Elsa.Workflows.Runtime.Http.Tests;

/// <summary>
/// The pump every node runs to converge its route table with other nodes (#2190). A shutdown must stop it quietly; any
/// other failure, a foreign cancellation included, must be logged and widen the interval, because a check that fails
/// in silence leaves endpoints published elsewhere returning 404 here with nothing to say why.
/// </summary>
public sealed class HttpEndpointRouteTableConvergencePumpTaskTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);
    private readonly ScriptedSynchronizer _synchronizer = new();
    private readonly RecordingLogger _logger = new();
    private readonly HttpEndpointRouteTableConvergenceOptions _options = new() { Interval = Interval, MaxBackoffInterval = TimeSpan.FromSeconds(16) };

    private HttpEndpointRouteTableConvergencePumpTask Pump() =>
        new(_synchronizer, Microsoft.Extensions.Options.Options.Create(_options), _logger);

    [Fact]
    public async Task EachTick_RunsOneConvergenceCheck()
    {
        var pump = Pump();

        await pump.ExecuteAsync(CancellationToken.None);
        await pump.ExecuteAsync(CancellationToken.None);

        Assert.Equal(2, _synchronizer.Checks);
        Assert.Equal(Interval, pump.CurrentSweepInterval);
        Assert.Empty(_logger.Errors);
    }

    [Fact]
    public async Task AFailedCheck_IsLogged_AndWidensTheInterval_UntilACheckSucceeds()
    {
        var pump = Pump();
        _synchronizer.Next = _ => throw new InvalidOperationException("database unavailable");

        await pump.ExecuteAsync(CancellationToken.None);
        await pump.ExecuteAsync(CancellationToken.None);

        Assert.Equal(2, _logger.Errors.Count);
        Assert.All(_logger.Errors, error => Assert.IsType<InvalidOperationException>(error));
        Assert.Equal(Interval * 2, pump.CurrentSweepInterval);

        _synchronizer.Next = null;
        await pump.ExecuteAsync(CancellationToken.None);
        Assert.Equal(Interval, pump.CurrentSweepInterval);
    }

    [Fact]
    public async Task TheShellStopping_EscapesAsCancellation_AndIsNotCountedAsAFailure()
    {
        var pump = Pump();
        using var stopping = new CancellationTokenSource();
        _synchronizer.Next = token =>
        {
            stopping.Cancel();
            token.ThrowIfCancellationRequested();
            return false;
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pump.ExecuteAsync(stopping.Token));

        Assert.Empty(_logger.Errors);
        Assert.Equal(Interval, pump.CurrentSweepInterval);
    }

    [Fact]
    public async Task AForeignCancellation_IsAFailedCheck_NotAShutdown()
    {
        // A provider command timeout can surface as OperationCanceledException while the shell is still running. Passing
        // it on as cancellation would end the tick unlogged and unbacked-off.
        var pump = Pump();
        _synchronizer.Next = _ => throw new OperationCanceledException("command timeout");

        await pump.ExecuteAsync(CancellationToken.None);

        var error = Assert.Single(_logger.Errors);
        Assert.IsType<OperationCanceledException>(error);
        Assert.Equal(Interval, pump.CurrentSweepInterval);
        await pump.ExecuteAsync(CancellationToken.None);
        Assert.Equal(Interval * 2, pump.CurrentSweepInterval);
    }

    private sealed class ScriptedSynchronizer : IHttpEndpointRouteTableSynchronizer
    {
        public int Checks { get; private set; }
        public Func<CancellationToken, bool>? Next { get; set; }

        public ValueTask RefreshAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The pump converges; it never refreshes unconditionally.");

        public ValueTask<bool> ConvergeAsync(CancellationToken cancellationToken = default)
        {
            Checks++;
            return ValueTask.FromResult(Next?.Invoke(cancellationToken) ?? true);
        }
    }

    private sealed class RecordingLogger : ILogger<HttpEndpointRouteTableConvergencePumpTask>
    {
        public List<Exception?> Errors { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Error)
                Errors.Add(exception);
        }
    }
}
