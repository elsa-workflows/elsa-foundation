using Elsa.Activities.Http.Constants;
using Elsa.Activities.Http.Services;
using Elsa.Integrations.Sdk.Connections;
using Elsa.Integrations.Sdk.DependencyInjection;
using Elsa.Secrets.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Logging;
using Microsoft.Extensions.Logging;

namespace Elsa.Integrations.Sdk.Harness;

/// <summary>
/// In-memory host for connector tests: fake connection lookup, fake secret resolver, and the named
/// Elsa HTTP client with connection auth on top of header-free outbound logging.
/// </summary>
public sealed class ConnectorHarness : IAsyncDisposable
{
    private readonly ServiceProvider _provider;
    private readonly CapturingLoggerProvider _logs;

    private ConnectorHarness(ServiceProvider provider, CapturingLoggerProvider logs, string tenantId)
    {
        _provider = provider;
        _logs = logs;
        TenantId = tenantId;
        Connections = provider.GetRequiredService<IConnectionLookup>() as InMemoryConnectionLookup
            ?? throw new InvalidOperationException("The harness requires InMemoryConnectionLookup.");
        Secrets = provider.GetRequiredService<ISecretValueResolver>() as InMemorySecretValueResolver
            ?? throw new InvalidOperationException("The harness requires InMemorySecretValueResolver.");
        Http = provider.GetRequiredService<RecordingHttpMessageHandler>();
        ConnectionContext = provider.GetRequiredService<IConnectionContext>();
    }

    public string TenantId { get; }

    public InMemoryConnectionLookup Connections { get; }

    public InMemorySecretValueResolver Secrets { get; }

    public RecordingHttpMessageHandler Http { get; }

    public IConnectionContext ConnectionContext { get; }

    public IServiceProvider Services => _provider;

    public IReadOnlyList<string> LogMessages => _logs.Messages;

    public static ConnectorHarness Create(string tenantId = "tenant-1")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var logs = new CapturingLoggerProvider();
        var http = new RecordingHttpMessageHandler();
        var connections = new InMemoryConnectionLookup();
        var secrets = new InMemorySecretValueResolver();

        var services = new ServiceCollection();
        services.AddSingleton(http);
        services.AddSingleton<IConnectionLookup>(connections);
        services.AddSingleton<ISecretValueResolver>(secrets);
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Trace);
            builder.AddProvider(logs);
        });
        services.TryAddSingleton<HttpActivityClientLogger>();
        services.AddElsaIntegrationsRuntime()
            .ConfigurePrimaryHttpMessageHandler(sp => sp.GetRequiredService<RecordingHttpMessageHandler>())
            .RemoveAllLoggers()
            .AddLogger<HttpActivityClientLogger>();
        services.AddSingleton<IConnectionContext>(sp => sp.GetRequiredService<IConnectionContextFactory>().Create(tenantId));

        return new ConnectorHarness(services.BuildServiceProvider(), logs, tenantId);
    }

    public ValueTask DisposeAsync()
    {
        _provider.Dispose();
        return ValueTask.CompletedTask;
    }
}
