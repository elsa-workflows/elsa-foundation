using System.Text.Json;
using Elsa.Workflows.Runtime.Api.StandaloneTriggers;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Services.Triggers;
using Microsoft.AspNetCore.DataProtection;
using Xunit;

namespace Elsa.Workflows.Runtime.Api.Tests;

public sealed partial class StandaloneTriggerManagementTests
{
    [Fact]
    public async Task HTTP_policy_edit_keeps_exact_grants_and_scope_without_grant_or_target_execution_permission()
    {
        var fixture = new ConfigurationFixture("http", Element("""{"method":"POST","path":"orders","sourceAccess":"Anonymous","http":{"mode":"AsyncFanOut","admissionPolicy":"RequireAll"}}"""));
        var before = fixture.Store.Resource;
        var command = ConfigurationCommand(Element("""{"method":"POST","path":"orders","sourceAccess":"Anonymous","http":{"mode":"AsyncFanOut","admissionPolicy":"AcceptAvailable"}}"""));

        var preview = await fixture.Preview(command);
        Assert.True(preview.CanApply);
        Assert.Contains(preview.Impact, item => item.Label == "HTTP admission policy" && item.Before == "RequireAll" && item.After == "AcceptAvailable");
        Assert.Contains(preview.Impact, item => item.Label == "Invocation grants" && item.Description!.Contains("Retained: 2; granted or replaced: 0; revoked: 0; cleared: 0", StringComparison.Ordinal));
        await fixture.Apply(command, preview);

        AssertConfigurationScopeUnchanged(before, fixture.Store.Resource);
        Assert.Equal("AcceptAvailable", fixture.Store.Resource.Trigger.Configuration.GetProperty("http").GetProperty("admissionPolicy").GetString());
        Assert.Equal(1, fixture.Store.Writes);
    }

    [Fact]
    public async Task Cron_expression_and_timezone_edit_preserves_grants_and_previews_no_invented_due_time()
    {
        var schedules = new InMemoryRecurringTriggerScheduleStore();
        var fixture = new ConfigurationFixture("cron", Element("""{"expression":"0 0 9 * * *","timezone":"UTC"}"""), schedules: schedules);
        var cursor = ScheduleImpactCursor(fixture);
        await schedules.SaveAsync(cursor);
        var before = fixture.Store.Resource;
        var command = ConfigurationCommand(Element("""{"expression":"0 0 10 * * *","timezone":"America/New_York"}"""));

        var preview = await fixture.Preview(command);
        Assert.True(preview.CanApply);
        Assert.Contains(preview.Impact, item => item.Label == "Cron expression" && item.Before == "0 0 9 * * *" && item.After == "0 0 10 * * *");
        Assert.Contains(preview.Impact, item => item.Label == "Timezone" && item.Before == "UTC" && item.After == "America/New_York");
        var next = Assert.Single(preview.Impact.Where(item => item.Label == "Next firing"));
        Assert.Equal(ScheduleImpactText(cursor), next.Before); Assert.Null(next.After);
        Assert.Contains("Advisory next firing unavailable: the source schedule planner is not installed.", next.Description!, StringComparison.Ordinal);
        await fixture.Apply(command, preview);

        AssertConfigurationScopeUnchanged(before, fixture.Store.Resource);
        Assert.Equal("America/New_York", fixture.Store.Resource.Trigger.Configuration.GetProperty("timezone").GetString());
    }

    [Fact]
    public async Task HTTP_mode_endpoint_and_source_access_changes_have_independent_preview_rows()
    {
        var fixture = new ConfigurationFixture("http", Element("""{"method":"POST","path":"old-orders","sourceAccess":"Anonymous","http":{"mode":"WorkflowResponse"}}"""), count: 1);
        var command = ConfigurationCommand(Element("""{"method":"PUT","path":"new-orders","sourceAccess":"Authenticated","http":{"mode":"AsyncFanOut","admissionPolicy":"RequireAll"}}"""));

        var preview = await fixture.Preview(command);

        Assert.True(preview.CanApply);
        Assert.Contains(preview.Impact, item => item.Label == "Method" && item.Before == "POST" && item.After == "PUT");
        Assert.Contains(preview.Impact, item => item.Label == "Route" && item.Before == "old-orders" && item.After == "new-orders");
        Assert.Contains(preview.Impact, item => item.Label == "Source access" && item.Before == "Anonymous" && item.After == "Authenticated");
        Assert.Contains(preview.Impact, item => item.Label == "HTTP response mode" && item.Before == "WorkflowResponse" && item.After == "AsyncFanOut");
        Assert.Equal(0, fixture.Store.Writes);
    }

    [Fact]
    public async Task Configuration_impact_redacts_descriptor_sensitive_and_undeclared_or_structured_values()
    {
        const string oldSecret = "old-private-token", newSecret = "new-private-token", nestedSecret = "nested-private-value", unknownSecret = "undeclared-private-value";
        var fixture = new ConfigurationFixture("event", JsonSerializer.SerializeToElement(new
        { setting = "before", token = oldSecret, connection = new { value = "old-structured" }, undeclared = "old-undeclared" }),
            fields: [new("setting", "Public setting"), new("token", "Credential", Sensitive: true), new("connection", "Connection")]);
        var command = ConfigurationCommand(JsonSerializer.SerializeToElement(new
        { setting = "after", token = newSecret, connection = new { value = nestedSecret }, undeclared = unknownSecret }));

        var preview = await fixture.Preview(command);
        var wire = JsonSerializer.Serialize(preview);

        Assert.True(preview.CanApply);
        Assert.Contains(preview.Impact, item => item.Label == "Public setting" && item.Before == "before" && item.After == "after");
        Assert.Contains(preview.Impact, item => item.Label == "Credential" && item.Before == "[Protected]" && item.After == "[Protected]");
        Assert.Contains(preview.Impact, item => item.Label == "Connection" && item.Before == "[Structured value]" && item.After == "[Structured value]");
        Assert.Contains(preview.Impact, item => item.Label == "Other configuration");
        foreach (var secret in new[] { oldSecret, newSecret, nestedSecret, unknownSecret }) Assert.DoesNotContain(secret, wire, StringComparison.Ordinal);
        Assert.Equal(0, fixture.Store.Writes);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Target_or_mapping_scope_edits_still_clear_grants_unless_explicitly_authorized(bool changeTarget)
    {
        var fixture = new ConfigurationFixture("event", Element("""{"eventName":"Order"}"""));
        fixture.Access.TargetExecute = true;
        var row = fixture.Store.Resource.Subscriptions[0];
        var command = JsonSerializer.SerializeToElement(new
        {
            kind = "SaveSubscription", subscriptionId = row.Id, expectedSubscriptionRevision = row.Revision, enabled = false,
            target = new { definitionId = changeTarget ? "replacement-definition" : row.DefinitionId, slotName = row.SlotName },
            mappings = changeTarget ? Array.Empty<object>() : [new { inputName = "message", binding = new { kind = "Literal", value = "changed input" } }],
            invocationRequest = "Keep"
        });

        var preview = await fixture.Preview(command);
        Assert.True(preview.CanApply);
        Assert.Contains(preview.Impact, item => item.Label == "Invocation grants" && item.Description!.Contains("cleared: 1", StringComparison.Ordinal));
        await fixture.Apply(command, preview);

        Assert.Null(fixture.Store.Resource.Subscriptions.Single(item => item.Id == row.Id).Grant);
        Assert.Same(fixture.OriginalSubscriptions[1].Grant, fixture.Store.Resource.Subscriptions.Single(item => item.Id == fixture.OriginalSubscriptions[1].Id).Grant);
        var grantCommand = JsonSerializer.SerializeToElement(new { kind = "GrantInvocation", subscriptionId = row.Id,
            expectedSubscriptionRevision = row.Revision + 1, expectedGrantRevision = (long?)null });
        var error = await Assert.ThrowsAsync<TriggerOperationException>(() => fixture.Preview(grantCommand));
        Assert.Equal("forbidden", error.Code);
        Assert.Equal(1, fixture.Store.Writes);
    }

    [Fact]
    public async Task Explicit_revocation_remains_revoked_after_a_later_configuration_edit()
    {
        var fixture = new ConfigurationFixture("cron", Element("""{"expression":"0 0 9 * * *","timezone":"UTC"}"""));
        fixture.Access.CanGrant = true;
        var row = fixture.Store.Resource.Subscriptions[0];
        var revoke = JsonSerializer.SerializeToElement(new { kind = "RevokeInvocation", subscriptionId = row.Id,
            expectedSubscriptionRevision = row.Revision, expectedGrantRevision = row.Grant!.Revision });
        var preview = await fixture.Preview(revoke);
        Assert.Contains(preview.Impact, item => item.Label == "Invocation grants" && item.Description!.Contains("revoked: 1", StringComparison.Ordinal));
        await fixture.Apply(revoke, preview);
        var revoked = fixture.Store.Resource.Subscriptions.Single(item => item.Id == row.Id).Grant;
        Assert.Equal(TriggerGrantStatus.Revoked, revoked!.Status);
        fixture.Access.CanGrant = false;
        var edit = ConfigurationCommand(Element("""{"expression":"0 0 10 * * *","timezone":"UTC"}"""));

        await fixture.Apply(edit, await fixture.Preview(edit));

        Assert.Same(revoked, fixture.Store.Resource.Subscriptions.Single(item => item.Id == row.Id).Grant);
        Assert.Equal(2, fixture.Store.Writes);
    }

    private static void AssertConfigurationScopeUnchanged(TriggerAggregate before, TriggerAggregate after)
    {
        Assert.Equal(before.Trigger.TypeKey, after.Trigger.TypeKey);
        Assert.Equal(before.Trigger.SchemaVersion, after.Trigger.SchemaVersion);
        Assert.Equal(before.Trigger.ConfigurationRevision + 1, after.Trigger.ConfigurationRevision);
        Assert.Equal(before.Trigger.AssociationRevision, after.Trigger.AssociationRevision);
        Assert.Equal(before.Subscriptions.Count, after.Subscriptions.Count);
        foreach (var row in before.Subscriptions) Assert.Same(row, after.Subscriptions.Single(item => item.Id == row.Id));
    }

    private static JsonElement ConfigurationCommand(JsonElement configuration)
    {
        var command = new Dictionary<string, object?>
        {
            ["kind"] = "SaveConfiguration", ["name"] = "Configured trigger",
            ["configuration"] = configuration.EnumerateObject().Where(item => item.Name != "http")
                .ToDictionary(item => item.Name, item => item.Value.Clone(), StringComparer.Ordinal)
        };
        if (configuration.TryGetProperty("http", out var http)) command["http"] = http;
        return JsonSerializer.SerializeToElement(command);
    }

    private sealed class ConfigurationFixture
    {
        internal FakeAccess Access { get; } = new() { CanGrant = false, TargetExecute = false };
        internal FakeStore Store { get; } = new();
        internal TriggerManagement Management { get; }
        internal Clock Clock { get; } = new();
        internal IReadOnlyList<TriggerSubscription> OriginalSubscriptions { get; }
        internal ConfigurationFixture(string type, JsonElement configuration, int count = 2, IReadOnlyList<TriggerConfigurationField>? fields = null,
            IRecurringTriggerScheduleStore? schedules = null, IEnumerable<IStandaloneSchedulePlanner>? planners = null)
        {
            var clock = Clock;
            OriginalSubscriptions = Enumerable.Range(0, count).Select(index => new TriggerSubscription("subscription-" + index, "trigger-a",
                "definition-" + index, "default", "native", 1, 1, true, [], new(TriggerManagementKind.Studio),
                new("grant-" + index, "trigger-a", "subscription-" + index, "definition-" + index, "default", "native", type,
                    1, TriggerGrantStatus.Granted, "original-authorizer", clock.Now))).ToArray();
            Store.Resource = new(new("trigger-a", "Configured trigger", type, 1, configuration, 1, 1, 1,
                TriggerDesiredState.Enabled, new(TriggerManagementKind.Studio), clock.Now, clock.Now, "creator", hasActivated: true,
                tenantId: Access.Scope.TenantId), OriginalSubscriptions);
            Management = new(Store, new ConfigurationCommitter(Store), Access, new ConfigurationCatalog(type, fields), new FakeDiscovery(),
                new Mapper(), new Publisher(), new EphemeralDataProtectionProvider(), clock, schedules: schedules, schedulePlanners: planners);
        }
        internal Task<TriggerPreview> Preview(JsonElement command) => Management.PreviewAsync("trigger-a",
            JsonSerializer.SerializeToElement(new { expectedRevision = Store.Resource.Trigger.Revision, command }), default).AsTask();
        internal ValueTask<TriggerChangeResult> Apply(JsonElement command, TriggerPreview preview) => Management.ApplyAsync("trigger-a",
            JsonSerializer.SerializeToElement(new { requestId = Guid.NewGuid().ToString("N"), expectedRevision = Store.Resource.Trigger.Revision,
                previewId = preview.PreviewId, command }), default);
    }

    private sealed class ConfigurationCommitter(FakeStore store) : ITriggerManagementCommitter
    {
        public bool SupportsActivatedResources => true;
        public bool CanActivateType(string typeKey, int schemaVersion) => true;
        public bool CanStopType(string typeKey, int schemaVersion) => true;
        public ValueTask<TriggerMutationReceipt> CommitAsync(TriggerScope scope, TriggerStoreWrite write, CancellationToken cancellationToken = default) =>
            store.WriteAsync(scope, write, cancellationToken);
    }

    private sealed class ConfigurationCatalog(string type, IReadOnlyList<TriggerConfigurationField>? fields) : IStandaloneTriggerTypeCatalog
    {
        public TriggerTypeDescriptor? Find(string key) => new(type, 1, type, type == "cron" ? "Cron" : type == "http" ? "Http" : "Event",
            fields ?? (type == "http" ? [new("method", "Method"), new("path", "Route"), new("sourceAccess", "Source access")]
                : type == "cron" ? [new("expression", "Cron expression"), new("timezone", "Timezone")] : []), []);
        public IReadOnlyList<TriggerTypeDescriptor> List() => [Find(type)!];
    }
}
