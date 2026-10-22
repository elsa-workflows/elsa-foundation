using Elsa.Activities.Http.Connections;
using Elsa.Activities.Http.Constants;
using Elsa.Activities.Http.Services;
using Elsa.Integrations.Sdk.DependencyInjection;
using Elsa.Secrets.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Logging;
using Microsoft.Extensions.Logging;

namespace Elsa.Integrations.Sdk.Harness;

/// <summary>
/// In-memory host for connector tests: fake <see cref="ISecretValueResolver"/>, the named Elsa
/// HTTP client, and the unbound <see cref="IHttpConnectionApplicator"/>. Compose
/// <c>AddConnectionsWorkflows</c> in the test host when a real http connection must apply.
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
        Secrets = provider.GetRequiredService<ISecretValueResolver>() as InMemorySecretValueResolver
            ?? throw new InvalidOperationException("The harness requires InMemorySecretValueResolver.");
        Http = provider.GetRequiredService<RecordingHttpMessageHandler>();
        HttpClientFactory = provider.GetRequiredService<IHttpClientFactory>();
        HttpClient = HttpClientFactory.CreateClient(HttpActivityConstants.HttpClientName);
        ConnectionApplicator = provider.GetRequiredService<IHttpConnectionApplicator>();
    }

    public string TenantId { get; }

    public InMemorySecretValueResolver Secrets { get; }

    public RecordingHttpMessageHandler Http { get; }

    public IHttpClientFactory HttpClientFactory { get; }

    public HttpClient HttpClient { get; }

    public IHttpConnectionApplicator ConnectionApplicator { get; }

    public IServiceProvider Services => _provider;

    public IReadOnlyList<string> LogMessages => _logs.Messages;

    public static ConnectorHarness Create(string tenantId = "tenant-1")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var logs = new CapturingLoggerProvider();
        var http = new RecordingHttpMessageHandler();
        var secrets = new InMemorySecretValueResolver();

        var services = new ServiceCollection();
        services.AddSingleton(http);
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

        return new ConnectorHarness(services.BuildServiceProvider(), logs, tenantId);
    }

    public ValueTask DisposeAsync()
    {
        _provider.Dispose();
        return ValueTask.CompletedTask;
    }
}
