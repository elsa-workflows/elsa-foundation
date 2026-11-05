using Elsa.Workflows.Runtime.Core.Configuration;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class StimulusDeliveryStoreTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Cohort_GC_keeps_template_material_through_absent_target_handoff_then_collects_after_sticky_retirement(bool ef)
    {
        await using var harness = await Harness.Create(ef);
        var artifacts = harness.Artifacts; var references = harness.References;
        IExecutableActivityTemplateStore templates = ef
            ? new EfExecutableActivityTemplateStore(harness.Context!, new Accessor(), new Codec())
            : InMemoryExecutableActivityTemplateStore.CreateForMaintenance(artifacts, references, harness.Clock);
        Assert.True(templates.CanMaintainReferences(artifacts, references));
        var original = Material(Pin);
        ExecutableActivityTemplate Template(string id) => new(id, "hash-" + id, original.RootActivity,
            new Dictionary<string, WorkflowExecutableResumeTarget>(), [], [], [], "test/1", new Dictionary<string, string>(), Now);
        var template = Template("selected-template"); var unrelated = Template("unrelated-template");
        await templates.SaveAsync(template); await templates.SaveAsync(unrelated);
        await artifacts.SaveAsync(new(original.Identity, original.RootActivity, original.ResumeTargets, original.CreatedAt,
            new Dictionary<string, string> { ["activity.templateHash"] = template.TemplateHash }, original.IncidentStrategy));
        WorkflowExecutableSourceReference Reference(ExecutableActivityTemplate value) => new("source-" + value.TemplateId,
            value.TemplateId, "ActivityDefinitionVersion", "version", "1", "definition", "version", "1", Now, Now,
            WorkflowExecutableReferenceScope.Published, DeletedAt: Now, DeletedReason: "retired");
        await references.SaveAsync(Reference(template)); await references.SaveAsync(Reference(unrelated));
        var source = new PostCommitDeliveryContext(await harness.Seed(), Scope);
        await harness.Delivery.InstallAsync(source, Snapshot(source));
        Assert.True(await references.RetireAsync("selected-reference", Now));
        var collector = new WorkflowExecutableReferenceGarbageCollector(artifacts, references, templates, harness.Executions,
            null, Options.Create(new WorkflowExecutableGarbageCollectionOptions { ArtifactCreationGracePeriod = TimeSpan.Zero }),
            harness.Clock, NullLogger<WorkflowExecutableReferenceGarbageCollector>.Instance, harness.Delivery);

        var owed = await collector.SweepAsync();
        Assert.Equal(1, owed.DeletedReferenceCount); Assert.Equal(1, owed.DeletedActivityTemplateCount);
        Assert.NotNull(await templates.FindAsync(template.TemplateId));
        Assert.NotNull(await references.FindAsync("source-" + template.TemplateId));
        Assert.Null(await templates.FindAsync(unrelated.TemplateId));
        await harness.Claims.RecordDeliveryResultAsync(source.Claim, new(source.SourceOutboxItemId, RuntimePostCommitOutboxStatus.Delivered, Now));
        Assert.True(await harness.Executions.DeleteAsync("publisher"));
        Assert.False((await collector.SweepAsync()).DidWork); // Missing target is still an owed handoff, not a released pin.
        Assert.NotNull(await artifacts.FindAsync(Pin.ArtifactId)); Assert.NotNull(await templates.FindAsync(template.TemplateId));
        var target = KeyedWorkflowStartIdentity.For("key", Pin.ArtifactId).WorkflowExecutionId;
        await harness.Executions.SaveAsync(Publisher with { WorkflowExecutionId = target, Status = WorkflowExecutionStatus.Completed });
        Assert.True(await harness.Executions.DeleteAsync(target));
        var retired = await collector.SweepAsync();
        Assert.Equal(1, retired.DeletedArtifactCount);
        Assert.NotNull(await templates.FindAsync(template.TemplateId)); // Physical workflow material protected this sweep's initial reference cleanup.
        var collected = await collector.SweepAsync();
        Assert.Equal(1, collected.DeletedReferenceCount); Assert.Equal(1, collected.DeletedActivityTemplateCount);
        Assert.Null(await templates.FindAsync(template.TemplateId)); Assert.Null(await references.FindAsync("source-" + template.TemplateId));
    }
}
