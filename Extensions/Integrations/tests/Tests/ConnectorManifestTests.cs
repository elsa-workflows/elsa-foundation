using Elsa.Integrations.Sdk.Manifest;
using Elsa.Integrations.Tests.Support;

namespace Elsa.Integrations.Tests;

public sealed class ConnectorManifestTests
{
    [Fact]
    public void FromAssembly_copies_activity_attributes_and_is_not_a_reconciliation_source()
    {
        Assert.Null(typeof(ConnectorManifest).GetInterface("IActivityReconciliationSource"));

        var manifest = ConnectorManifest.FromAssembly(typeof(SamplePingActivity).Assembly);
        var ping = Assert.Single(manifest.Activities, activity => activity.TypeName == typeof(SamplePingActivity).FullName);
        var trigger = Assert.Single(manifest.Activities, activity => activity.TypeName == typeof(SampleWebhookTrigger).FullName);

        Assert.Equal("1.2.3", ping.Version);
        Assert.Equal("plug", ping.Icon);
        Assert.False(ping.IsTrigger);
        Assert.Contains(ping.Inputs, input => input.Key == "Url" && input.Tier == "optional");
        Assert.Contains(ping.Inputs, input => input.Key == "Authorization" && input.IsCredential && input.IsSensitive);

        Assert.Equal("2.0.0", trigger.Version);
        Assert.True(trigger.IsTrigger);
        Assert.Contains(trigger.Inputs, input => input.Key == "SecretHeader" && input.Tier == "setting");
    }
}
