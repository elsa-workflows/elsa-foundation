using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Integrations.Sdk;

namespace Company.Connector.Activities;

[Activity(Icon = "plug")]
[Version("1.0.0")]
public sealed class Ping : ConnectorActivity<string>
{
    public Ping(IHttpClientFactory httpClientFactory) : base(httpClientFactory)
    {
    }

    [ActivityInput(IsCredential = true)]
    public string? Authorization { get; set; }

    [ActivityInput(Tier = InputTier.Essential)]
    public string Url { get; set; } = "https://example.com/health";

    protected override async ValueTask<ActivityTransition<string>> ExecuteAsync(ActivityExecutionContext context)
    {
        using (PushHttpAuth(request =>
        {
            if (!string.IsNullOrWhiteSpace(Authorization))
                request.Headers.TryAddWithoutValidation("Authorization", Authorization);
        }))
        {
            var body = await CreateHttpClient().GetStringAsync(Url, context.CancellationToken);
            return ActivityTransition.Complete(body);
        }
    }
}
