using System.Collections.Concurrent;
using System.Data.Common;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Tests;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit.Abstractions;

namespace Elsa.Etl.Persistence.ProviderTests;

/// <summary>Overlapping runtime transactions on actual servers; injected failures are tested separately.</summary>
public abstract class EtlRuntimeConcurrencyTests(EtlNativeProviderFixture fixture, ITestOutputHelper output)
{
    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Concurrent_admission_retries_native_conflicts_as_a_whole(bool differentBindings)
    {
        await using var database = await EtlProviderRuntimeDatabase.CreateTypedAsync(fixture);
        var gate = new FirstSaveGate();
        var conflicts = new ConcurrentQueue<string>();
        await using var leftContext = database.CreateContext(new NativeRaceSave(gate, conflicts));
        await using var rightContext = database.CreateContext(new NativeRaceSave(gate, conflicts));
        var leftStore = ((IEtlRuntimeTestDatabase)database).CreateStore(leftContext);
        var rightStore = ((IEtlRuntimeTestDatabase)database).CreateStore(rightContext);
        var admission = EtlRuntimeFixtures.Admission("native-admission-race", new EtlScope("tenant-race", "partition-race"));
        var other = differentBindings ? admission with
        {
            Bindings = admission.Bindings with { Output = admission.Bindings.Output with { Revision = "other" } }
        } : admission;

        EtlAdmissionResult[] results;
        try
        {
            results = await Task.WhenAll(leftStore.AdmitAsync(admission).AsTask(), rightStore.AdmitAsync(other).AsTask())
                .WaitAsync(TimeSpan.FromSeconds(90));
        }
        finally
        {
            foreach (var conflict in conflicts)
                output.WriteLine("Observed native conflict: " + conflict);
        }

        Assert.Equal([EtlStoreStatus.Applied, differentBindings ? EtlStoreStatus.Conflict : EtlStoreStatus.Replay],
            results.Select(result => result.Status).Order().ToArray());
        Assert.NotEmpty(conflicts);
        await using var observer = database.CreateContext();
        Assert.Single(await observer.Runs.AsNoTracking().ToListAsync());
        Assert.Empty(await observer.Terminals.AsNoTracking().ToListAsync());
    }

    private sealed class FirstSaveGate
    {
        private int arrivals;
        private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask ArriveAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref arrivals) == 2)
                released.TrySetResult();
            await released.Task.WaitAsync(TimeSpan.FromSeconds(60), cancellationToken);
        }
    }

    private sealed class NativeRaceSave(FirstSaveGate gate, ConcurrentQueue<string> conflicts) : SaveChangesInterceptor
    {
        private int joined;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref joined, 1) == 0)
                await gate.ArriveAsync(cancellationToken);
            return result;
        }

        public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
        {
            var failure = eventData.Exception;
            if (EfRelationalExceptionClassifier.IsSaveConflict(failure, EfWriteConflict.Concurrency | EfWriteConflict.UniqueKey) ||
                EfRelationalExceptionClassifier.IsTransientWriteConflict(failure))
            {
                for (var cause = failure; cause is not null; cause = cause.InnerException)
                {
                    if (cause is not DbException)
                        continue;
                    var type = cause.GetType();
                    var code = type.GetProperty("SqlState")?.GetValue(cause) ?? type.GetProperty("Number")?.GetValue(cause);
                    conflicts.Enqueue(type.Name + ":" + code);
                    break;
                }
            }
            return Task.CompletedTask;
        }
    }
}

[Collection(EtlPostgreSqlProviderCollection.Name)]
public sealed class EtlRuntimeConcurrencyPostgreSqlTests(EtlPostgreSqlProviderFixture fixture, ITestOutputHelper output)
    : EtlRuntimeConcurrencyTests(fixture, output);

[Collection(EtlSqlServerProviderCollection.Name)]
public sealed class EtlRuntimeConcurrencySqlServerTests(EtlSqlServerProviderFixture fixture, ITestOutputHelper output)
    : EtlRuntimeConcurrencyTests(fixture, output);

[Collection(EtlMySqlProviderCollection.Name)]
public sealed class EtlRuntimeConcurrencyMySqlTests(EtlMySqlProviderFixture fixture, ITestOutputHelper output)
    : EtlRuntimeConcurrencyTests(fixture, output);
