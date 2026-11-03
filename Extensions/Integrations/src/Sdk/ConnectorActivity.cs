using Elsa.Activities.Http.Connections;
using Elsa.Activities.Http.Constants;
using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Integrations.Sdk;

/// <summary>
/// Base for connector activities. v1 is activities only: no host endpoints, hosted services, EF
/// contexts, or other host-integrated features, so the package stays intended to be collectible
/// (#2726 rule 9). Collectible loading is unproven; foundation has no production collectible load
/// path (ADR 0070/0071).
/// </summary>
/// <remarks>
/// Credentials are <c>[ActivityInput(IsCredential = true)]</c> and resolve at activation through
/// foundation <c>ISecretValueResolver</c> (via <c>IRuntimeSecretResolver</c>). This type has
/// no resolver or cache of its own. HTTP connectors take
/// <see cref="IHttpConnectionApplicator"/> and apply a bound <see cref="ConnectionReference"/>
/// at send time, the same seam <c>SendHttpRequest</c> uses. Service connections,
/// <c>[ActivityConnection]</c>, <c>SensitiveValue</c>, <c>ConnectionUnavailable</c>, and the
/// <c>http</c> type (api-key / basic / bearer) are owned by Connections.
/// </remarks>
public abstract class ConnectorActivity<TResult> : Activity<TResult>
{
    protected ConnectorActivity()
    {
    }

    protected ConnectorActivity(IHttpClientFactory httpClientFactory, IHttpConnectionApplicator connectionApplicator)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(connectionApplicator);
        HttpClientFactory = httpClientFactory;
        ConnectionApplicator = connectionApplicator;
    }

    /// <summary>
    /// The named <see cref="HttpActivityConstants.HttpClientName"/> factory, when the connector
    /// opted into HTTP. Non-HTTP connectors omit the HTTP constructor.
    /// </summary>
    protected IHttpClientFactory? HttpClientFactory { get; }

    /// <summary>
    /// The composed http connection applicator. <c>ActivitiesHttp</c> registers an unbound
    /// default; <c>ConnectionsWorkflows</c> replaces it. This type does not add a second
    /// authenticator or DelegatingHandler on the named client.
    /// </summary>
    protected IHttpConnectionApplicator? ConnectionApplicator { get; }

    protected HttpClient CreateHttpClient()
    {
        if (HttpClientFactory is null)
        {
            throw new InvalidOperationException(
                "Pass IHttpClientFactory to ConnectorActivity to use the named Elsa.Activities.Http client.");
        }

        return HttpClientFactory.CreateClient(HttpActivityConstants.HttpClientName);
    }

    /// <summary>
    /// Applies a bound <see cref="ConnectionReference"/> to <paramref name="request"/> through
    /// the composed <see cref="IHttpConnectionApplicator"/>. Call this after authored headers
    /// so the connection wins, matching <c>SendHttpRequest</c>.
    /// </summary>
    protected ValueTask ApplyConnectionAsync(
        HttpRequestMessage request,
        ConnectionReference? connection,
        ActivityExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        if (ConnectionApplicator is null)
        {
            throw new InvalidOperationException(
                "Pass IHttpConnectionApplicator to ConnectorActivity to apply a bound http connection.");
        }

        return ConnectionApplicator.ApplyAsync(request, connection, context, cancellationToken);
    }
}

/// <inheritdoc cref="ConnectorActivity{TResult}"/>
public abstract class ConnectorActivity : ConnectorActivity<ActivityUnit>
{
    protected ConnectorActivity()
    {
    }

    protected ConnectorActivity(IHttpClientFactory httpClientFactory, IHttpConnectionApplicator connectionApplicator)
        : base(httpClientFactory, connectionApplicator)
    {
    }
}
