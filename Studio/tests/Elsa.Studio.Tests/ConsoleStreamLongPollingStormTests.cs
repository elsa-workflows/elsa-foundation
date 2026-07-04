using ConsoleLogStreaming.Core;
using ConsoleLogStreaming.Core.Providers;
using Elsa.Studio.ConsoleStream;
using Elsa.Studio.ConsoleStream.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Elsa.Studio.Tests;

/// <summary>
/// These tests drive the process-wide <see cref="ConsoleLogStreamingHost"/> (console tee, capture
/// pump, provider factory), so they must not overlap with anything else that could touch that
/// static state or write measurable stdout while the storm harness is counting requests.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ConsoleLogStreamingHostCollection
{
    public const string Name = "console-log-streaming-host";
}

/// <summary>
/// End-to-end regression tests for the long-polling request storm behind PR #215: a host that logs a
/// stdout line per request (CShells' ShellMiddleware "Resolved shell ...", UseHttpLogging, Serilog
/// request logging, ...) combined with SignalR long-polling fallback lets every captured line
/// complete the next pending poll 1:1 — a self-sustaining loop paced only by round-trip time.
/// The harness reproduces exactly that shape in-process and asserts that the paced provider wired by
/// <see cref="ConsoleStreamStudioServiceCollectionExtensions.AddConsoleStreamStudioHost"/> keeps the
/// request rate structurally bounded, independent of any logging filters.
/// </summary>
[Collection(ConsoleLogStreamingHostCollection.Name)]
public sealed class ConsoleStreamLongPollingStormTests
{
    private const string HubPath = "/_elsa/studio/diagnostics/console-logs/hub";

    [Fact]
    public async Task StudioHostBuildsThePacedProvider()
    {
        await ConsoleLogStreamingHost.ShutdownAsync();
        try
        {
            var services = new ServiceCollection();
            services.AddConsoleStreamStudioHost();
            await using var serviceProvider = services.BuildServiceProvider();

            Assert.IsType<PacedConsoleLogProvider>(serviceProvider.GetRequiredService<IConsoleLogProvider>());
        }
        finally
        {
            await ConsoleLogStreamingHost.ShutdownAsync();
        }
    }

    [Fact]
    public async Task LongPollingWithPerRequestStdoutLoggingStaysBounded()
    {
        var result = await RunLongPollingStormHarnessAsync(paced: true, TimeSpan.FromSeconds(2));

        // At one release per 100ms the feedback loop is capped at ~10 poll round-trips per second
        // (measured: ~18 requests in 2s), while the unpaced harness below measures >100/s.
        Assert.True(result.HubRequests < 60,
            $"Expected the paced provider to bound hub traffic, but saw {result.HubRequests} hub requests in 2s.");
        Assert.True(result.ItemsReceived > 0, "Expected the damped stream to stay live.");
    }

    [Fact]
    public async Task HarnessReproducesTheStormWithoutThePacedProvider()
    {
        // Guards the harness itself: with the pacing removed (and nothing else changed), the same
        // host must storm. If this ever stops storming, the bounded assertion above proves nothing.
        var result = await RunLongPollingStormHarnessAsync(paced: false, TimeSpan.FromSeconds(2));

        // Measured ~117 requests/s in-process; 50/s is far above anything the paced host can produce
        // while still leaving headroom for slow CI machines.
        Assert.True(result.HubRequests > 100,
            $"Expected the unpaced harness to reproduce the storm, but saw only {result.HubRequests} hub requests in 2s.");
    }

    private static async Task<(int HubRequests, int ItemsReceived)> RunLongPollingStormHarnessAsync(bool paced, TimeSpan duration)
    {
        await ConsoleLogStreamingHost.ShutdownAsync();

        // ConfigureProvider is first-wins until the next shutdown, so pre-empting it here makes the
        // paced factory installed by AddConsoleStreamStudioHost a no-op while keeping every other
        // aspect of the host identical between the two harness variants.
        if (!paced)
        {
            ConsoleLogStreamingHost.ConfigureProvider(context =>
                new InMemoryConsoleLogProvider(context.Options, context.RedactionPipeline, context.SourceRegistry));
        }

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddConsoleStreamStudioHost();

        var app = builder.Build();
        var hubRequests = 0;
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments(HubPath))
                Interlocked.Increment(ref hubRequests);

            // The per-request Information-style stdout line that fuels the feedback loop.
            Console.WriteLine($"Resolved shell 'default' for request path '{context.Request.Path}'.");
            await next();
        });
        app.MapConsoleStreamStudio();

        await app.StartAsync();
        try
        {
            var itemsReceived = 0;
            var connection = new HubConnectionBuilder()
                .WithUrl(new Uri(app.GetTestServer().BaseAddress, HubPath), options =>
                {
                    options.Transports = HttpTransportType.LongPolling;
                    options.HttpMessageHandlerFactory = _ => app.GetTestServer().CreateHandler();
                })
                .Build();

            await using (connection)
            {
                await connection.StartAsync();

                using var streamCts = new CancellationTokenSource();
                var pump = Task.Run(async () =>
                {
                    try
                    {
                        await foreach (var _ in connection.StreamAsync<object?>("StreamAsync", (object?)null, streamCts.Token))
                            Interlocked.Increment(ref itemsReceived);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                });

                // Ignite the feedback loop: with long polling there is no ambient traffic, so keep
                // writing stdout lines until the first one makes it through the stream and completes
                // a pending poll — from then on the loop feeds itself off the per-request log line.
                var ignitionDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
                while (Volatile.Read(ref itemsReceived) == 0 && DateTime.UtcNow < ignitionDeadline)
                {
                    if (pump.IsFaulted)
                        await pump; // surface stream failures instead of reporting an empty run
                    Console.WriteLine("storm-ignition");
                    await Task.Delay(100);
                }

                if (Volatile.Read(ref itemsReceived) == 0)
                    throw new InvalidOperationException("The storm harness failed to ignite: no stream item arrived within 15s.");

                var requestsAtIgnition = Volatile.Read(ref hubRequests);
                await Task.Delay(duration);
                var requestsDuringWindow = Volatile.Read(ref hubRequests) - requestsAtIgnition;

                streamCts.Cancel();
                await Task.WhenAny(pump, Task.Delay(TimeSpan.FromSeconds(5)));
                await connection.StopAsync();

                return (requestsDuringWindow, itemsReceived);
            }
        }
        finally
        {
            await app.DisposeAsync();
            await ConsoleLogStreamingHost.ShutdownAsync();
        }
    }
}
