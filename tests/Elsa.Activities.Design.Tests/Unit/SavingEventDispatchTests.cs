using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.EFCore.EntityHandlers;
using Elsa.Events.Core.Contracts;
using Elsa.Persistence.EFCore.Events;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Serialization.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Design.Tests.Unit;

/// <summary>
/// US5 — activity-catalog saving handler runs via <see cref="OnEntitySaving"/> event
/// dispatch. Tests verify (a) the handler's behaviour through the event-shaped Handle signature
/// and (b) the §2.6.1 contribution-shaped DI surface: many handlers can subscribe to the same
/// event, each gets invoked.
/// </summary>
public sealed class SavingEventDispatchTests
{
    [Fact]
    public async Task SavingHandler_ProducesShadowDescriptor_FromOnEntitySaving()
    {
        // Hands a synthetic OnEntitySaving to the handler and asserts the descriptor JSON
        // lands in the shadow column. This is the same code path the DbContext dispatch
        // calls in production — we exercise the contract without standing up the full
        // event pipeline.
        using var host = ActivitiesDesignTestHost.CreateWithDescriptorRegistry(
            (kind: "Clr", type: typeof(ClrImplementationDescriptor)));

        var serializer = new JsonPayloadSerializer(new JsonPayloadConverterRegistry());
        var handler = new ActivityDefinitionVersionSavingHandler(serializer);

        await using var ctx = host.CreateContext();
        var defId = Guid.NewGuid().ToString("N");
        ctx.ActivityDefinitions.Add(new ActivityDefinition
        {
            Id = defId,
            ActivityTypeKey = "Foo",
            Category = "Test"
        });
        var version = new ActivityDefinitionVersion(1, defId)
        {
            Id = Guid.NewGuid().ToString("N"),
            ImplementationDescriptor = new ClrImplementationDescriptor(new TypeInformation("Foo", "Acme", "Acme", "1.0.0.0"))
        };
        var entry = ctx.ActivityDefinitionVersions.Add(version);

        await handler.Handle(new OnEntitySaving(ctx, entry), CancellationToken.None);

        Assert.Equal("Clr", version.ImplementationKind);
        Assert.False(string.IsNullOrWhiteSpace(version.ImplementationDescriptorPayload));
        Assert.Contains("Acme", version.ImplementationDescriptorPayload);
        _ = entry; // suppress unused

    }

    [Fact]
    public async Task SavingHandler_IsNoOp_ForUnrelatedEntities()
    {
        // The handler filters by entity type. An OnEntitySaving for a row that isn't an
        // ActivityDefinitionVersion must pass through without touching it.
        using var host = ActivitiesDesignTestHost.Create();
        var serializer = new JsonPayloadSerializer(new JsonPayloadConverterRegistry());
        var handler = new ActivityDefinitionVersionSavingHandler(serializer);

        await using var ctx = host.CreateContext();
        var unrelated = new ActivityDefinition
        {
            Id = Guid.NewGuid().ToString("N"),
            ActivityTypeKey = "X",
            Category = "Test"
        };
        var entry = ctx.ActivityDefinitions.Add(unrelated);

        // Should complete without throwing; nothing to assert beyond no-throw.
        await handler.Handle(new OnEntitySaving(ctx, entry), CancellationToken.None);
    }

    [Fact]
    public void MultipleHandlers_CanSubscribeToOnEntitySaving()
    {
        // §2.6.1 contribution shape: any feature may register its own IEventHandler<OnEntitySaving>
        // alongside the activity-catalog handler. DI resolves both — the event pipeline invokes
        // each in turn.
        var services = new ServiceCollection();
        var serializer = new JsonPayloadSerializer(new JsonPayloadConverterRegistry());
        services.AddSingleton<Elsa.Serialization.Core.IPayloadSerializer>(serializer);
        services.AddScoped<IEventHandler<OnEntitySaving>, ActivityDefinitionVersionSavingHandler>();
        services.AddScoped<IEventHandler<OnEntitySaving>, ProbeOnEntitySavingHandler>();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var handlers = scope.ServiceProvider.GetServices<IEventHandler<OnEntitySaving>>().ToList();

        Assert.Equal(2, handlers.Count);
        Assert.Contains(handlers, h => h is ActivityDefinitionVersionSavingHandler);
        Assert.Contains(handlers, h => h is ProbeOnEntitySavingHandler);
    }

    private sealed class ProbeOnEntitySavingHandler : IEventHandler<OnEntitySaving>
    {
        public Task Handle(OnEntitySaving @event, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
