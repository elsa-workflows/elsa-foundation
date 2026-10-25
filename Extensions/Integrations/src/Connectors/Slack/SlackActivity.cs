using Elsa.Activities.Http.Connections;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Integrations.Sdk;
using Microsoft.Extensions.Logging;

namespace Elsa.Integrations.Connectors.Slack;

public abstract class SlackActivity<TResult> : ConnectorActivity<TResult>
{
    private readonly ILoggerFactory _loggerFactory;

    protected SlackActivity(
        IHttpClientFactory httpClientFactory,
        IHttpConnectionApplicator connectionApplicator,
        ILoggerFactory loggerFactory)
        : base(httpClientFactory, connectionApplicator)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _loggerFactory = loggerFactory;
    }

    [ActivityInput(Key = nameof(Connection), DisplayName = "Connection", Tier = InputTier.Essential)]
    [ActivityConnection(ConnectionType = "http")]
    public ConnectionReference? Connection { get; set; }

    internal SlackRetryOptions Retry { get; set; } = SlackRetryOptions.Default;

    internal SlackApiClient CreateClient() =>
        new(CreateHttpClient(), ApplyConnectionAsync, _loggerFactory.CreateLogger<SlackApiClient>(), Retry);

    internal ActivityTransition<TResult> SlackFault(SlackApiException exception) =>
        ActivityTransition.Fault<TResult>(new ActivityFault(exception.Code, exception.Message, exception.IsRetryable));

    public ValueTask<ActivityTransition<TResult>> ExecuteForTestsAsync(ActivityExecutionContext context) =>
        ExecuteAsync(context);
}

public abstract class SlackActivity : SlackActivity<ActivityUnit>
{
    protected SlackActivity(
        IHttpClientFactory httpClientFactory,
        IHttpConnectionApplicator connectionApplicator,
        ILoggerFactory loggerFactory)
        : base(httpClientFactory, connectionApplicator, loggerFactory)
    {
    }
}
