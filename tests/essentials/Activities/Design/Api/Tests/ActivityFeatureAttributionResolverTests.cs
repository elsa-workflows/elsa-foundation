using CShells.Features;
using Elsa.Activities.Design.Api.Services;
using Elsa.Serialization.Core;
using Xunit;

namespace Elsa.Activities.Design.Api.Tests;

/// <summary>
/// Unit tests for <see cref="ActivityFeatureAttributionResolver"/>: activity type key → live CLR type
/// (well-known type registry) → assembly → providing shell feature (runtime feature catalog). Every
/// unresolvable step degrades to null attribution instead of failing.
/// </summary>
public sealed class ActivityFeatureAttributionResolverTests
{
    private const string ActivityTypeKey = "Acme.Activities.Noop";

    [Fact]
    public async Task Resolves_the_feature_whose_startup_type_shares_the_activity_types_assembly()
    {
        // FixtureActivity and FixtureFeature live in this test assembly, so the feature owns the activity.
        var resolver = new ActivityFeatureAttributionResolver(
            RegistryWith(ActivityTypeKey, typeof(FixtureActivity)),
            CatalogWith(new ShellFeatureDescriptor("AcmeActivities") { StartupType = typeof(FixtureFeature) }));

        var featureId = await resolver.ResolveProvidingFeatureAsync(ActivityTypeKey, CancellationToken.None);

        Assert.Equal("AcmeActivities", featureId);
    }

    [Fact]
    public async Task Unknown_alias_resolves_to_null()
    {
        var resolver = new ActivityFeatureAttributionResolver(
            RegistryWith(ActivityTypeKey, typeof(FixtureActivity)),
            CatalogWith(new ShellFeatureDescriptor("AcmeActivities") { StartupType = typeof(FixtureFeature) }));

        Assert.Null(await resolver.ResolveProvidingFeatureAsync("Unknown.Alias", CancellationToken.None));
    }

    [Fact]
    public async Task Missing_dependencies_degrade_to_null_attribution()
    {
        var resolver = new ActivityFeatureAttributionResolver();

        Assert.Null(await resolver.ResolveProvidingFeatureAsync(ActivityTypeKey, CancellationToken.None));
    }

    [Fact]
    public async Task No_feature_in_the_types_assembly_resolves_to_null()
    {
        // The only descriptor's startup type lives in the framework assembly, not this test assembly.
        var resolver = new ActivityFeatureAttributionResolver(
            RegistryWith(ActivityTypeKey, typeof(FixtureActivity)),
            CatalogWith(new ShellFeatureDescriptor("SomewhereElse") { StartupType = typeof(string) }));

        Assert.Null(await resolver.ResolveProvidingFeatureAsync(ActivityTypeKey, CancellationToken.None));
    }

    [Fact]
    public async Task Descriptors_without_a_startup_type_are_skipped()
    {
        var resolver = new ActivityFeatureAttributionResolver(
            RegistryWith(ActivityTypeKey, typeof(FixtureActivity)),
            CatalogWith(new ShellFeatureDescriptor("NoStartupType")));

        Assert.Null(await resolver.ResolveProvidingFeatureAsync(ActivityTypeKey, CancellationToken.None));
    }

    [Fact]
    public async Task Multiple_features_in_one_assembly_deterministically_attribute_to_the_first_ordinal_id()
    {
        var resolver = new ActivityFeatureAttributionResolver(
            RegistryWith(ActivityTypeKey, typeof(FixtureActivity)),
            CatalogWith(
                new ShellFeatureDescriptor("ZuluFeature") { StartupType = typeof(FixtureFeature) },
                new ShellFeatureDescriptor("AlphaFeature") { StartupType = typeof(SecondFixtureFeature) }));

        Assert.Equal("AlphaFeature", await resolver.ResolveProvidingFeatureAsync(ActivityTypeKey, CancellationToken.None));
    }

    private static IWellKnownTypeRegistry RegistryWith(string alias, Type type) =>
        new StubTypeRegistry(new Dictionary<string, Type>(StringComparer.Ordinal) { [alias] = type });

    private static IRuntimeFeatureCatalog CatalogWith(params ShellFeatureDescriptor[] descriptors) =>
        new FakeRuntimeFeatureCatalog(descriptors);

    private sealed class FixtureActivity;

    private sealed class FixtureFeature;

    private sealed class SecondFixtureFeature;

    private sealed class StubTypeRegistry(IReadOnlyDictionary<string, Type> typesByAlias) : IWellKnownTypeRegistry
    {
        public void RegisterType(Type type, string alias) => throw new NotSupportedException();

        public bool TryGetAlias(Type type, out string alias)
        {
            alias = typesByAlias.FirstOrDefault(pair => pair.Value == type).Key!;
            return alias is not null;
        }

        public bool TryGetType(string alias, out Type type) => typesByAlias.TryGetValue(alias, out type!);

        public IEnumerable<Type> ListTypes() => typesByAlias.Values;

        public string GetAliasOrDefault(Type type) => TryGetAlias(type, out var alias) ? alias : type.FullName!;

        public Type GetTypeOrDefault(string alias) => TryGetType(alias, out var type) ? type : typeof(object);

        public bool TryGetTypeOrDefault(string alias, out Type type) => TryGetType(alias, out type);
    }

    private sealed class FakeRuntimeFeatureCatalog(ShellFeatureDescriptor[] descriptors) : IRuntimeFeatureCatalog
    {
        public Task<RuntimeFeatureCatalogSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Build());

        public Task<RuntimeFeatureCatalogSnapshot> RefreshAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Build());

        private RuntimeFeatureCatalogSnapshot Build()
        {
            var map = new Dictionary<string, ShellFeatureDescriptor>(StringComparer.OrdinalIgnoreCase);
            foreach (var descriptor in descriptors)
                if (!string.IsNullOrWhiteSpace(descriptor.Id))
                    map[descriptor.Id] = descriptor;

            return new RuntimeFeatureCatalogSnapshot(1, [], descriptors, map, DateTimeOffset.UnixEpoch);
        }
    }
}
