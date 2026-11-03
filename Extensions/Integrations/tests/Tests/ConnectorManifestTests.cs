using System.Reflection;
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
        Assert.Contains(ping.Inputs, input => input.Key == "Connection" && input.DisplayName == "Connection" && !input.IsCredential);

        Assert.Equal("2.0.0", trigger.Version);
        Assert.True(trigger.IsTrigger);
        Assert.Contains(trigger.Inputs, input => input.Key == "SecretHeader" && input.Tier == "setting");
    }

    [Fact]
    public void FromAssembly_reads_Version_declared_on_a_base_type()
    {
        var manifest = ConnectorManifest.FromAssembly(typeof(DerivedFromVersionedBase).Assembly);
        var derived = Assert.Single(manifest.Activities, activity => activity.TypeName == typeof(DerivedFromVersionedBase).FullName);
        Assert.Equal("9.8.7", derived.Version);
    }

    [Fact]
    public void FromAssembly_skips_open_generic_activity_types()
    {
        var manifest = ConnectorManifest.FromAssembly(typeof(OpenGenericConnectorActivity<>).Assembly);
        Assert.DoesNotContain(manifest.Activities, activity =>
            activity.TypeName == typeof(OpenGenericConnectorActivity<>).FullName
            || (activity.TypeName?.Contains("OpenGenericConnectorActivity", StringComparison.Ordinal) ?? false));
    }

    [Fact]
    public void FromAssembly_reads_Icon_declared_on_a_base_type()
    {
        var manifest = ConnectorManifest.FromAssembly(typeof(DerivedWithoutOwnIcon).Assembly);
        var derived = Assert.Single(manifest.Activities, activity => activity.TypeName == typeof(DerivedWithoutOwnIcon).FullName);
        Assert.Equal("plug", derived.Icon);
    }

    [Fact]
    public void FromAssembly_uses_types_that_loaded_when_GetTypes_is_partial()
    {
        var loaded = typeof(SamplePingActivity);
        var exception = new ReflectionTypeLoadException([loaded, null], [new TypeLoadException("missing")]);
        Assert.Equal([loaded], ConnectorManifest.LoadedTypes(exception));
    }
}
