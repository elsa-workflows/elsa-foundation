using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class StimulusDeliverySchemaGateTests
{
    [Fact]
    public async Task Finalized_artifact_family_accepts_legacy_sources_new_material_and_root_lease_writes()
    {
        await using var fixture = await Fixture.Create(RuntimeArtifactEfModule.SchemaFamily);
        var original = RuntimeEventExecutableTestFixture.Create("family-original");
        var reference = new WorkflowExecutableSourceReference("family-source", original.Identity.ArtifactId, "definition",
            "source", "1", original.Identity.DefinitionId, original.Identity.DefinitionVersionId, "1", Now, Now,
            WorkflowExecutableReferenceScope.Published, TenantId: Scope.Value);
        string sourceContent;
        WorkflowExecutableRootWriteLease existingLease;
        await using (var seed = fixture.Open())
        {
            await new EfWorkflowExecutableStore(seed, new Accessor()).SaveAsync(original);
            await new EfWorkflowExecutableSourceReferenceStore(seed, new Accessor(), new NoTokens()).SaveAsync(reference);
            sourceContent = (await seed.WorkflowExecutableSourceReferences.AsNoTracking().SingleAsync()).ContentJson;
            existingLease = Assert.IsType<WorkflowExecutableRootWriteLease>(await new EfWorkflowExecutableStore(seed, new Accessor())
                .TryAcquireRootWriteLeaseAsync(original.Identity.ArtifactId, "family-old-lease", Now.AddMinutes(2), Now));
        }
        await fixture.Finalize();
        await using var context = fixture.Open();
        var sources = new EfWorkflowExecutableSourceReferenceStore(context, new Accessor(), new NoTokens());
        var retired = reference with { DeletedAt = Now.AddSeconds(1) };
        Assert.True(await sources.TryRetireAsync(reference, retired));
        Assert.True(await sources.TryRestoreAsync(retired, reference));
        var savedSource = await context.WorkflowExecutableSourceReferences.AsNoTracking().SingleAsync();
        Assert.Equal(RuntimeArtifactEfModule.ScopedPublicationSchemaVersion, savedSource.SchemaVersion);
        Assert.Equal(sourceContent, savedSource.ContentJson);
        Assert.Null((await sources.FindAsync(reference.SourceReferenceId))!.PublicationScope);

        var materials = new EfWorkflowExecutableStore(context, new Accessor());
        Assert.True(await materials.RenewRootWriteLeaseAsync(existingLease, Now.AddMinutes(3), Now.AddSeconds(1)));
        Assert.Equal(RuntimeArtifactEfModule.SchemaVersion, (await context.WorkflowExecutableRootWriteLeases.AsNoTracking().SingleAsync()).SchemaVersion);
        await materials.ReleaseRootWriteLeaseAsync(existingLease);
        await materials.SaveAsync(RuntimeEventExecutableTestFixture.Create("family-new"));
        Assert.Contains(await context.WorkflowExecutables.AsNoTracking().ToArrayAsync(),
            row => row.SchemaVersion == RuntimeArtifactEfModule.ScopedPublicationSchemaVersion);
        Assert.Contains(await context.WorkflowExecutableCoordinations.AsNoTracking().ToArrayAsync(),
            row => row.SchemaVersion == RuntimeArtifactEfModule.ScopedPublicationSchemaVersion);
        var template = new ExecutableActivityTemplate("family-template", "family-template-hash", original.RootActivity,
            original.ResumeTargets, [], [], [], "test/1", new Dictionary<string, string>(), Now);
        await new EfExecutableActivityTemplateStore(context, new Accessor(), new NoTokens()).SaveAsync(template);
        Assert.Equal(RuntimeArtifactEfModule.ScopedPublicationSchemaVersion, (await context.ExecutableActivityTemplates.AsNoTracking().SingleAsync()).SchemaVersion);
        Assert.Equal(RuntimeArtifactEfModule.ScopedPublicationSchemaVersion, (await context.ExecutableActivityTemplateHashClaims.AsNoTracking().SingleAsync()).SchemaVersion);
        var modelPin = new WorkflowExecutableIdentity("family-new-model", "model-definition", "model-version", "1.0.0", "pending");
        RuntimeModelMaterial Model(WorkflowExecutableIdentity pin) => new(1, pin, new("test.family", 1), "test/1",
            JsonSerializer.SerializeToElement(new { start = "wait" }), new(1, []), [], [], []);
        var modelSeed = Model(modelPin);
        await ((IWorkflowExecutableStore)materials).SaveModelAsync(Model(modelPin with { ArtifactHash = modelSeed.ComputeContentHash() }));
        Assert.Equal(RuntimeArtifactEfModule.ScopedPublicationSchemaVersion, (await context.WorkflowExecutables.AsNoTracking().SingleAsync(row => row.IsModelMaterial)).SchemaVersion);
        var lease = await materials.TryAcquireRootWriteLeaseAsync(original.Identity.ArtifactId, "family-lease", Now.AddMinutes(2), Now);
        Assert.NotNull(lease);
        Assert.True(await materials.RenewRootWriteLeaseAsync(lease, Now.AddMinutes(3), Now.AddSeconds(1)));
        var savedLease = await context.WorkflowExecutableRootWriteLeases.AsNoTracking().SingleAsync();
        Assert.Equal(RuntimeArtifactEfModule.ScopedPublicationSchemaVersion, savedLease.SchemaVersion);
        await materials.ReleaseRootWriteLeaseAsync(lease);
        Assert.Empty(await context.WorkflowExecutableRootWriteLeases.AsNoTracking().ToArrayAsync());
    }

    [Fact]
    public async Task Finalized_operational_family_keeps_legacy_claim_content_and_accepts_sibling_state_writes()
    {
        await using var fixture = await Fixture.Create(RuntimeOperationalStateEfModule.SchemaFamily);
        var timer = new DurableTimer("family-timer", "family-execution", "Delay", "delay", Now, Now);
        var schedule = new RecurringTriggerSchedule(RecurringTriggerSchedule.BuildId("artifact", "node"), "artifact", "node", "Timer", "timer",
            RecurringScheduleKind.Interval, "PT1M", Now, Now, null, null, true);
        // Frozen predecessor documents: the current writer only arranges the relational projections.
        const string timerContent = """
            {"timerId":"ZgBhAG0AaQBsAHkALQB0AGkAbQBlAHIA","workflowExecutionId":"ZgBhAG0AaQBsAHkALQBlAHgAZQBjAHUAdABpAG8AbgA=",
             "stimulusType":"RABlAGwAYQB5AA==","stimulusHash":"ZABlAGwAYQB5AA==","dueTime":"2026-10-26T01:00:00+00:00",
             "createdAt":"2026-10-26T01:00:00+00:00","input":null,"metadata":null,"payloadType":null,"providerId":null}
            """;
        const string scheduleContent = """
            {"scheduleId":"YQByAHQAaQBmAGEAYwB0ADoAbgBvAGQAZQA=","artifactId":"YQByAHQAaQBmAGEAYwB0AA==","executableNodeId":"bgBvAGQAZQA=",
             "stimulusType":"VABpAG0AZQByAA==","stimulusHash":"dABpAG0AZQByAA==","kind":"Interval","expression":"UABUADEATQA=",
             "nextOccurrence":"2026-10-26T01:00:00+00:00","createdAt":"2026-10-26T01:00:00+00:00","activationId":null,"slotId":null,"isActive":true}
            """;
        await using (var seed = fixture.Open())
        {
            await new EfDurableTimerStore(seed, new Accessor(), new NoTokens()).SaveAsync(timer);
            await new EfRecurringTriggerScheduleStore(seed, new Accessor(), new NoTokens()).SaveAsync(schedule);
            var timerRow = await seed.DurableTimers.SingleAsync();
            var scheduleRow = await seed.RecurringTriggerSchedules.SingleAsync();
            Assert.Equal("1.0.0", timerRow.SchemaVersion);
            Assert.Equal("1.0.0", scheduleRow.SchemaVersion);
            timerRow.ContentJson = timerContent;
            scheduleRow.ContentJson = scheduleContent;
            await seed.SaveChangesAsync();
        }
        await fixture.Finalize();
        await using var context = fixture.Open();
        var timers = new EfDurableTimerStore(context, new Accessor(), new NoTokens());
        var timerClaim = Assert.Single(await timers.ClaimDueAsync(new("owner", Now, TimeSpan.FromMinutes(1), 1)));
        Assert.True((await timers.RenewClaimAsync(timerClaim, Now.AddSeconds(1), TimeSpan.FromMinutes(2))).Succeeded);
        var writtenTimer = await context.DurableTimers.AsNoTracking().SingleAsync();
        Assert.Equal(RuntimeOperationalStateEfModule.StandaloneScheduleSchemaVersion, writtenTimer.SchemaVersion);
        Assert.Equal(timerContent, writtenTimer.ContentJson);
        var schedules = new EfRecurringTriggerScheduleStore(context, new Accessor(), new NoTokens());
        var scheduleClaim = Assert.Single(await schedules.ClaimDueAsync(new("owner", Now, TimeSpan.FromMinutes(1), 1)));
        Assert.NotNull(await schedules.RenewClaimAsync(scheduleClaim, Now.AddSeconds(1), TimeSpan.FromMinutes(2)));
        var writtenSchedule = await context.RecurringTriggerSchedules.AsNoTracking().SingleAsync();
        Assert.Equal(RuntimeOperationalStateEfModule.StandaloneScheduleSchemaVersion, writtenSchedule.SchemaVersion);
        Assert.Equal(scheduleContent, writtenSchedule.ContentJson);

        await schedules.PrepareActivationAsync("family-activation", [schedule with
        { ScheduleId = RecurringTriggerSchedule.BuildId("family-activation", "artifact", "node"), ActivationId = "family-activation", SlotId = "family-slot", IsActive = false }]);
        await schedules.ActivateAsync("family-activation", null);
        Assert.Equal(RuntimeOperationalStateEfModule.StandaloneScheduleSchemaVersion,
            (await context.RecurringTriggerScheduleProjectionStates.AsNoTracking().SingleAsync()).SchemaVersion);
        await new EfSchedulerStateStore(context, new Accessor()).SaveAsync(new("family-execution", 1));
        await new EfDurableValueStateStore(context, new Accessor(), new NoTokens()).SaveAsync(new("value", "family-execution", "value",
            new("String", null, null), DurableValueLifecycle.Instance, DurableValueStorage.Inline, JsonSerializer.SerializeToElement("saved"),
            null, null, Now, new Dictionary<string, string>()));
        await new EfExecutionLivenessStateStore(context, new Accessor(), new NoTokens()).SaveAsync(new("liveness", "family-execution",
            null, null, null, null, [], new Dictionary<string, string>()));
        await new EfWorkflowHoldStateStore(context, new Accessor()).SaveAsync(new("hold", "family-execution"));
        await new EfRuntimeCheckpointCommitStore(context, new Accessor()).CommitAsync(Commit(Pending("family-checkpoint", false)),
            new(RuntimeCheckpointPersistenceMode.Immediate));
        Assert.Equal(RuntimeOperationalStateEfModule.StandaloneScheduleSchemaVersion, (await context.SchedulerStates.AsNoTracking().SingleAsync()).SchemaVersion);
        Assert.Equal(RuntimeOperationalStateEfModule.StandaloneScheduleSchemaVersion, (await context.DurableValueStates.AsNoTracking().SingleAsync()).SchemaVersion);
        Assert.Equal(RuntimeOperationalStateEfModule.StandaloneScheduleSchemaVersion, (await context.ExecutionLivenessStates.AsNoTracking().SingleAsync()).SchemaVersion);
        Assert.Equal(RuntimeOperationalStateEfModule.StandaloneScheduleSchemaVersion, (await context.WorkflowHoldStates.AsNoTracking().SingleAsync()).SchemaVersion);
        Assert.Equal(RuntimeOperationalStateEfModule.StandaloneScheduleSchemaVersion, (await context.RuntimeCheckpointCommits.AsNoTracking().SingleAsync()).SchemaVersion);
    }

    [Fact]
    public async Task Finalized_model_family_accepts_retained_legacy_state_and_new_input_receipts_without_changing_content()
    {
        await using var fixture = await Fixture.Create(RuntimeModelStateEfModule.SchemaFamily);
        var protocol = new RuntimeExecutionProtocol("test.family", 1);
        var definition = JsonSerializer.SerializeToElement(new { start = "wait" });
        RuntimeModelMaterial Material(WorkflowExecutableIdentity pin) => new(1, pin, protocol, "test/1", definition, new(1, []), [], [], []);
        var initialMaterial = Material(new("family-model", "definition", "version", "1.0.0", "pending"));
        var material = Material(initialMaterial.Identity with { ArtifactHash = initialMaterial.ComputeContentHash() });
        RuntimeModelExecutionState state;
        string content;
        await using (var seed = fixture.Open())
        {
            IWorkflowExecutableStore materials = new EfWorkflowExecutableStore(seed, new Accessor());
            await materials.SaveModelAsync(material);
            var actual = (await materials.FindMaterialAsync(material.Identity.ArtifactId))!;
            state = new(1, Scope, "family-model-execution", Scope.Value, protocol, material.Identity, actual.IncarnationId,
                1, RuntimeModelLifecycle.Running, new("system", "initiator"), Now, Now, definition, null);
            await EfModelCheckpointStaging.StageAsync(seed, Change("initial"), Scope.Value, CancellationToken.None);
            await seed.SaveChangesAsync();
            content = (await seed.Set<RuntimeModelExecutionEntity>().AsNoTracking().SingleAsync()).ContentJson;
        }
        await fixture.Finalize();
        await using var context = fixture.Open();
        await EfModelCheckpointStaging.StageAdmissionAsync(context, state, Scope.Value, CancellationToken.None);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        await EfModelCheckpointStaging.StageAsync(context, Change("retained", 1), Scope.Value, CancellationToken.None);
        await context.SaveChangesAsync();
        var writtenState = await context.Set<RuntimeModelExecutionEntity>().AsNoTracking().SingleAsync();
        Assert.Equal(RuntimeModelStateEfModule.IngressSchemaVersion, writtenState.SchemaVersion);
        Assert.Equal(content, writtenState.ContentJson);
        Assert.Null(EfRuntimeModelExecutionReader.Read(writtenState, Scope.Value, state.ExecutionId).IngressOrigin);
        var receipts = await context.Set<RuntimeModelInputEntity>().AsNoTracking().ToArrayAsync();
        Assert.Equal(2, receipts.Length);
        Assert.Single(receipts, row => row.SchemaVersion == RuntimeModelStateEfModule.IngressSchemaVersion);

        RuntimeModelStateChange Change(string input, long? expectedRevision = null) => new(expectedRevision, state,
            new(Scope, state.ExecutionId, input, "test.input", input, 1, Now, null, null, definition));
    }
}
