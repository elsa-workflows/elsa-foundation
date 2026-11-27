using System.Text.Json;
using System.Text.Json.Nodes;
using Cronos;
using Elsa.ActorFoundation.TestSupport;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Services.Executables;
using Kernel.Foundation.Contracts;
using Kernel.Native;
using Kernel.WorkflowActor;
using Microsoft.Extensions.Time.Testing;
using Xunit;
using static Elsa.Workflows.Runtime.KernelHost.Tests.Compiled;
using WorkflowActor = Kernel.WorkflowActor.WorkflowActor;

namespace Elsa.Workflows.Runtime.KernelHost.Tests;

public sealed class KernelScheduleAuthorityTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 10, 0, 0, TimeSpan.Zero);
    private static readonly ActorRef Actor = new(new string('A', 64));
    private static readonly string Timer = "~" + new string('B', 64);

    [Theory]
    [InlineData(null)]
    [InlineData("default")]
    [InlineData("tenant-a")]
    public async Task The_exact_published_timer_pins_the_Start_spec_and_uses_the_live_Foundation_clock(string? runtimeTenant)
    {
        var fixture = await Publication(runtimeTenant);
        var header = Assert.IsType<ScheduleHeader>(await fixture.BindAsync());
        Assert.Equal(new RecurringSchedule(RecurringSchedule.Interval, "PT3S"), header.Schedule);
        Assert.Equal("schedule-start:scheduled-definition:trigger", header.StartKey);
        Assert.Equal(4, header.Revision);

        var offers = new List<RuntimeKernelScheduleOccurrence>();
        var reads = 0;
        var checkedOn = Now.AddSeconds(4);
        RuntimeKernelScheduleAttemptOutcome? result = RuntimeKernelScheduleAttemptOutcome.Accepted;
        using var attempt = new CancellationTokenSource();
        var acceptor = new KernelScheduleOccurrenceAcceptor(fixture.Control.Scope, fixture.FoundationTenant, token =>
        {
            Assert.Equal(attempt.Token, token);
            return ValueTask.FromResult(checkedOn.AddSeconds(reads++).ToUnixTimeMilliseconds());
        }, async (occurrence, material, deadline, readClock, token) =>
        {
            Assert.Equal(fixture.Control.Owner!.Executable, material);
            Assert.Equal(attempt.Token, token);
            Assert.True(deadline >= occurrence.AcceptBefore);
            offers.Add(occurrence);
            if (offers.Count == 1)
            {
                Assert.Equal(checkedOn, await readClock(token));
                Assert.Equal(checkedOn.AddSeconds(1), await readClock(token));
            }
            return result ?? throw new IOException("The store's commit acknowledgement was lost.");
        });

        var offer = Offer(fixture, header);
        Assert.True(await acceptor.TryAcceptAsync(offer, attempt.Token));
        var stored = Assert.Single(offers);
        Assert.Equal(fixture.Control.Scope, stored.Scope);
        Assert.Equal(runtimeTenant, stored.Scope.TenantId);
        Assert.Equal(fixture.Control.ScheduleKey, stored.ScheduleKey);
        Assert.Equal(Actor.Id, stored.ScheduleReference);
        Assert.Equal("Green", stored.SlotName);
        Assert.Equal("pub-green", stored.ActivationId);
        Assert.Equal(1, stored.PublicationRevision);
        Assert.Equal(6, stored.ArmGeneration);
        Assert.Equal(Timer, stored.TimerDeliveryId);
        Assert.Equal(Now, stored.Due);
        Assert.Equal(Now.AddMinutes(1), stored.AcceptBefore);
        Assert.Equal(fixture.Source.ArtifactId, stored.Executable.ArtifactId);
        Assert.Equal("trigger", stored.TriggerNode);
        var expected = WorkflowActor.Start(NativeExecutionModel.Start(fixture.Source.ArtifactId,
            triggerNode: "trigger", trigger: JsonValue.Create(fixture.Control.ScheduleKey), definition: fixture.Control.DefinitionId) with
        {
            ExecutableSelection = JsonSerializer.Serialize(fixture.Control.Owner!.Executable)
        });
        Assert.Equal(JsonSerializer.Serialize(expected), stored.PinnedStart);

        // The active source may retire before replay. The exact callback still offers the original pinned bytes.
        await fixture.References.SaveAsync(fixture.Source.Retire(Now));
        Assert.Null(await fixture.BindAsync());
        Assert.True(await acceptor.TryAcceptAsync(offer, attempt.Token));
        Assert.Equal(stored, offers[1]);
        Assert.Equal(2, reads);
        result = RuntimeKernelScheduleAttemptOutcome.Refused;
        Assert.False(await acceptor.TryAcceptAsync(offer, attempt.Token));
        result = RuntimeKernelScheduleAttemptOutcome.Retry;
        await Assert.ThrowsAsync<TimeoutException>(() =>
            acceptor.TryAcceptAttemptAsync(offer, offer.OfferUntilUnixMilliseconds + 60_000, attempt.Token).AsTask());
        Assert.Equal(stored, offers[^1]);
        result = null;
        await Assert.ThrowsAsync<IOException>(() => acceptor.TryAcceptAsync(offer, attempt.Token).AsTask());
    }

    [Fact]
    public async Task A_foreign_control_missing_source_wrong_artifact_or_non_start_node_never_arms()
    {
        var fixture = await Publication("tenant-a");
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.BindAsync(access: new(new("tenant-b"))).AsTask());
        Assert.Null(await fixture.BindAsync(fixture.Control with { Owner = null }));
        Assert.Null(await fixture.BindAsync(fixture.Control with { Scope = fixture.Control.Scope with { TenantId = null } }));
        var owner = fixture.Control.Owner!;
        Assert.Null(await fixture.BindAsync(fixture.Control with { Owner = owner with { SlotId = "different-slot" } }));
        Assert.Null(await fixture.BindAsync(fixture.Control with
            { Owner = owner with { ActivationId = "missing", SourceReferenceId = WorkflowActivationReferenceIdentity.Create("missing") } }));
        Assert.Null(await fixture.BindAsync(fixture.Control with
            { Owner = owner with { Executable = owner.Executable with { ArtifactHash = "different-hash" } } }));
        Assert.Null(await fixture.BindAsync(fixture.Control with { ScheduleKey = "schedule:another-definition:trigger" }));
        Assert.Null(await fixture.BindAsync(fixture.Control with
            { TriggerNode = "missing", ScheduleKey = "schedule:scheduled-definition:missing" }));
        await fixture.References.SaveAsync(fixture.Source with { TenantId = null });
        Assert.Null(await fixture.BindAsync());
        await fixture.References.SaveAsync(fixture.Source with { DefinitionVersionId = "different-version" });
        Assert.Null(await fixture.BindAsync());
    }

    [Fact]
    public async Task Refuses_offers_that_do_not_match_the_bound_tenant_shape_or_pinned_start()
    {
        var fixture = await Publication(null);
        var header = Assert.IsType<ScheduleHeader>(await fixture.BindAsync());
        var original = Offer(fixture, header);
        var calls = 0;
        var acceptor = new KernelScheduleOccurrenceAcceptor(fixture.Control.Scope, fixture.FoundationTenant,
            _ => throw new InvalidOperationException("A preflight refusal cannot read the Foundation clock."),
            (_, _, _, _, _) => { calls++; return ValueTask.FromResult(RuntimeKernelScheduleAttemptOutcome.Accepted); });
        var payload = JsonNode.Parse(header.Authority!)!.AsObject();
        var pinned = JsonSerializer.Deserialize<StartSpec>(payload["PinnedStart"]!.GetValue<string>())!;
        payload["PinnedStart"] = JsonSerializer.Serialize(pinned with { Input = """{"Mode":"Balanced","TriggerNode":"different"}""" });

        Assert.False(await acceptor.TryAcceptAsync(original with { Tenant = "tenant-a" }));
        Assert.False(await acceptor.TryAcceptAsync(original with { Schedule = new("unissued") }));
        Assert.False(await acceptor.TryAcceptAsync(original with { TimerDeliveryId = "unissued" }));
        Assert.False(await acceptor.TryAcceptAsync(original with { ArmGeneration = 0 }));
        Assert.False(await acceptor.TryAcceptAsync(original with { OfferUntilUnixMilliseconds = long.MaxValue }));
        Assert.False(await acceptor.TryAcceptAsync(original with { Authority = "{invalid" }));
        Assert.False(await acceptor.TryAcceptAsync(original with { Authority = new string('a', 16_385) }));
        Assert.False(await acceptor.TryAcceptAsync(original with { Authority = payload.ToJsonString() }));
        var typeData = JsonNode.Parse(pinned.TypeData)!.AsObject();
        typeData["Version"] = int.MaxValue;
        payload["PinnedStart"] = JsonSerializer.Serialize(pinned with { TypeData = typeData.ToJsonString() });
        Assert.False(await acceptor.TryAcceptAsync(original with { Authority = payload.ToJsonString() }));

        Assert.Equal(0, calls);
        Assert.Throws<ArgumentException>(() => new KernelScheduleOccurrenceAcceptor(new(new("tenant-a"), null), ContractScopes.Bind("tenant-a"),
            _ => ValueTask.FromResult(0L), (_, _, _, _, _) => ValueTask.FromResult(RuntimeKernelScheduleAttemptOutcome.Accepted)));
    }

    [Fact]
    public async Task Predecessor_authority_replays_its_exact_bytes_and_new_authority_requires_the_material_token()
    {
        var fixture = await Publication(null);
        var header = Assert.IsType<ScheduleHeader>(await fixture.BindAsync());
        var payload = JsonNode.Parse(header.Authority!)!.AsObject();
        var original = Offer(fixture, header);
        var pinned = JsonSerializer.Deserialize<StartSpec>(payload["PinnedStart"]!.GetValue<string>())!;
        var typeData = JsonNode.Parse(pinned.TypeData)!.AsObject();
        var token = typeData["ExecutableSelection"]!.GetValue<string>();
        typeData["ExecutableSelection"] = JsonSerializer.Serialize(fixture.Control.Owner!.Executable with { ArtifactHash = "replaced" });
        payload["PinnedStart"] = JsonSerializer.Serialize(pinned with { TypeData = typeData.ToJsonString() });
        Assert.Null(KernelScheduleAuthority.ReadOffer(original with { Authority = payload.ToJsonString() }, fixture.Control.Scope, fixture.FoundationTenant));

        typeData["ExecutableSelection"] = JsonSerializer.Serialize(fixture.Control.Owner.Executable, new JsonSerializerOptions { WriteIndented = true });
        payload["PinnedStart"] = JsonSerializer.Serialize(pinned with { TypeData = typeData.ToJsonString() });
        Assert.Null(KernelScheduleAuthority.ReadOffer(original with { Authority = payload.ToJsonString() }, fixture.Control.Scope, fixture.FoundationTenant));

        typeData.Remove("ExecutableSelection");
        var legacyPinned = JsonSerializer.Serialize(pinned with { TypeData = typeData.ToJsonString() });
        payload["PinnedStart"] = legacyPinned;
        Assert.Null(KernelScheduleAuthority.ReadOffer(original with { Authority = payload.ToJsonString() }, fixture.Control.Scope, fixture.FoundationTenant));

        payload["Version"] = 1;
        var replay = Assert.IsType<RuntimeKernelScheduleOccurrence>(KernelScheduleAuthority.ReadOffer(
            original with { Authority = payload.ToJsonString() }, fixture.Control.Scope, fixture.FoundationTenant, out _, out var material));
        Assert.Null(material);
        Assert.Equal(legacyPinned, replay.PinnedStart);
        Assert.Equal(fixture.Control.Owner.Executable, replay.Executable);
        Assert.Equal(fixture.Control.Owner.Executable, JsonSerializer.Deserialize<WorkflowExecutableIdentity>(token));
    }

    [Fact]
    public async Task One_foundation_actor_key_handles_both_default_aliases_and_preserves_the_exact_ledger_scope()
    {
        var fixture = await Publication(null);
        var control = fixture.Control;
        var offers = new List<RuntimeKernelScheduleOccurrence>();
        // Configure the host with the explicit default alias. Each committed source carries its own exact alias.
        var configured = control.Scope with { TenantId = PersistenceScope.DefaultValue };
        var acceptor = new KernelScheduleOccurrenceAcceptor(configured, fixture.FoundationTenant,
            _ => ValueTask.FromResult(Now.ToUnixTimeMilliseconds()), (occurrence, _, _, _, _) =>
            {
                offers.Add(occurrence);
                return ValueTask.FromResult(RuntimeKernelScheduleAttemptOutcome.Accepted);
            });
        var revisions = new List<long>();
        var keys = new List<string>();
        var aliases = new[] { null, PersistenceScope.DefaultValue, null };
        for (var i = 0; i < aliases.Length; i++)
        {
            var activation = $"default-{i + 1}";
            var sourceId = WorkflowActivationReferenceIdentity.Create(activation);
            var exact = control.Scope with { TenantId = aliases[i] };
            await fixture.References.SaveAsync(fixture.Source with { SourceReferenceId = sourceId, ActivationId = activation, TenantId = exact.TenantId });
            var current = control with { Scope = exact, Revision = i + 1, Owner = control.Owner! with
                { SourceReferenceId = sourceId, ActivationId = activation, PublicationRevision = i + 1 } };
            var header = Assert.IsType<ScheduleHeader>(await fixture.BindAsync(current));
            revisions.Add(header.Revision);
            keys.Add(header.StartKey);
            Assert.True(await acceptor.TryAcceptAsync(Offer(fixture, header) with { ArmGeneration = i + 1 }));
        }
        Assert.Equal([1L, 2L, 3L], revisions);
        Assert.All(keys, key => Assert.Equal("schedule-start:scheduled-definition:trigger", key));
        Assert.Equal(aliases, offers.Select(offer => offer.Scope.TenantId).ToArray());
        Assert.All(offers, offer => { Assert.Equal(Actor.Id, offer.ScheduleReference); Assert.Equal(control.ScheduleKey, offer.ScheduleKey); });
    }

    [Fact]
    public async Task A_published_flowchart_pins_its_exact_start_trigger_node()
    {
        var authored = Node("flow", Types.Flowchart,
            structure: Structure("elsa.flowchart.structure", new
            {
                startNodeId = "trigger",
                connections = new[] { new { source = new { nodeId = "trigger", port = "Done" }, target = new { nodeId = "after" } } },
                variables = Array.Empty<object>()
            }),
            slots: [Slot("Flowchart.Activities",
                WriteLine("after", Literal("text", "scheduled flowchart")),
                Node("trigger", Types.Timer, [Literal("Interval", "PT3S", "String")]))]);
        var fixture = await Publication(null, trigger: authored);
        var header = Assert.IsType<ScheduleHeader>(await fixture.BindAsync());
        var occurrence = Assert.IsType<RuntimeKernelScheduleOccurrence>(
            KernelScheduleAuthority.ReadOffer(Offer(fixture, header), fixture.Control.Scope, fixture.FoundationTenant));
        var pinned = JsonSerializer.Deserialize<StartSpec>(occurrence.PinnedStart)!;
        Assert.Equal("trigger", JsonSerializer.Deserialize<NativeStart>(pinned.Input!)!.TriggerNode);
    }

    [Fact]
    public async Task Cron_is_parsed_by_the_host_calendar_and_the_serialized_header_is_bounded()
    {
        var cron = await Publication(null, trigger: Node("trigger", "Elsa.Cron", [Literal("Expression", "*/30 * * * * *", "String")]));
        await Assert.ThrowsAsync<NativeScheduleException>(() => cron.BindAsync().AsTask());
        var calendar = new Calendar();
        var header = Assert.IsType<ScheduleHeader>(await cron.BindAsync(calendar: calendar));
        Assert.Equal(new RecurringSchedule(RecurringSchedule.Cron, "*/30 * * * * *"), header.Schedule);
        Assert.Equal(30_000, calendar.First);

        var malformed = await Publication(null, trigger: Node("trigger", "Elsa.Cron", [Literal("Expression", "not a cron", "String")]));
        await Assert.ThrowsAsync<NativeScheduleException>(() => malformed.BindAsync(calendar: calendar).AsTask());
        var oversized = await Publication(null, definition: new string('é', 3_000));
        var refused = await Assert.ThrowsAsync<MetadataLimitExceeded>(() => oversized.BindAsync().AsTask());
        Assert.Equal(16 * 1024, refused.LimitBytes);
    }

    private static ScheduleOccurrence Offer(Fixture fixture, ScheduleHeader header) =>
        new(fixture.FoundationTenant.Tenant, Actor, 6, Timer, Now.ToUnixTimeMilliseconds(), Now.AddMinutes(1).ToUnixTimeMilliseconds(), header.Authority!);

    private static async Task<Fixture> Publication(string? tenant, string definition = "scheduled-definition", ExecutableNode? trigger = null)
    {
        const string activation = "pub-green";
        const string slotName = "Green";
        var executable = ExecutableOf("immutable-artifact", definition,
            trigger ?? Node("trigger", Types.Timer, [Literal("Interval", "PT3S", "String")]));
        var references = new InMemoryWorkflowExecutableSourceReferenceStore();
        var executables = new InMemoryWorkflowExecutableStore();
        await executables.SaveAsync(executable);
        var slot = WorkflowActivationSlotIdentity.Create(definition, slotName);
        var identity = executable.Identity;
        var source = new WorkflowExecutableSourceReference(WorkflowActivationReferenceIdentity.Create(activation), identity.ArtifactId,
            "definition", definition, identity.ArtifactVersion, definition, identity.DefinitionVersionId, identity.ArtifactVersion,
            Now, Now, WorkflowExecutableReferenceScope.Published, ActivationId: activation, SlotId: slot, TenantId: tenant);
        await references.SaveAsync(source);
        var tenantName = tenant ?? PersistenceScope.DefaultValue;
        var control = new RuntimeKernelScheduleControl(new(new(tenantName), tenant), $"schedule:{definition}:trigger", definition, "trigger", 4,
            new(slot, slotName, activation, 1, source.SourceReferenceId, identity));
        return new(control, source, ContractScopes.Bind(tenantName), references, executables);
    }

    private sealed record Fixture(RuntimeKernelScheduleControl Control, WorkflowExecutableSourceReference Source, BoundScope FoundationTenant,
        InMemoryWorkflowExecutableSourceReferenceStore References, InMemoryWorkflowExecutableStore Executables)
    {
        internal ValueTask<ScheduleHeader?> BindAsync(RuntimeKernelScheduleControl? control = null, ICronCalendar? calendar = null, Accessor? access = null) =>
            KernelScheduleAuthority.BindAsync(control ?? Control, Control.Scope, FoundationTenant, References, Executables,
                KernelHostCapabilities.All, calendar, access ?? new(Control.Scope.Persistence), new FakeTimeProvider(Now));
    }

    private sealed class Accessor(PersistenceScope scope) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(scope);
    }

    private sealed class Calendar : ICronCalendar
    {
        internal long? First { get; private set; }

        public long? Next(string expression, long after)
        {
            try
            {
                First = CronExpression.Parse(expression, CronFormat.IncludeSeconds)
                    .GetNextOccurrence(DateTimeOffset.FromUnixTimeMilliseconds(after), TimeZoneInfo.Utc)?.ToUnixTimeMilliseconds();
                return First;
            }
            catch (CronFormatException invalid) { throw new FormatException(invalid.Message, invalid); }
        }
    }
}
