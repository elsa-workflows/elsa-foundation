using Elsa.Integrations.Connectors.Slack.Activities;
using Elsa.Integrations.Sdk;
using Elsa.Integrations.Sdk.Manifest;

namespace Elsa.Integrations.Connectors.Slack.Tests;

public sealed class SlackSecretAndManifestTests
{
    [Fact]
    public async Task Token_is_absent_from_logs_outputs_and_faults()
    {
        await using var harness = SlackTestSupport.CreateHarness();
        harness.Http.ResponseContent = """{"ok":false,"error":"not_in_channel"}""";
        var activity = SlackTestSupport.Activity<SendMessage>(harness, send =>
        {
            send.Channel = "C1";
            send.Text = "hello";
        });

        var fault = SlackTestSupport.Fault(await activity.ExecuteForTestsAsync(SlackTestSupport.Context()));
        Assert.DoesNotContain(SlackTestSupport.Token, fault.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(SlackTestSupport.Token, fault.Code, StringComparison.Ordinal);
        Assert.DoesNotContain(harness.LogMessages, message => message.Contains(SlackTestSupport.Token, StringComparison.Ordinal));
    }

    [Fact]
    public void Manifest_lists_every_v1_activity()
    {
        var manifest = ConnectorManifest.FromAssembly(typeof(SendMessage).Assembly);
        var expected = new[]
        {
            typeof(SendMessage),
            typeof(ReplyInThread),
            typeof(UpdateMessage),
            typeof(AddReaction),
            typeof(ListChannels),
            typeof(LookupChannel),
            typeof(LookupUserByEmail),
            typeof(UploadFile)
        };

        Assert.Equal(expected.Length, manifest.Activities.Count);
        foreach (var type in expected)
        {
            var entry = Assert.Single(manifest.Activities, activity => activity.TypeName == type.FullName);
            Assert.Equal("1.0.0", entry.Version);
            Assert.False(string.IsNullOrWhiteSpace(entry.Icon));
            Assert.False(entry.IsTrigger);
            Assert.Contains(entry.Inputs, input => input.Key == "Connection" && input.DisplayName == "Connection");
        }
    }

    [Fact]
    public void Package_is_activities_only()
    {
        var types = typeof(SendMessage).Assembly.GetTypes();
        Assert.DoesNotContain(types, type => Implements(type, "IShellFeature"));
        Assert.DoesNotContain(types, type => Implements(type, "IHostedService"));
        Assert.DoesNotContain(types, type => type.Name.EndsWith("DbContext", StringComparison.Ordinal) || type.BaseType?.Name == "DbContext");
        Assert.DoesNotContain(types, type => typeof(DelegatingHandler).IsAssignableFrom(type));
        Assert.DoesNotContain(types, type => type.Name.Contains("Valence", StringComparison.OrdinalIgnoreCase));

        var names = typeof(SendMessage).Assembly.GetReferencedAssemblies().Select(name => name.Name).ToHashSet(StringComparer.Ordinal);
        Assert.DoesNotContain(names, name => name is not null && name.StartsWith("Elsa3.", StringComparison.Ordinal));
        Assert.DoesNotContain(names, name => name is "Elsa.Integrations.Connectors.OAuth2");
    }

    [Fact]
    public void Sdk_activities_only_loading_still_passes_for_the_sdk()
    {
        var types = typeof(ConnectorActivity).Assembly.GetTypes();
        Assert.DoesNotContain(types, type => Implements(type, "IShellFeature"));
        Assert.DoesNotContain(types, type => Implements(type, "IHostedService"));
    }

    private static bool Implements(Type type, string interfaceName) =>
        type is { IsClass: true, IsAbstract: false } && type.GetInterfaces().Any(candidate => candidate.Name == interfaceName);
}
