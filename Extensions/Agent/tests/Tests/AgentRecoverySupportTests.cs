using System.Text.Json;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Services;
using Elsa.Api.AspNetCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Elsa.Agent.Tests;

/// <summary>
/// #3441 self-review round 1 (S2): a composition reports what a restart does to its sessions. Bootstrap names it, and a
/// durable store configured into a composition that cannot use it warns at activation instead of failing silently.
/// </summary>
public sealed class AgentRecoverySupportTests
{
    [Theory]
    [InlineData("default", "processLifetime")]
    [InlineData("durable", "durable")]
    [InlineData("durable-replaced-pauses", "unsupported")]
    [InlineData("default-replaced-pauses", "unsupported")]
    public async Task Bootstrap_reports_what_a_restart_does_to_the_compositions_sessions(string composition, string expected)
    {
        using var harness = Harness(composition);

        var (status, body) = await AgentRecoveryApi.SendAsync(harness.Host, "Bootstrap", AgentRecoveryApi.Principal("actor-1", "tenant-1"));

        Assert.Equal(200, status);
        Assert.Equal(expected, JsonDocument.Parse(body).RootElement.GetProperty("data").GetProperty("recovery").GetString());
    }

    [Theory]
    [InlineData("durable-replaced-pauses", true)]
    [InlineData("durable", false)]
    [InlineData("default-replaced-pauses", false)]
    public async Task Activation_warns_only_when_a_configured_durable_store_cannot_be_used_for_recovery(string composition, bool warns)
    {
        using var harness = Harness(composition);
        var logger = new ListLogger();
        var reporter = new AgentRecoverySupportReporter(harness.Host.Services.GetRequiredService<IServiceScopeFactory>(), logger);

        await reporter.InitializeAsync(CancellationToken.None);

        Assert.Equal(warns, logger.Entries.Any(x => x.Level == LogLevel.Warning && x.Message.Contains("durable recovery", StringComparison.Ordinal)));
    }

    [Fact]
    public void The_default_composition_registers_the_activation_report()
        => Assert.Contains(AgentTestServices.Create().GetServices<CShells.Lifecycle.IShellInitializer>(), x => x is AgentRecoverySupportReporter);

    private static AgentRestartHarness Harness(string composition)
    {
        Func<TimeProvider, IAgentSessionRecordStore>? store = composition.StartsWith("durable", StringComparison.Ordinal)
            // Any fencing store other than the process-lifetime default counts as a durable selection.
            ? clock => new FaultingRecordStore(new InMemoryAgentSessionRecordStore(clock))
            : null;
        return new AgentRestartHarness(store, services =>
        {
            services.AddRouting().AddElsaEndpoints();
            if (composition.EndsWith("replaced-pauses", StringComparison.Ordinal))
                services.Replace(ServiceDescriptor.Singleton<IAgentTurnStateStore>(_ => new InMemoryAgentTurnStateStore()));
        });
    }

    private sealed class ListLogger : ILogger<AgentRecoverySupportReporter>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }
}
