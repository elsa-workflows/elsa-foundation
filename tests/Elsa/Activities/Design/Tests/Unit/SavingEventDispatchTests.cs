using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.EFCore.DbContext;
using Elsa.Activities.Design.Persistence.EFCore.EntityHandlers;
using Elsa.Events.Core.Contracts;
using Elsa.Persistence.EFCore.Contracts;
using Elsa.Persistence.EFCore.Events;
using Elsa.Persistence.EFCore.Handlers;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using Xunit;

namespace Elsa.Activities.Design.Tests.Unit;

/// <summary>
/// US5 — activity-catalog saving handler runs on the converged contributor + single-aggregator
/// shape. <see cref="ActivityDefinitionVersionSavingHandler"/> is a typed
/// <see cref="IEntitySavingHandler{TDbContext,TEntity}"/>; the single
/// <see cref="ApplyEntitySavingHandlers"/> aggregator is the sole
/// <see cref="IEventHandler{OnEntitySaving}"/> and dispatches every registered typed contributor.
/// Tests verify (a) the typed handler's behaviour through its Handle signature, (b) the aggregator
/// fans out to every registered typed handler, and (c) exactly one
/// <see cref="IEventHandler{OnEntitySaving}"/> exists (the aggregator).
/// </summary>
public sealed class SavingEventDispatchTests
{
    [Fact]
    public async Task SavingHandler_ProducesShadowDescriptor()
    {
        // Invokes the typed handler directly and asserts the descriptor JSON lands in the shadow
        // column + the kind is derived. This is the same work the aggregator drives in production.
        using var host = ActivitiesDesignTestHost.Create();

        var serializer = new JsonPayloadSerializer(new JsonPayloadConverterRegistry());
        var handler = new ActivityDefinitionVersionSavingHandler(serializer);

        await using var ctx = host.CreateContext();
        var version = NewVersion();

        await handler.Handle(ctx, version, CancellationToken.None);

        Assert.Equal(typeof(TypeInformation).FullName, version.DescriptorType);
        Assert.False(string.IsNullOrWhiteSpace(version.DescriptorPayloadSource));
        Assert.Contains("Acme", version.DescriptorPayloadSource);
        Assert.False(string.IsNullOrWhiteSpace(version.DesignFacetsSource));
        Assert.Contains("elsa.test.visual", version.DesignFacetsSource);
    }

    [Fact]
    public async Task LoadingHandler_RehydratesDesignFacets()
    {
        var serializer = new JsonPayloadSerializer(new JsonPayloadConverterRegistry());
        var savingHandler = new ActivityDefinitionVersionSavingHandler(serializer);
        var loadingHandler = new ActivityDefinitionVersionLoadingHandler(serializer, NullLogger<ActivityDefinitionVersionLoadingHandler>.Instance);

        using var host = ActivitiesDesignTestHost.Create();
        await using var ctx = host.CreateContext();
        var saved = NewVersion();
        await savingHandler.Handle(ctx, saved, CancellationToken.None);

        var loaded = new ActivityDefinitionVersion("1.0.0", saved.DefinitionId)
        {
            Id = saved.Id,
            DescriptorType = saved.DescriptorType,
            DescriptorPayloadSource = saved.DescriptorPayloadSource,
            DesignFacetsSource = saved.DesignFacetsSource,
        };

        await loadingHandler.Handle(ctx, loaded, CancellationToken.None);

        var facet = Assert.Single(loaded.DesignFacets);
        Assert.Equal("elsa.test.visual", facet.Kind);
        Assert.Equal("1.0.0", facet.SchemaVersion);
        Assert.Equal("left", facet.Payload.GetProperty("lane").GetString());
    }

    [Fact]
    public async Task Aggregator_DispatchesToEveryRegisteredTypedHandler()
    {
        // The converged shape: the single aggregator resolves every IEntitySavingHandler<,>
        // closed over the runtime DbContext + entity types and invokes each. We register the
        // real activity-catalog handler alongside a probe and assert both run.
        var probe = new ProbeSavingHandler();
        var serializer = new JsonPayloadSerializer(new JsonPayloadConverterRegistry());

        var services = new ServiceCollection();
        services.AddSingleton<IPayloadSerializer>(serializer);
        services.AddScoped<IEntitySavingHandler<ActivitiesDesignDbContext, ActivityDefinitionVersion>, ActivityDefinitionVersionSavingHandler>();
        services.AddSingleton<IEntitySavingHandler<ActivitiesDesignDbContext, ActivityDefinitionVersion>>(probe);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        using var host = ActivitiesDesignTestHost.Create();
        await using var ctx = host.CreateContext();
        var version = NewVersion();
        var entry = ctx.ActivityDefinitionVersions.Add(version);

        var aggregator = new ApplyEntitySavingHandlers(scope.ServiceProvider);
        await aggregator.Handle(new OnEntitySaving(ctx, entry), CancellationToken.None);

        // Probe ran (fan-out reached every typed contributor)…
        Assert.True(probe.WasInvoked);
        // …and the real handler ran (shadow column populated).
        Assert.Equal(typeof(TypeInformation).FullName, version.DescriptorType);
        Assert.Contains("Acme", version.DescriptorPayloadSource);
    }

    [Fact]
    public async Task Aggregator_IsNoOp_ForUnrelatedEntities()
    {
        // The aggregator closes over the runtime entity type. With no IEntitySavingHandler<,>
        // registered for ActivityDefinition, publishing OnEntitySaving for it is inert.
        var services = new ServiceCollection();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        using var host = ActivitiesDesignTestHost.Create();
        await using var ctx = host.CreateContext();
        var unrelated = new ActivityDefinition
        {
            Id = Guid.NewGuid().ToString("N"),
            ActivityTypeKey = "X",
            Category = "Test"
        };
        var entry = ctx.ActivityDefinitions.Add(unrelated);

        var aggregator = new ApplyEntitySavingHandlers(scope.ServiceProvider);
        // No registered handler for ActivityDefinition → completes without throwing.
        await aggregator.Handle(new OnEntitySaving(ctx, entry), CancellationToken.None);
    }

    [Fact]
    public void ExactlyOneEventHandler_HandlesOnEntitySaving()
    {
        // The audience for OnEntitySaving is the single aggregator — features contribute via the
        // typed interface, never their own IEventHandler<OnEntitySaving>.
        var services = new ServiceCollection();
        services.AddScoped<IEventHandler, ApplyEntitySavingHandlers>();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var savingSubscribers = scope.ServiceProvider
            .GetServices<IEventHandler>()
            .OfType<IEventHandler<OnEntitySaving>>()
            .ToList();

        Assert.Single(savingSubscribers);
        Assert.IsType<ApplyEntitySavingHandlers>(savingSubscribers[0]);
    }

    private static ActivityDefinitionVersion NewVersion()
    {
        var defId = Guid.NewGuid().ToString("N");
        return new ActivityDefinitionVersion("1.0.0", defId)
        {
            Id = Guid.NewGuid().ToString("N"),
            DescriptorType = typeof(TypeInformation).FullName!,
            DescriptorPayload = JsonSerializer.SerializeToElement(new TypeInformation("Foo", "Acme", "Acme", "1.0.0.0")),
            DesignFacets =
            [
                Facet("elsa.test.visual", """{ "lane": "left" }""")
            ]
        };
    }

    private static ActivityDesignFacet Facet(string kind, string json)
    {
        using var document = JsonDocument.Parse(json);
        return new ActivityDesignFacet(kind, "1.0.0", document.RootElement);
    }

    private sealed class ProbeSavingHandler : IEntitySavingHandler<ActivitiesDesignDbContext, ActivityDefinitionVersion>
    {
        public bool WasInvoked { get; private set; }

        public ValueTask Handle(ActivitiesDesignDbContext dbContext, ActivityDefinitionVersion entity, CancellationToken cancellationToken)
        {
            WasInvoked = true;
            return ValueTask.CompletedTask;
        }
    }
}
