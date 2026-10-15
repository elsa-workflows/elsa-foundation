using Elsa.Activities.Http.Constants;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Integrations.Sdk.Http;

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
/// no resolver or cache of its own. Service connections, <c>[ActivityConnection]</c>,
/// <c>SensitiveValue</c>, <c>ConnectionUnavailable</c>, and the <c>http</c> type (api-key / basic /
/// bearer) are #2729 and are not declared here.
/// </remarks>
public abstract class ConnectorActivity<TResult> : Activity<TResult>
{
    protected ConnectorActivity()
    {
    }

    protected ConnectorActivity(IHttpClientFactory httpClientFactory)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        HttpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// The named <see cref="HttpActivityConstants.HttpClientName"/> factory, when the connector
    /// opted into HTTP. Non-HTTP connectors omit the factory constructor.
    /// </summary>
    protected IHttpClientFactory? HttpClientFactory { get; }

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
    /// Pushes outbound HTTP auth onto the named client for the duration of the returned scope.
    /// Call this from the activity method itself, not from an async helper.
    /// </summary>
    protected static IDisposable PushHttpAuth(Action<HttpRequestMessage> apply) =>
        IntegrationsHttpAuthScope.Push(apply);
}

/// <inheritdoc cref="ConnectorActivity{TResult}"/>
public abstract class ConnectorActivity : ConnectorActivity<ActivityUnit>
{
    protected ConnectorActivity()
    {
    }

    protected ConnectorActivity(IHttpClientFactory httpClientFactory) : base(httpClientFactory)
    {
    }
}
