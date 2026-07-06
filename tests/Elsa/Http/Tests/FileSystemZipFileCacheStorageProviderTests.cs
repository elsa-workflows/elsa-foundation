using Elsa.Http.Core.Contracts;
using Elsa.Http.Options;
using Elsa.Http.Services;
using Elsa.Http.Tests.Support;
using Xunit;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Elsa.Http.Tests;

public class FileSystemZipFileCacheStorageProviderTests
{
    private static FileSystemZipFileCacheStorage NewStorage() =>
        new(MsOptions.Create(new HttpZipFileCacheOptions { LocalCacheDirectory = Path.GetTempPath() }), new JsonPayloadSerializer());

    [Fact]
    public void GetStorage_UnderConcurrency_ConstructsStorageExactlyOnce()
    {
        // Deterministic (no timing) bite for the check-then-act race. A blocking, counting factory
        // parks the first thread(s) that pass the provider's initialization gate. With a thread-safe
        // Lazy<T>, exactly one thread ever runs the factory (the rest block on Lazy's lock) so the
        // factory is invoked once. With a non-atomic '??=', every thread passes the null-check and
        // invokes the factory, so the invocation count equals the number of threads.
        const int threads = 16;
        var factoryInvocations = 0;
        using var firstEntered = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        using var allCalled = new CountdownEvent(threads);

        var provider = new FileSystemZipFileCacheStorageProvider(() =>
        {
            Interlocked.Increment(ref factoryInvocations);
            firstEntered.Set();
            // Hold inside the factory so that, under a racing '??=', every thread that already passed
            // the null-check also enters here and is counted before any instance is published.
            release.Wait();
            return NewStorage();
        });

        var results = new IZipFileCacheStorage[threads];
        var workers = Enumerable.Range(0, threads).Select(i =>
        {
            var t = new Thread(() =>
            {
                allCalled.Signal();
                results[i] = provider.GetStorage();
            });
            t.Start();
            return t;
        }).ToArray();

        // Wait until every worker has started and at least one has reached the factory, then give the
        // racing threads a moment to pile into the factory (harmless for Lazy<T>, which admits only one).
        allCalled.Wait();
        firstEntered.Wait();
        Thread.Sleep(100);

        release.Set();
        foreach (var t in workers)
            t.Join();

        Assert.Equal(1, factoryInvocations);
        Assert.Single(results.Distinct());
    }

    [Fact]
    public void GetStorage_ReturnsSameInstanceOnRepeatedCalls()
    {
        var provider = new FileSystemZipFileCacheStorageProvider(NewStorage);
        Assert.Same(provider.GetStorage(), provider.GetStorage());
    }
}
