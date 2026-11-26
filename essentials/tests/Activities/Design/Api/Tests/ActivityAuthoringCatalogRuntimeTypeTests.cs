using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Http.Activities;
using Elsa.Serialization.Core;
using Elsa.Serialization.Testing;
using Xunit;

namespace Elsa.Activities.Design.Api.Tests;

/// <summary>
/// A CLR activity that declares <c>public const string ActivityType</c> stamps that type on its compiled nodes, while
/// its catalog key stays the CLR full name. The catalog must expose the declared type so the inspector can match them.
/// </summary>
public sealed class ActivityAuthoringCatalogRuntimeTypeTests
{
    private const string ClrFullName = "Elsa.Activities.Http.Activities.HttpEndpoint";

    [Fact]
    public async Task Catalog_exposes_the_declared_runtime_type_while_keeping_the_clr_full_name_key()
    {
        var view = await ListAsync(ClrFullName, RegistryWith(typeof(HttpEndpoint)));

        var descriptor = Assert.Single(view.Activities, activity => activity.ActivityTypeKey == ClrFullName);
        Assert.Equal(HttpEndpoint.ActivityType, descriptor.RuntimeActivityType);
    }

    [Fact]
    public async Task Catalog_leaves_runtime_type_null_for_a_clr_activity_without_a_declaration()
    {
        var view = await ListAsync(typeof(SendHttpRequest).FullName!, RegistryWith(typeof(SendHttpRequest)));

        var descriptor = Assert.Single(view.Activities, activity => activity.ActivityTypeKey == typeof(SendHttpRequest).FullName);
        Assert.Null(descriptor.RuntimeActivityType);
    }

    [Fact]
    public async Task Catalog_leaves_runtime_type_null_when_no_registry_is_available()
    {
        var view = await ListAsync(ClrFullName, typeRegistry: null);

        var descriptor = Assert.Single(view.Activities, activity => activity.ActivityTypeKey == ClrFullName);
        Assert.Null(descriptor.RuntimeActivityType);
    }

    // Production seeds each CLR activity under its canonical alias (the CLR full name); mirror that for the types under test.
    private static IWellKnownTypeRegistry RegistryWith(params Type[] types)
    {
        var registry = SerializationServices.TypeRegistry();
        foreach (var type in types)
            registry.RegisterType(type, type.FullName!);
        return registry;
    }

    private static Task<ActivityAuthoringCatalogView> ListAsync(string activityTypeKey, IWellKnownTypeRegistry? typeRegistry = null)
    {
        var definition = new ActivityDefinition
        {
            Id = $"def-{activityTypeKey}",
            ActivityTypeKey = activityTypeKey,
            Category = "Http",
            DisplayName = activityTypeKey,
        };
        var version = new ActivityDefinitionVersion("1.0.0", definition.Id)
        {
            Id = $"ver-{activityTypeKey}",
            ConsumerKey = "elsa.clr-activity",
            SourceKind = "CLR",
            SourceId = "Elsa.Activities.Http.dll",
        };

        return CatalogReaderFixture.ListAsync(
            [definition],
            [version],
            new NoFeatureAttribution(),
            typeRegistry: typeRegistry);
    }

    private sealed class NoFeatureAttribution : Elsa.Activities.Design.Api.Contracts.IActivityFeatureAttributionResolver
    {
        public ValueTask<string?> ResolveProvidingFeatureAsync(string activityTypeKey, CancellationToken cancellationToken) =>
            ValueTask.FromResult<string?>(null);
    }
}
