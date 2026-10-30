using Elsa.Integrations.Connectors.Microsoft365.Activities;
using Elsa.Integrations.Sdk;
using Elsa.Integrations.Sdk.Manifest;

namespace Elsa.Integrations.Connectors.Microsoft365.Tests;

public sealed class Microsoft365SecretAndManifestTests
{
    [Fact]
    public async Task Token_client_secret_and_raw_body_are_absent_from_faults_and_logs()
    {
        await using var harness = Microsoft365TestSupport.CreateHarness();
        harness.Http.ResponseStatus = System.Net.HttpStatusCode.BadRequest;
        harness.Http.ResponseContent = Microsoft365TestSupport.GraphError(
            "ErrorInvalidRequest",
            "The request is malformed.",
            extraJson: "\"innerError\":{\"request-id\":\"leak-me\",\"access_token\":\"" + Microsoft365TestSupport.Token + "\",\"client_secret\":\"" + Microsoft365TestSupport.ClientSecret + "\",\"date\":\"2026-10-25T00:00:00\"}");
        var activity = Microsoft365TestSupport.Activity<SendMail>(harness, send =>
        {
            send.To = "ada@example.com";
            send.Subject = "Hello";
            send.Body = "World";
        });

        var fault = Microsoft365TestSupport.Fault(await activity.ExecuteForTestsAsync(Microsoft365TestSupport.Context()));
        Assert.DoesNotContain(Microsoft365TestSupport.Token, fault.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Microsoft365TestSupport.ClientSecret, fault.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("innerError", fault.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("request-id", fault.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("leak-me", fault.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Microsoft365TestSupport.Token, fault.Code, StringComparison.Ordinal);
        Assert.DoesNotContain(
            harness.LogMessages,
            message => message.Contains(Microsoft365TestSupport.Token, StringComparison.Ordinal)
                || message.Contains(Microsoft365TestSupport.ClientSecret, StringComparison.Ordinal));
    }

    [Fact]
    public void Manifest_lists_every_v1_activity_and_every_input_has_a_display_name()
    {
        var manifest = ConnectorManifest.FromAssembly(typeof(SendMail).Assembly);
        var expected = new[]
        {
            typeof(SendMail),
            typeof(ListMessages),
            typeof(CreateEvent),
            typeof(ListEvents),
            typeof(GetUser),
            typeof(UploadFile),
            typeof(DownloadFile),
            typeof(PostTeamsChannelMessage)
        };

        Assert.Equal(expected.Length, manifest.Activities.Count);
        foreach (var type in expected)
        {
            var entry = Assert.Single(manifest.Activities, activity => activity.TypeName == type.FullName);
            Assert.Equal("1.0.0", entry.Version);
            Assert.False(string.IsNullOrWhiteSpace(entry.Icon));
            Assert.False(entry.IsTrigger);
            Assert.Contains(entry.Inputs, input => input.Key == "Connection" && input.DisplayName == "Connection");
            Assert.Contains(entry.Inputs, input => input.Key == "GraphBaseUrl" && input.DisplayName == "Graph base URL");
            Assert.All(entry.Inputs, input => Assert.False(string.IsNullOrWhiteSpace(input.DisplayName), $"{type.Name}.{input.Key} is missing DisplayName."));
        }
    }

    [Fact]
    public void Package_is_activities_only()
    {
        var types = typeof(SendMail).Assembly.GetTypes();
        Assert.DoesNotContain(types, type => Implements(type, "IShellFeature"));
        Assert.DoesNotContain(types, type => Implements(type, "IHostedService"));
        Assert.DoesNotContain(types, type => type.Name.EndsWith("DbContext", StringComparison.Ordinal) || type.BaseType?.Name == "DbContext");
        Assert.DoesNotContain(types, type => typeof(DelegatingHandler).IsAssignableFrom(type));
        Assert.DoesNotContain(types, type => type.Name.Contains("Valence", StringComparison.OrdinalIgnoreCase));

        var names = typeof(SendMail).Assembly.GetReferencedAssemblies().Select(name => name.Name).ToHashSet(StringComparer.Ordinal);
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
