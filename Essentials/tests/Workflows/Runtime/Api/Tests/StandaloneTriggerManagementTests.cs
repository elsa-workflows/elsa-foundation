using System.Text.Json;
using Elsa.Events.Core.Contracts;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Runtime.Api.StandaloneTriggers;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Services.Triggers;
using Microsoft.AspNetCore.DataProtection;
using Xunit;

namespace Elsa.Workflows.Runtime.Api.Tests;

public sealed partial class StandaloneTriggerManagementTests
{
    [Fact]
    public async Task Target_changed_during_prepare_refuses_the_reviewed_change_before_calling_the_committer()
    {
        var fixture = new Fixture();
        var command = Element("""{"kind":"AddSubscription","target":{"definitionId":"definition-a","slotName":"default"},"mappings":[],"enabled":false,"invocationRequest":"None"}""");
        var preview = await fixture.Management.PreviewAsync("trigger-a", JsonSerializer.SerializeToElement(new { expectedRevision = 1, command }), default);
        var reads = 0;
        fixture.Discovery.BeforeDescribe = () => { if (++reads == 3) fixture.Discovery.ContractRevision = "replaced-after-prepare"; };

        var refusal = await Assert.ThrowsAsync<TriggerOperationException>(() => fixture.Management.ApplyAsync("trigger-a",
            JsonSerializer.SerializeToElement(new { requestId = "changed-during-prepare", previewId = preview.PreviewId, expectedRevision = 1, command }), default).AsTask());

        Assert.Equal("conflict", refusal.Code);
        Assert.Equal(0, fixture.Store.Writes);
        Assert.Empty(fixture.Store.Resource.Subscriptions);
    }

    [Theory]
    [InlineData("token")]
    [InlineData("command")]
    [InlineData("subject")]
    [InlineData("expiry")]
    [InlineData("revision")]
    [InlineData("target")]
    public async Task Impact_review_rejects_altered_stale_or_foreign_proof_without_writing(string change)
    {
        var fixture = new Fixture();
        var command = Element("""{"kind":"AddSubscription","target":{"definitionId":"definition-a","slotName":"default"},"mappings":[],"enabled":false,"invocationRequest":"None"}""");
        var preview = await fixture.Management.PreviewAsync("trigger-a", JsonSerializer.SerializeToElement(new { expectedRevision = 1, command }), default);
        Assert.True(preview.CanApply);
        var token = preview.PreviewId;
        if (change == "token") token += "corrupt";
        if (change == "command") command = Element("""{"kind":"PauseTrigger"}""");
        if (change == "subject") fixture.Access.Subject = "another-user";
        if (change == "expiry") fixture.Clock.Now = fixture.Clock.Now.AddMinutes(6);
        if (change == "revision") fixture.Store.Resource = fixture.Store.Resource with { Trigger = Trigger(2) };
        if (change == "target") fixture.Discovery.ContractRevision = "replaced";
        var error = await Assert.ThrowsAsync<TriggerOperationException>(() => fixture.Management.ApplyAsync("trigger-a",
            JsonSerializer.SerializeToElement(new { requestId = "apply-1", previewId = token, expectedRevision = 1, command }), default).AsTask());
        Assert.Equal("conflict", error.Code);
        Assert.Equal(0, fixture.Store.Writes);
        Assert.Empty(fixture.Store.Resource.Subscriptions);
    }

    [Fact]
    public async Task Apply_retry_reconciles_original_commit_before_expired_preview_and_rejects_changed_intent()
    {
        var fixture = new Fixture(); var command = Element("""{"kind":"PauseTrigger"}""");
        var preview = await fixture.Management.PreviewAsync("trigger-a", JsonSerializer.SerializeToElement(new { expectedRevision = 1, command }), default);
        var request = JsonSerializer.SerializeToElement(new { requestId = "apply-1", previewId = preview.PreviewId, expectedRevision = 1, command });
        var first = await fixture.Management.ApplyAsync("trigger-a", request, default);
        fixture.Clock.Now = fixture.Clock.Now.AddHours(1);
        var replay = await fixture.Management.ApplyAsync("trigger-a", request, default);
        Assert.Equal("Applied", first.Disposition);
        Assert.Equal("AlreadyApplied", replay.Disposition);
        Assert.Equal(first.Revision, replay.Revision);
        Assert.Equal(1, fixture.Store.Writes);
        var changed = JsonSerializer.SerializeToElement(new { requestId = "apply-1", previewId = preview.PreviewId, expectedRevision = 1,
            command = new { kind = "DeleteTrigger" } });
        var error = await Assert.ThrowsAsync<TriggerOperationException>(() => fixture.Management.ApplyAsync("trigger-a", changed, default).AsTask());
        Assert.Equal("conflict", error.Code);
        Assert.Equal(1, fixture.Store.Writes);
    }

    [Fact]
    public async Task Authoring_committer_saves_paused_resources_but_refuses_activation_before_effects()
    {
        var fixture = new Fixture();
        var paused = new TriggerStoreWrite("paused", "paused-content", "operator", 1, Trigger(2), []);
        await fixture.Committer.CommitAsync(fixture.Access.Scope, paused);
        Assert.Equal(1, fixture.Store.Writes);
        var grant = new TriggerInvocationGrant("grant", "trigger-a", "subscription-a", "definition-a", "default", "native", "event", 1,
            TriggerGrantStatus.Granted, "operator", fixture.Clock.Now);
        var subscription = new TriggerSubscription("subscription-a", "trigger-a", "definition-a", "default", "native", 1, 1, true, [], new(TriggerManagementKind.Studio), grant);
        var activated = new TriggerStoreWrite("enable", "enable-content", "operator", 2, Trigger(3, TriggerDesiredState.Enabled), [subscription]);
        var error = await Assert.ThrowsAsync<TriggerOperationException>(() => fixture.Committer.CommitAsync(fixture.Access.Scope, activated).AsTask());
        Assert.Equal("unavailable", error.Code);
        Assert.Equal(1, fixture.Store.Writes);
        Assert.Equal(TriggerDesiredState.Paused, fixture.Store.Resource.Trigger.DesiredState);
    }

    [Fact]
    public async Task Trigger_owned_associations_redact_hidden_targets_and_workflow_direction_requires_target_read()
    {
        var fixture = new Fixture();
        var subscription = new TriggerSubscription("subscription-a", "trigger-a", "definition-a", "default", "native", 1, 1, false, [], new(TriggerManagementKind.Studio));
        fixture.Store.Resource = fixture.Store.Resource with { Subscriptions = [subscription] };
        fixture.Access.TargetRead = false;
        var fromTrigger = await fixture.Management.SubscriptionsAsync(new(TriggerId: "trigger-a"), default);
        var restricted = Assert.Single(fromTrigger.Items);
        Assert.Empty(restricted.Target.DefinitionId);
        Assert.Contains(restricted.Findings, finding => finding.Code == "TargetDetailsRestricted");
        Assert.Empty(restricted.Mappings);
        var fromWorkflow = await Assert.ThrowsAsync<TriggerOperationException>(() => fixture.Management.SubscriptionsAsync(new(DefinitionId: "definition-a"), default).AsTask());
        var direct = await fixture.Management.SubscriptionAsync("subscription-a", default);
        Assert.Equal("missing", fromWorkflow.Code);
        Assert.Empty(direct.Target.DefinitionId);
        Assert.Equal(0, fixture.Store.Writes);
    }

    [Fact]
    public async Task Resource_operator_can_pause_and_authority_manager_can_revoke_after_losing_target_read_or_execute()
    {
        var fixture = new Fixture();
        fixture.Access.TargetRead = false; fixture.Access.TargetExecute = false;
        var grant = new TriggerInvocationGrant("grant", "trigger-a", "subscription-a", "private-definition", "private-slot", "native", "event", 1,
            TriggerGrantStatus.Granted, "grant-owner", fixture.Clock.Now);
        fixture.Store.Resource = fixture.Store.Resource with { Subscriptions = [new("subscription-a", "trigger-a", "private-definition", "private-slot", "native",
            1, 1, false, [], new(TriggerManagementKind.Studio), grant)] };
        var command = Element("""{"kind":"PauseTrigger"}""");
        var preview = await fixture.Management.PreviewAsync("trigger-a", JsonSerializer.SerializeToElement(new { expectedRevision = 1, command }), default);
        Assert.True(preview.CanApply);
        Assert.DoesNotContain("private-definition", JsonSerializer.Serialize(preview));
        await fixture.Management.ApplyAsync("trigger-a", JsonSerializer.SerializeToElement(new { requestId = "pause-restricted", expectedRevision = 1,
            command, previewId = preview.PreviewId }), default);
        command = Element("""{"kind":"RevokeInvocation","subscriptionId":"subscription-a","expectedSubscriptionRevision":1,"expectedGrantRevision":1}""");
        preview = await fixture.Management.PreviewAsync("trigger-a", JsonSerializer.SerializeToElement(new { expectedRevision = 2, command }), default);
        Assert.True(preview.CanApply);
        await fixture.Management.ApplyAsync("trigger-a", JsonSerializer.SerializeToElement(new { requestId = "revoke-restricted", expectedRevision = 2,
            command, previewId = preview.PreviewId }), default);
        Assert.Equal(TriggerGrantStatus.Revoked, Assert.Single(fixture.Store.Resource.Subscriptions).Grant!.Status);
        Assert.Equal(2, fixture.Store.Writes);
    }

    [Fact]
    public async Task Installed_types_use_the_same_page_wire_contract_as_the_Studio_client()
    {
        var result = await new Fixture().Management.TypesAsync(default);
        Assert.Single(result.Items);
        Assert.Null(result.Next);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Mapping_context_reports_installed_portable_languages_and_actual_evaluator_availability(bool available)
    {
        var portable = new ContextOnlyExpressionHandler();
        var fixture = new Fixture(expressionHandlers: [portable], expressionEvaluator: available ? portable : null);

        var result = JsonSerializer.SerializeToElement(await fixture.Management.MappingContextAsync("trigger-a",
            Element("""{"target":{"definitionId":"definition-a","slotName":"default"}}"""), default));

        var language = Assert.Single(result.GetProperty("supportedExpressions").EnumerateArray());
        Assert.Equal("javascript", language.GetProperty("language").GetString());
        Assert.Equal(available, language.GetProperty("available").GetBoolean());
        Assert.Equal(0, fixture.Store.Writes);
    }

    private sealed class ContextOnlyExpressionHandler : IPortableExpressionHandler, IPortableExpressionEvaluator
    {
        public string Language => "javascript";
        public ValueTask<JsonElement> EvaluateAsync(ExpressionEvaluationRequest request) =>
            throw new InvalidOperationException("Reading mapping metadata must not evaluate an expression.");
    }

    [Fact]
    public async Task Grant_request_requires_both_resource_authorization_and_current_target_invocation_permission()
    {
        var fixture = new Fixture(); fixture.Access.CanGrant = false;
        var command = Element("""{"kind":"AddSubscription","target":{"definitionId":"definition-a","slotName":"default"},"mappings":[],"enabled":false,"invocationRequest":"Grant"}""");
        var request = JsonSerializer.SerializeToElement(new { expectedRevision = 1, command });
        var error = await Assert.ThrowsAsync<TriggerOperationException>(() => fixture.Management.PreviewAsync("trigger-a", request, default).AsTask());
        Assert.Equal("forbidden", error.Code);
        fixture.Access.CanGrant = true; fixture.Access.TargetExecute = false;
        error = await Assert.ThrowsAsync<TriggerOperationException>(() => fixture.Management.PreviewAsync("trigger-a", request, default).AsTask());
        Assert.Equal("forbidden", error.Code);
        Assert.Equal(0, fixture.Store.Writes);
    }

    [Fact]
    public void Expression_wire_roundtrip_keeps_template_shape_explicit_null_and_exact_large_integer()
    {
        var wire = Element("""[{"inputName":"Amount","binding":{"kind":"Expression","template":{"language":"javascript","source":"amount","resultType":{"alias":"UInt64","collectionKind":"Single"},"options":{},"capabilityProfile":"binding-pure-v1"},"parameters":{"amount":{"kind":"Literal","value":18446744073709551615},"optional":{"kind":"Literal","value":null},"name":{"kind":"SourceField","path":["payload","name"]}}}}]""");
        var mapping = Assert.Single(TriggerManagement.ReadMappings(wire));
        var expression = Assert.IsType<TriggerExpressionBinding>(mapping.Binding);
        Assert.Equal("18446744073709551615", Assert.IsType<TriggerLiteralBinding>(expression.Parameters["amount"]).Value.GetRawText());
        Assert.Equal(JsonValueKind.Null, Assert.IsType<TriggerLiteralBinding>(expression.Parameters["optional"]).Value.ValueKind);
        var encoded = TriggerManagement.MappingJson(mapping);
        Assert.True(encoded.GetProperty("binding").TryGetProperty("template", out var template));
        Assert.Equal("javascript", template.GetProperty("language").GetString());
        Assert.Equal("18446744073709551615", encoded.GetProperty("binding").GetProperty("parameters").GetProperty("amount").GetProperty("value").GetRawText());
        var roundtrip = TriggerManagement.ReadMappings(JsonSerializer.SerializeToElement(new[] { encoded }));
        Assert.Equal(encoded.GetRawText(), TriggerManagement.MappingJson(Assert.Single(roundtrip)).GetRawText());
    }

    [Fact]
    public async Task Authoring_metadata_and_physical_store_alone_do_not_advertise_enable_but_retained_projection_allows_stop_only_review()
    {
        var fixture = new Fixture(new RuntimeCapability(false, true));
        fixture.Store.Resource = new(Trigger(1, TriggerDesiredState.Enabled), []);
        var view = await fixture.Management.ReadAsync("trigger-a", default);
        Assert.Equal("EnabledUnavailable", view.EffectiveState);
        Assert.Contains(view.Findings, finding => finding.Code == "RuntimeSourceUnavailable");
        Assert.False(view.Actions.Single(action => action.Action == "EnableTrigger").Allowed);
        Assert.True(view.Actions.Single(action => action.Action == "PauseTrigger").Allowed);
        var command = Element("""{"kind":"PauseTrigger"}""");
        Assert.True((await fixture.Management.PreviewAsync("trigger-a", JsonSerializer.SerializeToElement(new { expectedRevision = 1, command }), default)).CanApply);
        command = Element("""{"kind":"EnableTrigger"}""");
        Assert.False((await fixture.Management.PreviewAsync("trigger-a", JsonSerializer.SerializeToElement(new { expectedRevision = 1, command }), default)).CanApply);
        var removed = new Fixture(new RuntimeCapability(false, false));
        removed.Store.Resource = new(Trigger(1, TriggerDesiredState.Enabled), []);
        Assert.False((await removed.Management.ReadAsync("trigger-a", default)).Actions.Single(action => action.Action == "PauseTrigger").Allowed);
    }

    [Fact]
    public async Task Schedule_view_reads_actual_generation_due_cursor_and_recorded_failure_backoff()
    {
        var clock = new Clock(); var schedules = new InMemoryRecurringTriggerScheduleStore(clock);
        var fixture = new Fixture(new RuntimeCapability(true, true), schedules);
        fixture.Store.Resource = new(new("trigger-a", "Scheduled", "cron", 1, Element("""{"expression":"* * * * *","timezone":"UTC"}"""),
            7, 1, 0, TriggerDesiredState.Enabled, new(TriggerManagementKind.Studio), clock.Now, clock.Now, "operator", hasActivated: true, tenantId: "tenant-a"), []);
        var due = clock.Now.AddMinutes(-5);
        await schedules.SaveAsync(RecurringTriggerSchedule.ForResource(new("trigger-a", 7, 1, "cron", "Cron", fixture.Access.Scope.Persistence,
            fixture.Access.Scope.TenantId, "UTC"), RecurringScheduleKind.Cron, "* * * * *", due, clock.Now.AddHours(-1)));
        var claim = Assert.Single(await schedules.ClaimDueAsync(new("worker", clock.Now, TimeSpan.FromMinutes(1), 1)));
        Assert.True(await schedules.ReleaseClaimAsync(claim, clock.Now.AddMinutes(2)));
        var view = await fixture.Management.ReadAsync("trigger-a", default);
        Assert.Equal(7, view.Schedule!.Generation); Assert.Equal(due, view.Schedule.NextDueUtc); Assert.Equal("+00:00", view.Schedule.Offset);
        Assert.True(view.Schedule.IsOverdue); Assert.Equal(1, view.Schedule.FailureCount);
        Assert.Equal(clock.Now.AddMinutes(2), view.Schedule.NextRetryAt);
        Assert.Contains(view.Findings, finding => finding.Code == "ScheduleRetryBackoff");
    }

    private sealed class RuntimeCapability(bool installed, bool stop) : ITriggerManagementCommitter
    {
        public bool SupportsActivatedResources => true;
        public bool CanActivateType(string typeKey, int schemaVersion) => installed;
        public bool CanStopType(string typeKey, int schemaVersion) => stop;
        public ValueTask<TriggerMutationReceipt> CommitAsync(TriggerScope scope, TriggerStoreWrite write, CancellationToken ct = default) =>
            ValueTask.FromException<TriggerMutationReceipt>(new NotSupportedException("Readiness fixture does not write resources."));
    }

    [Fact]
    public async Task Activated_history_does_not_claim_an_HTTP_reservation_until_the_actual_owned_generation_is_read()
    {
        var reader = new ReservationReader();
        var fixture = new Fixture(new RuntimeCapability(true, true), reservations: reader, planners: [new ReservationPlanner()]);
        fixture.Store.Resource = new(new("trigger-a", "HTTP", "http", 1, Element("""{"path":"/orders","http":{"mode":"AsyncFanOut","admissionPolicy":"RequireAll"}}"""),
            1, 1, 0, TriggerDesiredState.Enabled, new(TriggerManagementKind.Studio), fixture.Clock.Now, fixture.Clock.Now, "operator", hasActivated: true, tenantId: "tenant-a"), []);
        var missing = await fixture.Management.ReadAsync("trigger-a", default);
        Assert.Equal("Unreserved", missing.Http!.ReservationState);
        Assert.Contains(missing.Findings, finding => finding.Code == "ReservationMissing");
        reader.Row = new("reservation", fixture.Access.Scope, "http", "hash", RuntimeIngressReservation.StandaloneTriggerOwner, "trigger-a", 1, 1,
            new Dictionary<string, string> { ["http:template"] = "orders" });
        var found = await fixture.Management.ReadAsync("trigger-a", default);
        Assert.Equal("Reserved", found.Http!.ReservationState); Assert.Equal("orders", found.Http.ReservedEndpoint);
    }
    [Theory]
    [InlineData("none", true)]
    [InlineData("owned", true)]
    [InlineData("standalone", false)]
    [InlineData("embedded", false)]
    [InlineData("other-tenant", false)]
    public async Task HTTP_enable_review_reads_the_actual_route_owner_without_disclosing_it(string owner, bool allowed)
    {
        var reader = new ReservationReader();
        var fixture = HttpPreviewFixture(reader);
        if (owner != "none") reader.Row = new("reservation", owner == "other-tenant" ? new(fixture.Access.Scope.Persistence, "secret-tenant") : fixture.Access.Scope,
            "http", "hash", owner == "embedded" ? RuntimeIngressReservation.PublicationSlotOwner : RuntimeIngressReservation.StandaloneTriggerOwner,
            owner == "owned" ? "trigger-a" : "secret-owner", 1, 1, new Dictionary<string, string>());
        var command = new { kind = "EnableTrigger" };
        var preview = await fixture.Management.PreviewAsync("trigger-a", JsonSerializer.SerializeToElement(new { expectedRevision = 1, command }), default);
        Assert.Equal(allowed, preview.CanApply);
        Assert.Equal(0, fixture.Store.Writes);
        if (!allowed) Assert.Contains(preview.Findings, finding => finding.Code == "route-conflict");
        Assert.DoesNotContain("secret-owner", JsonSerializer.Serialize(preview), StringComparison.Ordinal);
        Assert.DoesNotContain("secret-tenant", JsonSerializer.Serialize(preview), StringComparison.Ordinal);
    }

    [Fact]
    public async Task HTTP_route_claimed_after_review_refuses_apply_before_resource_effects_and_still_allows_pause()
    {
        var reader = new ReservationReader(); var fixture = HttpPreviewFixture(reader);
        var command = new { kind = "EnableTrigger" };
        var preview = await fixture.Management.PreviewAsync("trigger-a", JsonSerializer.SerializeToElement(new { expectedRevision = 1, command }), default);
        Assert.True(preview.CanApply);
        reader.Row = new("reservation", fixture.Access.Scope, "http", "hash", RuntimeIngressReservation.PublicationSlotOwner,
            "another-slot", 1, 1, new Dictionary<string, string>());
        var error = await Assert.ThrowsAsync<TriggerOperationException>(() => fixture.Management.ApplyAsync("trigger-a",
            JsonSerializer.SerializeToElement(new { requestId = "lost-route", previewId = preview.PreviewId, expectedRevision = 1, command }), default).AsTask());
        Assert.Equal("route-conflict", error.Code); Assert.Equal(0, fixture.Store.Writes);
        var trigger = fixture.Store.Resource.Trigger;
        fixture.Store.Resource = fixture.Store.Resource with { Trigger = new(trigger.Id, trigger.Name, trigger.TypeKey, trigger.SchemaVersion,
            trigger.Configuration, trigger.Revision, trigger.ConfigurationRevision, trigger.AssociationRevision, TriggerDesiredState.Enabled,
            trigger.Origin, trigger.CreatedAt, trigger.UpdatedAt, trigger.CreatedBy, hasActivated: true, tenantId: trigger.TenantId) };
        var pause = await fixture.Management.PreviewAsync("trigger-a", JsonSerializer.SerializeToElement(new { expectedRevision = 1, command = new { kind = "PauseTrigger" } }), default);
        Assert.True(pause.CanApply);
    }

    [Fact]
    public async Task HTTP_enable_review_refuses_uninspectable_reservations_before_any_write()
    {
        var fixture = HttpPreviewFixture(null);
        var preview = await fixture.Management.PreviewAsync("trigger-a", JsonSerializer.SerializeToElement(new { expectedRevision = 1, command = new { kind = "EnableTrigger" } }), default);
        Assert.False(preview.CanApply); Assert.Contains(preview.Findings, finding => finding.Code == "unavailable");
        Assert.Equal(0, fixture.Store.Writes);
    }

    private static Fixture HttpPreviewFixture(ReservationReader? reader)
    {
        var fixture = new Fixture(new RuntimeCapability(true, true), reservations: reader, planners: planners ?? [new ReservationPlanner()], policies: policies, bindings: bindings);
        var grant = new TriggerInvocationGrant("grant", "trigger-a", "subscription-a", "definition-a", "default", "native", "http", 1,
            TriggerGrantStatus.Granted, "operator", fixture.Clock.Now);
        var row = new TriggerSubscription("subscription-a", "trigger-a", "definition-a", "default", "native", 1, 1, true, [], new(TriggerManagementKind.Studio), grant);
        fixture.Store.Resource = new(new("trigger-a", "HTTP", "http", 1, Element("""{"path":"/orders","http":{"mode":"AsyncFanOut","admissionPolicy":"RequireAll"}}"""),
            1, 1, 0, TriggerDesiredState.Paused, new(TriggerManagementKind.Studio), fixture.Clock.Now, fixture.Clock.Now, "operator", tenantId: "tenant-a"), [row]);
        return fixture;
    }

    private sealed class ReservationPlanner : ITriggerReservationPlanner
    {
        public string TypeKey => "http";
        public ValueTask<IReadOnlyList<RuntimeIngressReservationDescriptor>> PlanAsync(TriggerScope scope, TriggerAggregate? current, TriggerStoreWrite proposed, CancellationToken ct = default) =>
            ValueTask.FromResult<IReadOnlyList<RuntimeIngressReservationDescriptor>>([new("http", "hash", new Dictionary<string, string>())]);
    }
    private sealed class ReservationReader : IRuntimeIngressReservationReader
    {
        internal RuntimeIngressReservation? Row; public bool SupportsReservations => true;
        public ValueTask<RuntimeIngressReservationPage> ListInScopeAsync(PersistenceScope scope, RuntimeIngressReservationQuery query, CancellationToken ct = default) =>
            ValueTask.FromResult(new RuntimeIngressReservationPage(Row is null || Row.StimulusType != query.StimulusType ||
                query.StimulusHash is not null && Row.StimulusHash != query.StimulusHash ? [] : [Row], null));
        public ValueTask<RuntimeIngressReservation?> FindAsync(TriggerScope scope, string type, string hash, CancellationToken ct = default) => ValueTask.FromResult(Row);
        public ValueTask<RuntimeIngressReservationPage> ListAsync(TriggerScope scope, RuntimeIngressReservationQuery query, CancellationToken ct = default) => ValueTask.FromResult(new RuntimeIngressReservationPage([], null));
    }

    private static JsonElement Element(string value) { using var document = JsonDocument.Parse(value); return document.RootElement.Clone(); }
    private static StandaloneTrigger Trigger(long revision, TriggerDesiredState state = TriggerDesiredState.Paused) => new("trigger-a", "Orders", "event", 1,
        Element("""{"eventName":"Order"}"""), revision, 1, 0, state, new(TriggerManagementKind.Studio), DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, "operator", tenantId: "tenant-a");

    private sealed class Fixture
    {
        internal readonly FakeAccess Access = new();
        internal readonly FakeStore Store = new();
        internal readonly FakeDiscovery Discovery = new();
        internal readonly Clock Clock = new();
        internal AuthoringTriggerCommitter Committer { get; }
        internal TriggerManagement Management { get; }
        internal Fixture(ITriggerManagementCommitter? runtime = null, IRecurringTriggerScheduleStore? schedules = null,
            IRuntimeIngressReservationReader? reservations = null, IEnumerable<ITriggerReservationPlanner>? planners = null,
            IEnumerable<IPortableExpressionHandler>? expressionHandlers = null, IPortableExpressionEvaluator? expressionEvaluator = null, IEnumerable<IIngressReservationPolicy>? policies = null,
            IWorkflowTriggerBindingStore? bindings = null)
        {
            Committer = new(Store);
            Management = new(Store, runtime ?? Committer, Access, new Catalog(), Discovery, new Mapper(), new Publisher(), new EphemeralDataProtectionProvider(), Clock,
                schedules: schedules, reservations: reservations, reservationPlanners: planners,
                expressionHandlers: expressionHandlers, expressionEvaluator: expressionEvaluator, reservationPolicies: policies, triggerBindings: bindings);
        }
    }
    private sealed class Clock : TimeProvider
    { internal DateTimeOffset Now = DateTimeOffset.UnixEpoch.AddDays(1); public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class FakeAccess : ITriggerRequestAccess
    {
        public string Subject { get; set; } = "operator";
        public TriggerScope Scope { get; } = new(new("tenant-a"), "tenant-a");
        internal bool TargetRead = true, TargetExecute = true, CanGrant = true;
        public ValueTask<bool> HasAsync(string permission, object? resource, CancellationToken cancellationToken) => ValueTask.FromResult(permission switch
        { "workflows/runtime:read" => TargetRead, "workflows/runtime:execute" => TargetExecute, TriggerPermissions.Authorize => CanGrant, _ => true });
        public async ValueTask RequireAsync(string permission, object? resource, CancellationToken cancellationToken, bool mask = false)
        { if (!await HasAsync(permission, resource, cancellationToken)) throw new TriggerOperationException(mask ? "missing" : "forbidden", "Denied"); }
        public ValueTask<bool> CanReadAsync(object? resource, CancellationToken cancellationToken) => ValueTask.FromResult(true);
        public ValueTask<TriggerActor> ActorAsync(CancellationToken cancellationToken) => ValueTask.FromResult(new TriggerActor(Subject, true, true, true, CanGrant, false, false, false));
    }
    private sealed class Catalog : IStandaloneTriggerTypeCatalog
    {
        public TriggerTypeDescriptor? Find(string key) => new(key, 1, key, key == "cron" ? "Cron" : "Event", [], []);
        public IReadOnlyList<TriggerTypeDescriptor> List() => [Find("event")!];
    }
    private sealed class Mapper : ITriggerInputMapper
    {
        public ValueTask<WorkflowExecutableInputValidation> MapAsync(WorkflowExecutableInputContract contract, IReadOnlyList<TriggerInputMapping> mappings, JsonElement source, CancellationToken cancellationToken = default) => ValueTask.FromResult(WorkflowExecutableInputValidation.Success([]));
        public WorkflowExecutableInputValidation ValidateMappings(WorkflowExecutableInputContract contract, IReadOnlyList<TriggerInputMapping> mappings) => WorkflowExecutableInputValidation.Success([]);
    }
    private sealed class Publisher : IInlineEventPublisher
    { public Task Publish(IEvent value, CancellationToken cancellationToken = default) => Task.CompletedTask; }
    private sealed class FakeDiscovery : IStandaloneTriggerTargetDiscovery
    {
        internal string ContractRevision = "contract-v1";
        internal Action? BeforeDescribe;
        public ValueTask<TriggerTargetDescription?> DescribeAsync(TriggerScope scope, string definitionId, string slotName, CancellationToken cancellationToken = default)
        {
            BeforeDescribe?.Invoke();
            return ValueTask.FromResult<TriggerTargetDescription?>(new(definitionId, slotName, "Target", "native", TriggerTargetState.Available,
                new(1, []), ContractRevision, "version-1", "1", "publication-1", 1, false, []));
        }
        public async ValueTask<TriggerTargetPage> ListAsync(TriggerScope scope, TriggerTargetQuery query, CancellationToken cancellationToken = default) =>
            new([(await DescribeAsync(scope, "definition-a", "default", cancellationToken))!], null);
    }
    private sealed class FakeStore : IStandaloneTriggerStore
    {
        internal TriggerAggregate Resource = new(Trigger(1), []);
        internal int Writes;
        private readonly Dictionary<string, TriggerMutationReceipt> receipts = [];
        public ValueTask<TriggerAggregate?> FindAsync(TriggerScope scope, string id, CancellationToken cancellationToken = default) => ValueTask.FromResult<TriggerAggregate?>(Resource.Trigger.Id == id ? Resource : null);
        public ValueTask<TriggerSubscription?> FindSubscriptionAsync(TriggerScope scope, string id, CancellationToken cancellationToken = default) => ValueTask.FromResult(Resource.Subscriptions.SingleOrDefault(row => row.Id == id));
        public ValueTask<TriggerResourcePage> ListAsync(TriggerScope scope, TriggerResourceQuery query, CancellationToken cancellationToken = default) => ValueTask.FromResult(new TriggerResourcePage([Resource.Trigger], null));
        public ValueTask<TriggerSubscriptionPage> ListSubscriptionsAsync(TriggerScope scope, TriggerSubscriptionQuery query, CancellationToken cancellationToken = default) => ValueTask.FromResult(new TriggerSubscriptionPage(Resource.Subscriptions, null));
        public ValueTask<TriggerMutationReceipt?> FindMutationAsync(TriggerScope scope, string actorId, string requestId, CancellationToken cancellationToken = default) => ValueTask.FromResult(receipts.GetValueOrDefault(requestId));
        public ValueTask<TriggerMutationReceipt> WriteAsync(TriggerScope scope, TriggerStoreWrite write, CancellationToken cancellationToken = default)
        {
            Writes++; Resource = new(write.Trigger, write.Subscriptions);
            var receipt = new TriggerMutationReceipt(write.RequestId, write.RequestFingerprint, write.ActorId, write.Trigger.Id, write.Trigger.Revision, DateTimeOffset.UnixEpoch);
            receipts[write.RequestId] = receipt; return ValueTask.FromResult(receipt);
        }
    }
}
