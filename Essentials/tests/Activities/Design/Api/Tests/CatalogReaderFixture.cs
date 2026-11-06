using Elsa.Activities.Design.Api.Contracts;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Api.Requests;
using Elsa.Activities.Design.Api.Services;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Stores;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Filters;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Serialization.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Design.Api.Tests;

/// <summary>
/// Drives the real <see cref="ActivityAuthoringCatalogReader"/> over an in-memory catalog, with the built-in
/// engine-intrinsic descriptors composed. Unless a test passes its own resolver, availability evaluator and settings
/// store, no feature is attributed and every activity is addable.
/// </summary>
internal static class CatalogReaderFixture
{
    public static async Task<ActivityAuthoringCatalogView> ListAsync(
        IReadOnlyList<ActivityDefinition> definitions,
        IReadOnlyList<ActivityDefinitionVersion> versions,
        IActivityFeatureAttributionResolver? resolver = null,
        IActivityAvailabilityEvaluator? availabilityEvaluator = null,
        IActivityAvailabilitySettingsStore? settingsStore = null,
        IWellKnownTypeRegistry? wellKnownTypes = null,
        bool resolveFromServices = false)
    {
        var definitionsStore = new InMemoryDefinitionStore(definitions);
        var versionsStore = new InMemoryVersionStore(versions);
        var evaluator = availabilityEvaluator ?? new AllAddableEvaluator();
        var settings = settingsStore ?? new NullSettingsStore();
        var attribution = resolver ?? new NoFeatureAttribution();
        if (!resolveFromServices)
            return await new ActivityAuthoringCatalogReader(definitionsStore, versionsStore, evaluator, settings,
                [new IntrinsicAuthoringDescriptorProvider()], attribution, wellKnownTypes)
                .ListAsync(new ListActivityAuthoringCatalog(), CancellationToken.None);

        var services = new ServiceCollection()
            .AddSingleton<IActivityDefinitionStore>(definitionsStore)
            .AddSingleton<IActivityDefinitionVersionStore>(versionsStore)
            .AddSingleton<IActivityAvailabilityEvaluator>(evaluator)
            .AddSingleton<IActivityAvailabilitySettingsStore>(settings)
            .AddSingleton<IBuiltInAuthoringDescriptorProvider, IntrinsicAuthoringDescriptorProvider>()
            .AddSingleton<IActivityFeatureAttributionResolver>(attribution)
            .AddScoped<ActivityAuthoringCatalogReader>();
        if (wellKnownTypes is not null) services.AddSingleton(wellKnownTypes);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ActivityAuthoringCatalogReader>()
            .ListAsync(new ListActivityAuthoringCatalog(), CancellationToken.None);
    }

    private sealed class InMemoryDefinitionStore(IReadOnlyList<ActivityDefinition> definitions) : IActivityDefinitionStore
    {
        public Task<ActivityDefinition> GetAsync(string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ActivityDefinition?> FindAsync(ActivityDefinitionFilter filter, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ActivityDefinition>> ListAsync(ActivityDefinitionFilter filter, CancellationToken cancellationToken = default) => Task.FromResult(definitions);
        public Task<ActivityDefinition?> FindByIdOrActivityTypeKeyAsync(string id, string activityTypeKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsByActivityTypeKeyAsync(string activityTypeKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class InMemoryVersionStore(IReadOnlyList<ActivityDefinitionVersion> versions) : IActivityDefinitionVersionStore
    {
        public Task<ActivityDefinitionVersion> GetAsync(string versionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ActivityDefinitionVersion> GetWithDefinitionAsync(string versionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ActivityDefinitionVersion?> FindByDefinitionAndSortKeyAsync(string definitionId, string semVerSortKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ActivityDefinitionVersion>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ActivityDefinitionVersion>> ListByDefinitionIdsAsync(IEnumerable<string> definitionIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ActivityDefinitionVersion>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult(versions);
    }

    private sealed class NoFeatureAttribution : IActivityFeatureAttributionResolver
    {
        public ValueTask<string?> ResolveProvidingFeatureAsync(string activityTypeKey, CancellationToken cancellationToken) =>
            ValueTask.FromResult<string?>(null);
    }

    private sealed class AllAddableEvaluator : IActivityAvailabilityEvaluator
    {
        public IReadOnlyCollection<IActivityDefinition> FilterAddable(IEnumerable<IActivityDefinition> activities, ActivityAvailabilitySettings? managementSettings = null) =>
            activities.ToArray();
    }

    private sealed class NullSettingsStore : IActivityAvailabilitySettingsStore
    {
        public Task<ActivityAvailabilitySettings?> LoadAsync(string scope, CancellationToken cancellationToken = default) => Task.FromResult<ActivityAvailabilitySettings?>(null);
        public Task SaveAsync(ActivityAvailabilitySettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
