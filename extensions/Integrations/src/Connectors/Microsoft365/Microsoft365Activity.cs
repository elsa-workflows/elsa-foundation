using Elsa.Activities.Http.Connections;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Integrations.Sdk;

namespace Elsa.Integrations.Connectors.Microsoft365;

public abstract class Microsoft365Activity<TResult> : ConnectorActivity<TResult>
{
    protected Microsoft365Activity(
        IHttpClientFactory httpClientFactory,
        IHttpConnectionApplicator connectionApplicator)
        : base(httpClientFactory, connectionApplicator)
    {
    }

    [ActivityInput(Key = nameof(Connection), DisplayName = "Connection", Tier = InputTier.Essential)]
    [ActivityConnection(ConnectionType = "http")]
    public ConnectionReference? Connection { get; set; }

    [ActivityInput(
        Key = nameof(GraphBaseUrl),
        DisplayName = "Graph base URL",
        Tier = InputTier.Optional,
        Description = "Microsoft Graph host. Default https://graph.microsoft.com. Use the national-cloud Graph URL when the Entra tenant is not commercial.")]
    public string? GraphBaseUrl { get; set; }

    internal Microsoft365RetryOptions Retry { get; set; } = Microsoft365RetryOptions.Default;

    internal Microsoft365UploadOptions Upload { get; set; } = Microsoft365UploadOptions.Default;

    internal Microsoft365ApiClient CreateClient() =>
        new(CreateHttpClient(), ApplyConnectionAsync, ResolveGraphBaseUri(), Retry, Upload);

    internal ActivityTransition<TResult> GraphFault(Microsoft365ApiException exception) =>
        ActivityTransition.Fault<TResult>(new ActivityFault(exception.Code, exception.Message, exception.IsRetryable));

    internal ValueTask<ActivityTransition<TResult>> ExecuteForTestsAsync(ActivityExecutionContext context) =>
        ExecuteAsync(context);

    private Uri ResolveGraphBaseUri()
    {
        var raw = string.IsNullOrWhiteSpace(GraphBaseUrl) ? Microsoft365Api.DefaultBaseUrl : GraphBaseUrl.Trim();
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new Microsoft365ApiException(
                Microsoft365ErrorCodes.InvalidInput,
                "Graph base URL must be an https URL.");
        }

        return uri;
    }
}

public abstract class Microsoft365Activity : Microsoft365Activity<ActivityUnit>
{
    protected Microsoft365Activity(
        IHttpClientFactory httpClientFactory,
        IHttpConnectionApplicator connectionApplicator)
        : base(httpClientFactory, connectionApplicator)
    {
    }
}
