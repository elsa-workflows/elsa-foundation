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
/// In-memory host for connector tests: fake <see cref="ISecretValueResolver"/>, fake
/// <see cref="IHttpConnectionApplicator"/>, and the named Elsa HTTP client with header-free
/// outbound logging. Pass <c>configure</c> to <see cref="Create"/> to replace the applicator.
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
        Connections = provider.GetRequiredService<InMemoryHttpConnectionApplicator>();
        Http = provider.GetRequiredService<RecordingHttpMessageHandler>();
        HttpClientFactory = provider.GetRequiredService<IHttpClientFactory>();
        HttpClient = HttpClientFactory.CreateClient(HttpActivityConstants.HttpClientName);
        ConnectionApplicator = provider.GetRequiredService<IHttpConnectionApplicator>();
    }

    public string TenantId { get; }

    public InMemorySecretValueResolver Secrets { get; }

    public InMemoryHttpConnectionApplicator Connections { get; }

    public RecordingHttpMessageHandler Http { get; }

    public IHttpClientFactory HttpClientFactory { get; }

    public HttpClient HttpClient { get; }

    public IHttpConnectionApplicator ConnectionApplicator { get; }

    public IServiceProvider Services => _provider;

    public IReadOnlyList<string> LogMessages => _logs.Messages;

    public static ConnectorHarness Create(string tenantId = "tenant-1", Action<IServiceCollection>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var logs = new CapturingLoggerProvider();
        var http = new RecordingHttpMessageHandler();
        var secrets = new InMemorySecretValueResolver();
        var connections = new InMemoryHttpConnectionApplicator();

        var services = new ServiceCollection();
        services.AddSingleton(http);
        services.AddSingleton<ISecretValueResolver>(secrets);
        services.AddSingleton(connections);
        services.AddSingleton<IHttpConnectionApplicator>(connections);
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Trace);
            builder.AddProvider(logs);
        });
        services.AddElsaIntegrationsRuntime()
            .ConfigurePrimaryHttpMessageHandler(sp => sp.GetRequiredService<RecordingHttpMessageHandler>());
        configure?.Invoke(services);

        return new ConnectorHarness(services.BuildServiceProvider(), logs, tenantId);
    }

    public ValueTask DisposeAsync()
    {
        _provider.Dispose();
        return ValueTask.CompletedTask;
    }
}
