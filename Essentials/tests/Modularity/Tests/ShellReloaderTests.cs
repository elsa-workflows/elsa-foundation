using CShells.Lifecycle;
using Elsa.Modularity.Api.Services;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Elsa.Modularity.Tests;

public sealed class ShellReloaderTests
{
    private static readonly InvalidOperationException ConfigurationChanged =
        new("Configuration changed while composing shell 'Authoring'. Retry activation or reload.");

    [Fact]
    public async Task ReloadsOnceWhenTheFirstAttemptSucceeds()
    {
        var registry = new QueuedReloadRegistry((Exception?)null);

        Assert.Equal(1, await Reload(registry));
        Assert.Equal(1, registry.Reloads);
    }

    [Fact]
    public async Task RetriesWhenConfigurationChangedDuringComposition()
    {
        var registry = new QueuedReloadRegistry(ConfigurationChanged, null);

        Assert.Equal(1, await Reload(registry));
        Assert.Equal(2, registry.Reloads);
    }

    [Fact]
    public async Task StopsAfterThreeAttemptsWhenConfigurationKeepsChanging()
    {
        var registry = new QueuedReloadRegistry(ConfigurationChanged, ConfigurationChanged, ConfigurationChanged, null);

        Assert.Equal(0, await Reload(registry));
        Assert.Equal(3, registry.Reloads);
    }

    [Fact]
    public async Task ReportsAnyOtherFailureOnItsFirstAttempt()
    {
        var registry = new QueuedReloadRegistry(new InvalidOperationException("An initializer failed."), null);

        Assert.Equal(0, await Reload(registry));
        Assert.Equal(1, registry.Reloads);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(0)] // during the pause between the first and second attempt
    public async Task LogsAndPropagatesACancellationAfterTheSave(int cancelledAttempt)
    {
        var registry = new QueuedReloadRegistry(ConfigurationChanged, null);
        var logger = new RecordingLogger();
        using var cancellation = new CancellationTokenSource();
        registry.BeforeReload = attempt =>
        {
            if (attempt == cancelledAttempt) cancellation.Cancel();
            else if (cancelledAttempt == 0 && attempt == 1) cancellation.CancelAfter(TimeSpan.FromMilliseconds(50));
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new ShellReloader(registry, new NuplaneHostTestComposition.StubShell("Authoring"), logger).ReloadAsync(cancellation.Token));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains("was cancelled", StringComparison.Ordinal));
    }

    private static Task<int> Reload(QueuedReloadRegistry registry) =>
        new ShellReloader(registry, new NuplaneHostTestComposition.StubShell("Authoring")).ReloadAsync();

    private sealed class RecordingLogger : ILogger<ShellReloader>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }

    private sealed class QueuedReloadRegistry(params Exception?[] outcomes) : IShellRegistry
    {
        private readonly Queue<Exception?> _outcomes = new(outcomes);

        public int Reloads { get; private set; }

        public Action<int>? BeforeReload { get; set; }

        public Task<ReloadResult> ReloadAsync(string name, CancellationToken cancellationToken = default)
        {
            Reloads++;
            BeforeReload?.Invoke(Reloads);
            cancellationToken.ThrowIfCancellationRequested();
            var error = _outcomes.Dequeue();
            return Task.FromResult(new ReloadResult(name, error is null ? new NuplaneHostTestComposition.StubShell(name) : null, null, error));
        }

        public IShell? GetActive(string name) => throw new NotSupportedException();
        public Task<IShell> GetOrActivateAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IShell> ActivateAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ReloadResult>> ReloadActiveAsync(ReloadOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IDrainOperation> DrainAsync(IShell shell, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UnregisterBlueprintAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProvidedBlueprint?> GetBlueprintAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IShellBlueprintManager?> GetManagerAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ShellPage> ListAsync(ShellListQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IReadOnlyCollection<IShell> GetAll(string name) => [];
        public IReadOnlyCollection<IShell> GetActiveShells() => [];
        public void Subscribe(IShellLifecycleSubscriber subscriber) { }
        public void Unsubscribe(IShellLifecycleSubscriber subscriber) { }
    }
}
