using System.Text.Json;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Services;
using Elsa.Api.AspNetCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Elsa.Agent.Tests;

/// <summary>
/// #3441 self-review rounds 1 and 2 (S2, P3-1): a composition reports what a restart does to its sessions. Bootstrap names
/// it, and a durable store configured into a composition that cannot use it fails activation instead of running partly durable.
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

    [Fact]
    public async Task Bootstrap_reports_unsupported_when_the_composition_cannot_be_evaluated()
    {
        using var harness = new AgentRestartHarness(configure: services =>
        {
            services.AddRouting().AddElsaEndpoints();
            services.Replace(ServiceDescriptor.Singleton<IAgentTurnStateStore>(_ => throw new InvalidOperationException("The paused-turn store is misconfigured.")));
        });

        var (status, body) = await AgentRecoveryApi.SendAsync(harness.Host, "Bootstrap", AgentRecoveryApi.Principal("actor-1", "tenant-1"));

        Assert.Equal(200, status);
        var data = JsonDocument.Parse(body).RootElement.GetProperty("data");
        Assert.Equal("unsupported", data.GetProperty("recovery").GetString());
        Assert.True(data.GetProperty("enabled").GetBoolean());
    }

    [Theory]
    [InlineData("durable-replaced-pauses", true)]
    [InlineData("durable", false)]
    [InlineData("default-replaced-pauses", false)]
    public async Task Activation_refuses_a_configured_durable_store_that_the_composition_cannot_use_for_recovery(string composition, bool refused)
    {
        using var harness = Harness(composition);
        var validator = new AgentDurableRecoveryValidator(harness.Host.Services.GetRequiredService<IServiceScopeFactory>(), new ListLogger());

        var failure = await Record.ExceptionAsync(() => validator.InitializeAsync(CancellationToken.None));

        if (!refused)
        {
            Assert.Null(failure);
            return;
        }
        var diagnostic = Assert.IsType<InvalidOperationException>(failure).Message;
        Assert.Contains("durable", diagnostic, StringComparison.Ordinal);
        Assert.Contains(typeof(IAgentTurnStateStore).Name, diagnostic, StringComparison.Ordinal);
    }

    [Fact]
    public void The_default_composition_registers_the_activation_validator()
        => Assert.Contains(AgentTestServices.Create().GetServices<CShells.Lifecycle.IShellInitializer>(), x => x is AgentDurableRecoveryValidator);

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

    private sealed class ListLogger : ILogger<AgentDurableRecoveryValidator>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }
}
