using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Integrations.Sdk;

namespace Elsa.Integrations.Tests.Support;

[Activity(Icon = "plug")]
[Version("1.2.3")]
public sealed class SamplePingActivity : ConnectorActivity<string>
{
    public SamplePingActivity(IHttpClientFactory httpClientFactory) : base(httpClientFactory)
    {
    }

    [ActivityInput(Tier = InputTier.Optional, Description = "Absolute URL")]
    public string Url { get; set; } = "https://example.com/health";

    [ActivityInput(IsCredential = true)]
    public string? Authorization { get; set; }

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

[Activity(Icon = "webhook")]
[TriggerActivity]
[Version("2.0.0")]
public sealed class SampleWebhookTrigger : ConnectorActivity
{
    [ActivityInput(Tier = InputTier.Setting)]
    public string SecretHeader { get; set; } = "X-Hook-Secret";

    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context) =>
        ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value));
}
