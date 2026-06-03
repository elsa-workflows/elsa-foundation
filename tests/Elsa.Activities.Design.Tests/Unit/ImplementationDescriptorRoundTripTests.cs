using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.EFCore.EntityHandlers;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Serialization.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Design.Tests.Unit;

/// <summary>
/// US3 — Non-CLR activities are first-class catalog entries.
/// Round-trip proofs: the saving handler serialises the descriptor into the shadow column;
/// the loading handler deserialises it back through the descriptor-type registry.
/// </summary>
public sealed class ImplementationDescriptorRoundTripTests
{
    [Fact]
    public async Task ClrImplementationDescriptor_RoundTripsThroughTheCatalog()
    {
        using var host = ActivitiesDesignTestHost.CreateWithDescriptorRegistry(
            (kind: "Clr", type: typeof(ClrImplementationDescriptor)));

        var defId = Guid.NewGuid().ToString("N");
        var vId = Guid.NewGuid().ToString("N");
        var originalTypeInfo = new TypeInformation("SendRequest", "Acme.Http", "Acme.Http", "1.0.0.0");
        var serializer = new JsonPayloadSerializer(new JsonPayloadConverterRegistry());

        await using (var ctx = host.CreateContext())
        {
            ctx.ActivityDefinitions.Add(NewDef(defId, "Http.SendRequest"));
            var version = new ActivityDefinitionVersion(1, defId)
            {
                Id = vId,
                ImplementationDescriptor = new ClrImplementationDescriptor(originalTypeInfo),
                SourceKind = "Json",
                SourceId = "Elsa.Test",
                ReconciledAt = DateTimeOffset.UtcNow,
                ReconciledBy = "test"
            };
            ctx.ActivityDefinitionVersions.Add(version);
            // The handler is a typed IEntitySavingHandler<,>. In production the single
            // ApplyEntitySavingHandlers aggregator dispatches it when the DbContext publishes
            // OnEntitySaving; here we invoke the typed Handle directly so the test stays focused
            // on the descriptor-to-shadow serialisation path.
            var saver = new ActivityDefinitionVersionSavingHandler(serializer);
            await saver.Handle(ctx, version, CancellationToken.None);
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = host.CreateContext())
        {
            var loaded = await ctx.ActivityDefinitionVersions.SingleAsync(v => v.Id == vId);
            var loader = new ActivityDefinitionVersionLoadingHandler(
                serializer,
                host.DescriptorRegistry,
                new Microsoft.Extensions.Logging.Abstractions.NullLogger<ActivityDefinitionVersionLoadingHandler>());
            await loader.Handle(ctx, loaded, CancellationToken.None);

            Assert.Equal("Clr", loaded.ImplementationKind);
            var clr = Assert.IsType<ClrImplementationDescriptor>(loaded.ImplementationDescriptor);
            Assert.Equal(originalTypeInfo, clr.TypeInfo);
        }
    }

    [Fact]
    public async Task WorkflowImplementationDescriptor_RoundTripsStructurally_NoResolverRequired()
    {
        // SC-014 — the non-CLR descriptor persists and round-trips through the catalog even
        // though the matching resolver (Workflow → IActivity) lives in Unit G. Round-trip is
        // pure persistence — kind column + JSON shadow + registry-driven deserialisation.
        using var host = ActivitiesDesignTestHost.CreateWithDescriptorRegistry(
            (kind: "Workflow", type: typeof(WorkflowImplementationDescriptor)));

        var defId = Guid.NewGuid().ToString("N");
        var vId = Guid.NewGuid().ToString("N");
        var serializer = new JsonPayloadSerializer(new JsonPayloadConverterRegistry());

        await using (var ctx = host.CreateContext())
        {
            ctx.ActivityDefinitions.Add(NewDef(defId, "Wf.Approve"));
            var version = new ActivityDefinitionVersion(1, defId)
            {
                Id = vId,
                ImplementationDescriptor = new WorkflowImplementationDescriptor("wf-42", 7),
                SourceKind = "Json",
                SourceId = "Elsa.Test",
                ReconciledAt = DateTimeOffset.UtcNow,
                ReconciledBy = "test"
            };
            ctx.ActivityDefinitionVersions.Add(version);
            var saver = new ActivityDefinitionVersionSavingHandler(serializer);
            await saver.Handle(ctx, version, CancellationToken.None);
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = host.CreateContext())
        {
            var loaded = await ctx.ActivityDefinitionVersions.SingleAsync(v => v.Id == vId);
            var loader = new ActivityDefinitionVersionLoadingHandler(
                serializer,
                host.DescriptorRegistry,
                new Microsoft.Extensions.Logging.Abstractions.NullLogger<ActivityDefinitionVersionLoadingHandler>());
            await loader.Handle(ctx, loaded, CancellationToken.None);

            Assert.Equal("Workflow", loaded.ImplementationKind);
            var wf = Assert.IsType<WorkflowImplementationDescriptor>(loaded.ImplementationDescriptor);
            Assert.Equal("wf-42", wf.WorkflowDefinitionId);
            Assert.Equal(7, wf.WorkflowVersionId);
        }
    }

    private static ActivityDefinition NewDef(string id, string activityTypeKey) => new()
    {
        Id = id,
        ActivityTypeKey = activityTypeKey,
        Category = "Test"
    };
}
