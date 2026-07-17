using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimePostCommitIntentDispatcherTests
{
    private const string MarkerKind = "Test.Marker";
    private static readonly DateTimeOffset Now = new(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task DispatchAsync_SelectsTheSingleOrdinalHandler()
    {
        var marker = new Marker();
        using var provider = NewProvider(marker);
        using var scope = provider.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IRuntimePostCommitIntentDispatcher>();
        var intent = NewIntent(MarkerKind);

        await dispatcher.DispatchAsync(intent);

        Assert.Same(intent, Assert.Single(marker.Intents));
    }

    [Fact]
    public async Task DispatchAsync_RejectsUnsupportedKindWithActionableOrdinalDiagnostic()
    {
        using var provider = NewProvider(new Marker());
        using var scope = provider.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IRuntimePostCommitIntentDispatcher>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            dispatcher.DispatchAsync(NewIntent("test.marker")).AsTask());

        Assert.Contains("test.marker", exception.Message);
        Assert.Contains("no runtime post-commit intent handler is registered", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DispatchAsync_PropagatesHandlerFailureToTheOutboxBoundary()
    {
        var expected = new InvalidOperationException("marker delivery failed");
        using var provider = NewProvider(new Marker { Failure = expected });
        using var scope = provider.CreateScope();

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scope.ServiceProvider.GetRequiredService<IRuntimePostCommitIntentDispatcher>()
                .DispatchAsync(NewIntent(MarkerKind)).AsTask());

        Assert.Same(expected, actual);
    }

    private static ServiceProvider NewProvider(Marker marker)
    {
        var services = new ServiceCollection();
        services.AddSingleton(marker);
        services.AddRuntimePostCommitIntentHandler<MarkerHandler>(MarkerKind);
        services.AddRuntimePostCommitIntentHandler<MarkerHandler>(MarkerKind);
        services.AddScoped<IRuntimePostCommitIntentDispatcher, RuntimePostCommitIntentDispatcher>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static RuntimePostCommitIntent NewIntent(string kind) =>
        new(
            intentId: "intent-1",
            workflowExecutionId: "wfexec-1",
            kind: kind,
            recordedAt: Now,
            activityExecutionId: "actexec-1",
            idempotencyKey: "intent-1",
            payload: JsonSerializer.SerializeToElement(new { value = 42 }),
            metadata: new Dictionary<string, string>());

    private sealed class Marker
    {
        public List<RuntimePostCommitIntent> Intents { get; } = [];
        public Exception? Failure { get; init; }
    }

    private sealed class MarkerHandler(Marker marker) : IRuntimePostCommitIntentHandler
    {
        public ValueTask HandleAsync(RuntimePostCommitIntent intent, CancellationToken cancellationToken = default)
        {
            if (marker.Failure is not null)
                throw marker.Failure;

            marker.Intents.Add(intent);
            return ValueTask.CompletedTask;
        }
    }
}
