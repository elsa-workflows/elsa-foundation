using System.Text.Json;
using Elsa.Events.Core.Contracts;
using Elsa.Workflows.Runtime.Api.StandaloneTriggers;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Microsoft.AspNetCore.DataProtection;
using Xunit;

namespace Elsa.Workflows.Runtime.Api.Tests;

public sealed partial class StandaloneTriggerManagementTests
{
    private const string RawTriggerPayload = """{ "OwnerKey" : "Ren\u00e9<+>", "Number":18446744073709551615, "Scale":1.00, "Exponent":1e+00, "Null":null, "Items":[ 1.0, { "K":"\u0061" } ] }""";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Configuration_reads_and_saves_retain_complete_raw_text_when_no_protected_value_changes(bool absentProtectedField)
    {
        var original = "{ \"eventName\" : \"Before\", \"payload\" : " + RawTriggerPayload + " }";
        var proposed = "{  \"eventName\": \"After\", \"payload\" : " + RawTriggerPayload + "  }";
        TriggerConfigurationField[] fields = absentProtectedField
            ? [new("payload", "Payload", "json"), new("credential.token", "Credential", Sensitive: true)]
            : [new("payload", "Payload", "json")];
        var (fixture, management, validating) = RawConfigurationFixture(original, fields);

        var view = await management.ReadAsync("trigger-a", default);
        Assert.Equal(original, view.Configuration.GetRawText());
        Assert.Empty(view.ConfigurationRedactions!);
        Assert.Equal(original, JsonSerializer.SerializeToElement(view, StandaloneTriggersApi.Json).GetProperty("configuration").GetRawText());
        var list = await management.ListAsync(new(), default);
        Assert.Equal(original, Assert.Single(list.Items).Configuration.GetRawText());

        var applied = await SaveRawConfigurationAsync(fixture, management, proposed);

        Assert.Equal(1, fixture.Store.Writes);
        Assert.NotEmpty(validating.Configurations);
        Assert.All(validating.Configurations, configuration => Assert.Equal(proposed, configuration.GetRawText()));
        Assert.Equal(proposed, fixture.Store.Resource.Trigger.Configuration.GetRawText());
        Assert.Equal(proposed, applied.Trigger!.Configuration.GetRawText());
        Assert.Equal(proposed, (await management.ReadAsync("trigger-a", default)).Configuration.GetRawText());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Protected_edits_preserve_raw_public_siblings_and_restored_object_array_and_null_values(bool omitParent)
    {
        const string secretObject = """{ "Token":"private-nested", "N":2.00, "Escape":"\u0062" }""";
        const string secretArray = """[ { "Token":"private-array", "N":3e+00 }, null ]""";
        const string publicAuth = """{ "Label":"\u0063", "N":4.0, "Null":null }""";
        var original = "{ \"eventName\":\"Before\", \"token\":\"private-top\", \"auth.key\":null, \"auth\":{\"key\":" + secretObject +
                       ",\"public\":" + publicAuth + "}, \"credentials\":" + secretArray + ", \"payload\":" + RawTriggerPayload + " }";
        var fields = new TriggerConfigurationField[]
        {
            new("payload", "Payload", "json"), new("token", "Token", Sensitive: true),
            new("auth.key", "Nested token", Sensitive: true), new("credentials", "Credentials", "json", Sensitive: true)
        };
        var (fixture, management, validating) = RawConfigurationFixture(original, fields);
        var view = await management.ReadAsync("trigger-a", default);
        Assert.Equal(4, view.ConfigurationRedactions!.Count);
        AssertProtectedConfigurationHidden(view.Configuration);
        Assert.Equal(RawTriggerPayload, view.Configuration.GetProperty("payload").GetRawText());
        Assert.Equal(publicAuth, view.Configuration.GetProperty("auth").GetProperty("public").GetRawText());
        Assert.DoesNotContain("private-", JsonSerializer.Serialize(view, StandaloneTriggersApi.Json), StringComparison.Ordinal);

        var proposed = "{ \"eventName\":\"After\", \"payload\":" + RawTriggerPayload +
                       (omitParent ? "" : ", \"auth\":{ \"public\":" + publicAuth + " }") + " }";
        var applied = await SaveRawConfigurationAsync(fixture, management, proposed);
        var stored = fixture.Store.Resource.Trigger.Configuration;
        Assert.Equal(1, fixture.Store.Writes);
        Assert.NotEmpty(validating.Configurations);
        foreach (var configuration in validating.Configurations.Append(stored))
        {
            Assert.Equal(RawTriggerPayload, configuration.GetProperty("payload").GetRawText());
            Assert.Equal(secretObject, configuration.GetProperty("auth").GetProperty("key").GetRawText());
            Assert.Equal(secretArray, configuration.GetProperty("credentials").GetRawText());
            Assert.Equal(JsonValueKind.Null, configuration.GetProperty("auth.key").ValueKind);
            Assert.Equal("private-top", configuration.GetProperty("token").GetString());
            if (!omitParent) Assert.Equal(publicAuth, configuration.GetProperty("auth").GetProperty("public").GetRawText());
        }
        AssertProtectedConfigurationHidden(applied.Trigger!.Configuration);
        Assert.Equal(RawTriggerPayload, applied.Trigger.Configuration.GetProperty("payload").GetRawText());
        Assert.DoesNotContain("private-", JsonSerializer.Serialize(applied, StandaloneTriggersApi.Json), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Explicit_protected_replacements_including_null_are_retained_without_rewriting_the_proposed_configuration()
    {
        const string original = """{"eventName":"Before","token":"old-private","auth":{"key":"old-nested"},"auth.key":"old-literal"}""";
        var proposed = "{ \"eventName\":\"After\", \"token\":null, \"auth\":{ \"key\":\"new-private\" }, \"auth.key\":null, \"payload\":" + RawTriggerPayload + " }";
        var (fixture, management, validating) = RawConfigurationFixture(original,
            [new("payload", "Payload", "json"), new("token", "Token", Sensitive: true), new("auth.key", "Nested token", Sensitive: true)]);

        var applied = await SaveRawConfigurationAsync(fixture, management, proposed);

        Assert.Equal(proposed, fixture.Store.Resource.Trigger.Configuration.GetRawText());
        Assert.All(validating.Configurations, configuration => Assert.Equal(proposed, configuration.GetRawText()));
        Assert.Equal(JsonValueKind.Null, fixture.Store.Resource.Trigger.Configuration.GetProperty("token").ValueKind);
        Assert.Equal("new-private", fixture.Store.Resource.Trigger.Configuration.GetProperty("auth").GetProperty("key").GetString());
        Assert.False(applied.Trigger!.Configuration.TryGetProperty("token", out _));
        Assert.False(applied.Trigger.Configuration.TryGetProperty("auth.key", out _));
        Assert.False(applied.Trigger.Configuration.GetProperty("auth").TryGetProperty("key", out _));
        Assert.DoesNotContain("private", JsonSerializer.Serialize(applied, StandaloneTriggersApi.Json), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Configuration_validation_receives_raw_json_and_can_still_refuse_the_save_before_a_store_write()
    {
        const string original = """{ "eventName":"Before" }""";
        var proposed = "{ \"eventName\":\"After\", \"payload\":" + RawTriggerPayload + " }";
        var (fixture, management, validating) = RawConfigurationFixture(original, [new("payload", "Payload", "json")]);
        validating.Reject = true;

        var command = Element("{\"kind\":\"SaveConfiguration\",\"name\":\"Configured trigger\",\"configuration\":" + proposed + "}");
        var preview = await management.PreviewAsync("trigger-a", JsonSerializer.SerializeToElement(new
        {
            expectedRevision = fixture.Store.Resource.Trigger.Revision, command
        }, StandaloneTriggersApi.Json), default);
        Assert.False(preview.CanApply);
        var refusal = await Assert.ThrowsAsync<TriggerOperationException>(() => management.ApplyAsync("trigger-a",
            JsonSerializer.SerializeToElement(new
            {
                requestId = "refused-raw-configuration", expectedRevision = fixture.Store.Resource.Trigger.Revision, previewId = preview.PreviewId, command
            }, StandaloneTriggersApi.Json), default).AsTask());

        Assert.Equal("validation", refusal.Code);
        Assert.Equal(2, validating.Configurations.Count);
        Assert.All(validating.Configurations, configuration => Assert.Equal(proposed, configuration.GetRawText()));
        Assert.Equal(0, fixture.Store.Writes);
        Assert.Equal(original, fixture.Store.Resource.Trigger.Configuration.GetRawText());
    }

    private static void AssertProtectedConfigurationHidden(JsonElement configuration)
    {
        Assert.False(configuration.TryGetProperty("token", out _));
        Assert.False(configuration.TryGetProperty("auth.key", out _));
        Assert.False(configuration.TryGetProperty("credentials", out _));
        Assert.False(configuration.GetProperty("auth").TryGetProperty("key", out _));
    }

    private static (ConfigurationFixture Fixture, TriggerManagement Management, RawConfigurationPublisher Validating) RawConfigurationFixture(
        string configuration, IReadOnlyList<TriggerConfigurationField> fields)
    {
        var fixture = new ConfigurationFixture("event", Element(configuration), fields: fields);
        var validating = new RawConfigurationPublisher();
        var management = new TriggerManagement(fixture.Store, new ConfigurationCommitter(fixture.Store), fixture.Access,
            new ConfigurationCatalog("event", fields), new FakeDiscovery(), new Mapper(), validating,
            new EphemeralDataProtectionProvider(), fixture.Clock);
        return (fixture, management, validating);
    }

    private static async Task<TriggerChangeResult> SaveRawConfigurationAsync(ConfigurationFixture fixture, TriggerManagement management, string configuration)
    {
        var command = Element("{\"kind\":\"SaveConfiguration\",\"name\":\"Configured trigger\",\"configuration\":" + configuration + "}");
        var preview = await management.PreviewAsync("trigger-a", JsonSerializer.SerializeToElement(new
        {
            expectedRevision = fixture.Store.Resource.Trigger.Revision, command
        }, StandaloneTriggersApi.Json), default);
        Assert.True(preview.CanApply);
        return await management.ApplyAsync("trigger-a", JsonSerializer.SerializeToElement(new
        {
            requestId = "save-raw-configuration", expectedRevision = fixture.Store.Resource.Trigger.Revision, previewId = preview.PreviewId, command
        }, StandaloneTriggersApi.Json), default);
    }

    private sealed class RawConfigurationPublisher : IInlineEventPublisher
    {
        internal readonly List<JsonElement> Configurations = [];
        internal bool Reject;
        public Task Publish(IEvent value, CancellationToken cancellationToken = default)
        {
            if (value is StandaloneTriggerConfigurationValidating validating)
            {
                Configurations.Add(validating.Configuration.Clone());
                if (Reject) validating.Findings.Add(new("RejectedRawConfiguration", "Synthetic validation refusal.", "payload"));
            }
            return Task.CompletedTask;
        }
    }
}
