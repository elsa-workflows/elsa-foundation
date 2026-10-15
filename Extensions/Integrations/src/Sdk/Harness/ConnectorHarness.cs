using Elsa.Activities.Http.Constants;
using Elsa.Activities.Http.Services;
using Elsa.Integrations.Sdk.Connections;
using Elsa.Integrations.Sdk.OAuth;
using Elsa.Integrations.Sdk.Secrets;
using Elsa.Secrets.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Elsa.Integrations.Sdk.Harness;

/// <summary>
/// STUB of the #2726 testing harness: in-memory connections, a fake <see cref="ISecretValueResolver"/>,
/// the named Elsa HTTP client, and a capturing secret mask.
/// </summary>
internal sealed class ConnectorHarness : IAsyncDisposable
{
    private readonly ServiceProvider _services;
    private readonly HttpMessageHandler? _ownedPrimaryHandler;

    private ConnectorHarness(
        ServiceProvider services,
        InMemoryConnectionStore connections,
        FakeSecretBackend secrets,
        InMemoryValenceOAuthAppRegistry valenceApps,
        CapturingLoggerProvider logs,
        HarnessAuthState auth,
        OAuth2TokenService tokens,
        IConnectionContext connectionContext,
        OAuthCallbackOptions callback,
        OAuthCallbackHandler callbackHandler,
        TimeProvider timeProvider,
        HttpMessageHandler? ownedPrimaryHandler)
    {
        _services = services;
        Connections = connections;
        Secrets = secrets;
        ValenceApps = valenceApps;
        Logs = logs;
        Auth = auth;
        Tokens = tokens;
        ConnectionContext = connectionContext;
        Callback = callback;
        CallbackHandler = callbackHandler;
        TimeProvider = timeProvider;
        _ownedPrimaryHandler = ownedPrimaryHandler;
    }

    public string TenantId { get; private init; } = "default";
    public InMemoryConnectionStore Connections { get; }
    public FakeSecretBackend Secrets { get; }
    public InMemoryValenceOAuthAppRegistry ValenceApps { get; }
    public CapturingLoggerProvider Logs { get; }
    public HarnessAuthState Auth { get; }
    public OAuth2TokenService Tokens { get; }
    public IConnectionContext ConnectionContext { get; }
    public OAuthCallbackOptions Callback { get; }
    public OAuthCallbackHandler CallbackHandler { get; }
    public TimeProvider TimeProvider { get; }
    public IServiceProvider Services => _services;
    public IRuntimeSecretMask Mask => Auth.Mask;

    public static ConnectorHarness Create(Action<ConnectorHarnessOptions>? configure = null)
    {
        var options = new ConnectorHarnessOptions();
        configure?.Invoke(options);

        var connections = new InMemoryConnectionStore();
        var secrets = new FakeSecretBackend();
        var valence = new InMemoryValenceOAuthAppRegistry();
        var logs = new CapturingLoggerProvider();
        var auth = new HarnessAuthState();
        var callback = options.Callback;
        var services = new ServiceCollection();
        services.AddSingleton<IConnectionStore>(connections);
        services.AddSingleton<ISecretValueResolver>(secrets);
        services.AddSingleton<IGrantSecretWriter>(secrets);
        services.AddSingleton<IValenceOAuthAppRegistry>(valence);
        services.AddSingleton(options.TimeProvider);
        services.AddSingleton(callback);
        services.AddSingleton(auth);
        services.AddSingleton<OAuth2TokenService>();
        services.AddSingleton<OAuthCallbackHandler>();
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddProvider(logs);
            builder.SetMinimumLevel(LogLevel.Trace);
        });
        services.TryAddSingleton<HttpActivityClientLogger>();
        services.AddSingleton<IActiveConnectionAccessor>(auth);
        services.AddTransient(sp =>
        {
            var state = sp.GetRequiredService<HarnessAuthState>();
            return new OAuth2DelegatingHandler(
                state,
                state.Tokens ?? throw new InvalidOperationException("Token service is not bound."),
                state.Mask,
                state.ActivityExecutionId);
        });
        services.AddHttpClient(HttpActivityConstants.HttpClientName)
            .RemoveAllLoggers()
            .AddLogger<HttpActivityClientLogger>()
            .AddHttpMessageHandler(sp => sp.GetRequiredService<OAuth2DelegatingHandler>())
            .ConfigurePrimaryHttpMessageHandler(() => options.PrimaryHandler is null
                ? new HttpClientHandler()
                : new NonDisposingHandler(options.PrimaryHandler));

        var provider = services.BuildServiceProvider();
        var tokens = provider.GetRequiredService<OAuth2TokenService>();
        auth.Tokens = tokens;
        var context = new ConnectionContext(options.TenantId, connections, provider.GetRequiredService<IHttpClientFactory>());
        var callbackHandler = provider.GetRequiredService<OAuthCallbackHandler>();

        return new ConnectorHarness(
            provider,
            connections,
            secrets,
            valence,
            logs,
            auth,
            tokens,
            context,
            callback,
            callbackHandler,
            options.TimeProvider,
            options.OwnsPrimaryHandler ? options.PrimaryHandler : null)
        {
            TenantId = options.TenantId
        };
    }

    public async ValueTask UseConnectionAsync(ConnectionRecord connection, CancellationToken cancellationToken = default)
    {
        await Connections.UpsertAsync(connection, cancellationToken);
        Auth.ActiveConnection = connection;
    }

    public ValueTask DisposeAsync()
    {
        _services.Dispose();
        _ownedPrimaryHandler?.Dispose();
        return ValueTask.CompletedTask;
    }
}

internal sealed class ConnectorHarnessOptions
{
    public string TenantId { get; set; } = "default";
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;
    public HttpMessageHandler? PrimaryHandler { get; set; }
    public bool OwnsPrimaryHandler { get; set; }
    public OAuthCallbackOptions Callback { get; set; } = new();
}
