using Elsa.Diagnostics.Persistence.Draining;
using Elsa.Diagnostics.Persistence.Observability;
using Xunit;

namespace Elsa.Diagnostics.Persistence.Tests.Fixtures;

public sealed class DiagnosticsDrainTestFixture : IAsyncDisposable
{
    private readonly List<DiagnosticsDrain<int, int>> _drains = [];

    public DiagnosticsDrain<int, int> Create(
        IDiagnosticsDrainTarget<int, int> target,
        IDiagnosticsPersistenceObserver? observer = null,
        int queueCapacity = 16,
        int batchSize = 4,
        int retentionInterval = 100,
        int maxAttempts = 3,
        TimeSpan? baseRetryDelay = null,
        TimeSpan? maxRetryDelay = null,
        TimeSpan? shutdownTimeout = null)
    {
        var drain = new DiagnosticsDrain<int, int>(target, new()
        {
            BatchSize = batchSize,
            QueueCapacity = queueCapacity,
            RetentionInterval = retentionInterval,
            MaxAttempts = maxAttempts,
            BaseRetryDelay = baseRetryDelay ?? TimeSpan.FromMilliseconds(1),
            MaxRetryDelay = maxRetryDelay ?? TimeSpan.FromMilliseconds(2),
            ShutdownTimeout = shutdownTimeout ?? TimeSpan.FromSeconds(2)
        }, observer);
        _drains.Add(drain);
        return drain;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var drain in Enumerable.Reverse(_drains))
            await drain.DisposeAsync();
    }
}

public abstract class DiagnosticsDrainTestBase : IAsyncLifetime
{
    protected DiagnosticsDrainTestFixture Fixture { get; } = new();

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => Fixture.DisposeAsync().AsTask();
}
