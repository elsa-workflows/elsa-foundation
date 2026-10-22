using Elsa.Activities.Http.Activities;
using Elsa.Activities.Http.Connections;
using Elsa.Activities.Http.Constants;
using Elsa.Activities.Http.Options;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Connections.Core.Contracts;
using Elsa.Connections.Core.Models;
using Elsa.Connections.Extensions;
using Elsa.Connections.Options;
using Elsa.Connections.Workflows.Extensions;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Extensions;
using Elsa.Secrets.Options;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Values;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Integrations.Connectors.OAuth2.Tests;

internal static class OAuth2TestSupport
{
    public const string TenantId = "tenant-a";
    public const string ConnectionName = "http-oauth";
    public const string ActivityExecutionId = "actexec-oauth2";
    public const string EncryptionKey = "oauth2-connector-tests-key";

    public static async Task<OAuth2Harness> StartAsync(
        OAuth2AuthenticationScheme scheme,
        string clientSource = OAuth2ClientSources.Valence,
        Action<MockOAuthServer>? configureServer = null,
        TimeProvider? timeProvider = null)
    {
        var server = new MockOAuthServer();
        configureServer?.Invoke(server);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Elsa:Secrets:EncryptionKey"] = EncryptionKey,
                ["Elsa:Connections:PublicBaseUrl"] = "https://app.example"
            })
            .Build();

        var logs = new CapturingLoggerProvider();
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddProvider(logs);
            builder.SetMinimumLevel(LogLevel.Trace);
        });
        services.AddSecrets(configuration);
        services.Configure<SecretsOptions>(options => options.EncryptionKey ??= EncryptionKey);
        services.AddConnections(configuration);
        services.AddConnectionsWorkflows();
        services.AddOAuth2Connector();
        services.AddOptions<HttpActivityOptions>();
        services.AddSingleton<IWorkflowExecutionPartitionAccessor>(_ => new FixedPartition(TenantId));
        services.AddScoped<IRuntimeSecretMask, RecordingMask>();
        if (timeProvider is not null)
            services.Replace(ServiceDescriptor.Singleton(timeProvider));

        services.AddHttpClient(HttpActivityConstants.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new NonDisposingHandler(server));

        var provider = services.BuildServiceProvider();
        var scope = provider.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        if (clientSource == OAuth2ClientSources.Valence &&
            sp.GetRequiredService<IValenceOAuthAppRegistry>() is InMemoryValenceOAuthAppRegistry valence)
        {
            valence.Register(new ValenceOAuthApp(scheme.ValenceAppId ?? "elsa.oauth2", server.ExpectedClientId, server.ExpectedClientSecret));
        }

        var secrets = sp.GetRequiredService<ISecretManager>();
        var secretFields = new Dictionary<string, ConnectionSecretFieldInput>();
        if (clientSource == OAuth2ClientSources.BringYourOwn)
        {
            await secrets.CreateAsync(TenantId, new CreateSecretRequest
            {
                Name = "oauth2-client-secret",
                Value = server.ExpectedClientSecret,
                TypeName = SecretTypeNames.Text
            });
            secretFields[OAuth2SecretFields.ClientSecret] = new() { ExistingSecretName = "oauth2-client-secret" };
        }

        var settings = new Dictionary<string, string>(scheme.ToConnectionSettings(), StringComparer.Ordinal)
        {
            [HttpConnectionSettingKeys.AllowedHosts] = "oauth.test"
        };

        var manager = sp.GetRequiredService<IConnectionManager>();
        await manager.CreateAsync(TenantId, new CreateConnectionRequest
        {
            Name = ConnectionName,
            AuthScheme = OAuth2AuthenticationScheme.Name,
            Settings = settings,
            SecretFields = secretFields
        });

        return new OAuth2Harness(provider, scope, server, logs);
    }

    public static OAuth2AuthenticationScheme ClientCredentialsScheme(MockOAuthServer server) => new()
    {
        GrantType = OAuth2GrantTypes.ClientCredentials,
        TokenUrl = server.TokenEndpoint.ToString(),
        ScopesUrl = server.ScopesEndpoint.ToString(),
        Scopes = "read",
        ClientSource = OAuth2ClientSources.Valence,
        ValenceAppId = "elsa.oauth2"
    };

    public static OAuth2AuthenticationScheme AuthorizationCodeScheme(MockOAuthServer server) => new()
    {
        GrantType = OAuth2GrantTypes.AuthorizationCode,
        AuthorizationUrl = server.AuthorizationEndpoint.ToString(),
        TokenUrl = server.TokenEndpoint.ToString(),
        ScopesUrl = server.ScopesEndpoint.ToString(),
        Scopes = "read write",
        ClientSource = OAuth2ClientSources.Valence,
        ValenceAppId = "elsa.oauth2"
    };

    public static async Task<IActivityCompletionTransition<SendHttpRequestResult>> SendAsync(OAuth2Harness harness, Uri url)
    {
        var factory = harness.Services.GetRequiredService<IHttpClientFactory>();
        var options = harness.Services.GetRequiredService<IOptions<HttpActivityOptions>>();
        var applicator = harness.Services.GetRequiredService<IHttpConnectionApplicator>();
        var activity = new SendHttpRequest(factory, options, applicator)
        {
            Url = url,
            Method = "GET",
            Connection = new ConnectionReference(ConnectionName, ConnectionTypeNames.Http)
        };
        var context = new ActivityExecutionContext("wf-1", ActivityExecutionId, "att-1", "node-1", CancellationToken.None);
        var transition = await ((IActivity)activity).ExecuteAsync(context);
        return Assert.IsAssignableFrom<IActivityCompletionTransition<SendHttpRequestResult>>(transition);
    }

    public static async Task AuthorizeAsync(OAuth2Harness harness, MockOAuthServer server)
    {
        var tokens = harness.Services.GetRequiredService<OAuth2TokenService>();
        var resolver = harness.Services.GetRequiredService<IConnectionResolver>();
        await using var connection = await resolver.ResolveAsync(new ConnectionResolveRequest
        {
            TenantId = TenantId,
            Name = ConnectionName,
            RequiredType = ConnectionTypeNames.Http,
            Owner = ConnectionOwners.Tenant
        });
        var started = await tokens.BeginAuthorizationCodeAsync(connection);
        var code = server.ApproveAuthorization(new Uri(started.AuthorizationUrl));
        var callback = harness.Services.GetRequiredService<IEnumerable<IOAuthCallbackHandler>>();
        OAuthCallbackResponse? handled = null;
        foreach (var handler in callback)
        {
            handled = await handler.TryHandleAsync(new OAuthCallbackRequest(code, started.State, null, null));
            if (handled is not null)
                break;
        }

        Assert.NotNull(handled);
        Assert.Equal(200, handled.StatusCode);
    }

    private sealed class FixedPartition(string value) : IWorkflowExecutionPartitionAccessor
    {
        public WorkflowExecutionPartition Current { get; } = new(value);
    }
}

internal sealed class OAuth2Harness(
    ServiceProvider root,
    AsyncServiceScope scope,
    MockOAuthServer server,
    CapturingLoggerProvider logs) : IAsyncDisposable
{
    public IServiceProvider Services => scope.ServiceProvider;
    public MockOAuthServer Server => server;
    public CapturingLoggerProvider Logs => logs;
    public IRuntimeSecretMask Mask => Services.GetRequiredService<IRuntimeSecretMask>();
    public ISecretManager Secrets => Services.GetRequiredService<ISecretManager>();
    public ISecretValueResolver SecretValues => Services.GetRequiredService<ISecretValueResolver>();

    public async ValueTask DisposeAsync()
    {
        await scope.DisposeAsync();
        await root.DisposeAsync();
        server.Dispose();
    }
}

internal sealed class RecordingMask : IRuntimeSecretMask
{
    private readonly DefaultRuntimeSecretMask _inner = new();
    private readonly Dictionary<string, List<(string Name, string Value)>> _values = new(StringComparer.Ordinal);

    public void Register(string activityExecutionId, string referenceName, string value)
    {
        _inner.Register(activityExecutionId, referenceName, value);
        if (!_values.TryGetValue(activityExecutionId, out var list))
            _values[activityExecutionId] = list = [];
        list.Add((referenceName, value));
    }

    public bool HasRegistrations(string activityExecutionId) => _inner.HasRegistrations(activityExecutionId);

    public IReadOnlyList<(string Name, string Value)> Registrations(string activityExecutionId) =>
        _values.GetValueOrDefault(activityExecutionId) ?? [];

    public string Mask(string activityExecutionId, string text) => _inner.Mask(activityExecutionId, text);

    public void Release(string activityExecutionId) => _inner.Release(activityExecutionId);
}

internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly List<string> _lines = [];

    public IReadOnlyList<string> Lines
    {
        get
        {
            lock (_lines)
                return [.._lines];
        }
    }

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(_lines);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(List<string> lines) : ILogger
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (lines)
                lines.Add(formatter(state, exception));
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose()
        {
        }
    }
}
