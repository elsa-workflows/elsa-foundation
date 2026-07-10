using Elsa.Diagnostics.StructuredLogs.Persistence.EFCore.DbContext;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Diagnostics.StructuredLogs.Persistence.Tests;

/// <summary>
/// Delegates to <see cref="StructuredLogsTestHost"/> but can fail designated *async* creates (the
/// drain/prune path); the store's synchronous query creates and the test's own probes are untouched.
/// The create sequence is single-threaded (one drain loop), so call ordinals are stable.
/// </summary>
internal sealed class FaultInjectingFactory(StructuredLogsTestHost host) : IDbContextFactory<StructuredLogsDbContext>
{
    private int _asyncCreates;

    public Func<int, bool>? FailAsyncCreateWhen { get; set; }

    public StructuredLogsDbContext CreateDbContext() => host.CreateDbContext();

    public Task<StructuredLogsDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
    {
        var ordinal = Interlocked.Increment(ref _asyncCreates);

        if (FailAsyncCreateWhen?.Invoke(ordinal) == true)
            throw new InvalidOperationException($"Injected transient failure on async create #{ordinal}.");

        return Task.FromResult(host.CreateDbContext());
    }
}
