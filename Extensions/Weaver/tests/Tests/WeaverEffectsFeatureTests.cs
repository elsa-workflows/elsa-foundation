using CShells.Features;
using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Extensions;
using Elsa.Weaver.Services;
using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Models;
using Elsa.Cluster.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Weaver.Tests;

public sealed class WeaverEffectsFeatureTests
{
    [Fact]
    public async Task Legacy_composition_has_no_effects_admission_and_does_not_resolve_a_store_to_discover_it()
    {
        var calls = 0;
        using var root = WeaverTestServices.Create(services =>
            services.SelectWeaverSessionRecordStore(_ => { calls++; throw new InvalidOperationException("Unexpected store resolution."); }));
        using var scope = root.CreateScope();
        scope.ServiceProvider.GetRequiredService<WeaverRegistrationValidator>().Validate();
        Assert.Equal("weaver.effects.not_enabled", (await scope.ServiceProvider.GetRequiredService<WeaverEffectsAdmission>().CheckAsync())?.Code);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Known_memory_effects_need_no_schema_observer_even_if_one_cannot_be_resolved()
    {
        using var root = WeaverTestServices.Create(services =>
        {
            new WeaverEffectsFeature().ConfigureServices(services);
            services.AddScoped<ISchemaDormancyCheck>(_ => throw new InvalidOperationException("Memory needs no observer."));
            services.AddWeaver();
        });
        using var scope = root.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IWeaverToolInvoker>());
        Assert.Null(await scope.ServiceProvider.GetRequiredService<WeaverEffectsAdmission>().CheckAsync());
        Assert.Empty(scope.ServiceProvider.GetRequiredService<WeaverRegistrationValidator>().GetEffectsSelection()!.Requirements);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Explicit_old_or_null_store_selection_is_legacy_only(bool explicitNull, bool effects)
    {
        var calls = 0;
        using var root = WeaverTestServices.Create(services =>
        {
            IWeaverSessionRecordStore Factory(IServiceProvider _) { calls++; throw new InvalidOperationException("Unexpected store resolution."); }
            if (explicitNull) services.SelectWeaverSessionRecordStore(Factory, null);
            else services.SelectWeaverSessionRecordStore(Factory);
            if (effects) new WeaverEffectsFeature().ConfigureServices(services);
        });
        using var scope = root.CreateScope();
        if (effects)
        {
            var error = Assert.Throws<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<IWeaverToolInvoker>());
            Assert.Contains("has not declared effects support", error.Message);
            Assert.Contains("requiredEffectsFeatureType", error.Message);
        }
        else scope.ServiceProvider.GetRequiredService<WeaverRegistrationValidator>().Validate();
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Declared_custom_store_supports_a_specialized_actual_feature_without_resolving_the_factory()
    {
        var calls = 0;
        using var root = WeaverTestServices.Create(services =>
        {
            services.SelectWeaverSessionRecordStore(_ => { calls++; throw new InvalidOperationException("Unexpected store resolution."); }, typeof(ObservedEffectsFeature));
            new StricterEffectsFeature().ConfigureServices(services);
        });
        using var scope = root.CreateScope();
        var selection = scope.ServiceProvider.GetRequiredService<WeaverRegistrationValidator>().GetEffectsSelection()!;
        Assert.Equal(typeof(StricterEffectsFeature), selection.FeatureType);
        Assert.Equal(SchemaVersionRequirement.DeclaredBy(typeof(StricterEffectsFeature)), selection.Requirements);
        var refusal = await scope.ServiceProvider.GetRequiredService<WeaverEffectsAdmission>().CheckAsync();
        Assert.Equal("schema-version-not-finalized", refusal?.Code);
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("wrong-variation")]
    [InlineData("unowned-store")]
    [InlineData("replaced-store")]
    [InlineData("foreign-selection")]
    public void Invalid_effects_composition_fails_before_any_store_or_owner_dispatch(string scenario)
    {
        var calls = 0;
        using var root = WeaverTestServices.Create(services =>
        {
            IWeaverSessionRecordStore Factory(IServiceProvider _) { calls++; throw new InvalidOperationException("Unexpected store resolution."); }
            if (scenario == "wrong-variation") services.SelectWeaverSessionRecordStore(Factory, typeof(ObservedEffectsFeature));
            if (scenario == "unowned-store")
            {
                services.RemoveAll<IWeaverSessionRecordStore>();
                services.AddSingleton<IWeaverSessionRecordStore>(Factory);
            }
            if (scenario == "foreign-selection") services.AddScoped<WeaverEffectsSelection>(_ => new(typeof(WeaverEffectsFeature), []));
            else new WeaverEffectsFeature().ConfigureServices(services);
            if (scenario == "duplicate") new WeaverEffectsFeature().ConfigureServices(services);
            if (scenario == "replaced-store") services.Replace(ServiceDescriptor.Singleton<IWeaverSessionRecordStore>(Factory));
        });
        using var scope = root.CreateScope();
        Assert.Throws<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<IWeaverToolInvoker>());
        Assert.Equal(0, calls);
    }

    [Fact]
    public void A_specialization_cannot_silently_lose_its_bases_direct_schema_declaration()
    {
        using var root = WeaverTestServices.Create(services => new MissingDirectEffectsFeature().ConfigureServices(services));
        using var scope = root.CreateScope();
        var error = Assert.Throws<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<IWeaverToolInvoker>());
        Assert.Contains("must directly repeat", error.Message);
        Assert.Contains(typeof(ObservedEffectsFeature).FullName!, error.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task Missing_or_ambiguous_shared_observation_is_never_satisfaction(int observers)
    {
        using var root = WeaverTestServices.Create(services =>
        {
            new ObservedEffectsFeature().ConfigureServices(services);
            for (var i = 0; i < observers; i++) services.AddScoped<ISchemaDormancyCheck>(_ => new Observations { WriteVersion = "1.0.0" });
        });
        using var scope = root.CreateScope();
        var error = await scope.ServiceProvider.GetRequiredService<WeaverEffectsAdmission>().CheckAsync();
        Assert.Equal("schema-version-not-finalized", error?.Code);
        Assert.Equal(409, error?.StatusCode);
    }

    [Fact]
    public async Task Each_acceptance_uses_the_shared_current_observation_instead_of_caching_a_scoped_answer()
    {
        var observation = new Observations();
        using var root = WeaverTestServices.Create(services =>
        {
            new ObservedEffectsFeature().ConfigureServices(services);
            services.AddScoped<ISchemaDormancyCheck>(_ => observation);
        });
        using var scope = root.CreateScope();
        var admission = scope.ServiceProvider.GetRequiredService<WeaverEffectsAdmission>();
        Assert.Equal("schema-version-not-finalized", (await admission.CheckAsync())?.Code);
        observation.WriteVersion = "1.0.0";
        Assert.Null(await admission.CheckAsync());
        observation.WriteVersion = null;
        Assert.Equal("schema-version-not-finalized", (await admission.CheckAsync())?.Code);
        Assert.Equal(3, observation.Calls);
    }

    [Fact]
    public void A_store_cannot_declare_an_unrelated_feature_type()
    {
        var services = new ServiceCollection();
        Assert.Throws<ArgumentException>(() => services.SelectWeaverSessionRecordStore(_ => throw new InvalidOperationException(), typeof(string)));
        Assert.Empty(services);
    }

    [ShellFeature("WeaverObservedEffectsProbe", DependsOn = new object[] { typeof(WeaverFeature) })]
    [RequiresSchemaVersion("effects.fixture", "1.0.0")]
    public class ObservedEffectsFeature : WeaverEffectsFeature;

    [ShellFeature("WeaverStricterEffectsProbe", DependsOn = new object[] { typeof(WeaverFeature) })]
    [RequiresSchemaVersion("effects.fixture", "1.0.0")]
    [RequiresSchemaVersion("effects.fixture", "2.0.0")]
    public class StricterEffectsFeature : ObservedEffectsFeature;

    [ShellFeature("WeaverMissingDirectEffectsProbe", DependsOn = new object[] { typeof(WeaverFeature) })]
    public class MissingDirectEffectsFeature : ObservedEffectsFeature;

    // The fixture supplies observations; the existing shared rule, not a second version comparator, decides availability.
    private sealed class Observations : ISchemaDormancyCheck
    {
        internal string? WriteVersion { get; set; }
        internal int Calls { get; private set; }
        public SchemaAvailability Evaluate(IEnumerable<SchemaVersionRequirement> requirements) => SchemaDormancyRule.Evaluate(requirements,
            family => new(family, null, ["1.0.0", "2.0.0"], WriteVersion, WriteVersion, false, null, [], null, DateTimeOffset.UnixEpoch));
        public ValueTask<SchemaAvailability> EvaluateAsync(IEnumerable<SchemaVersionRequirement> requirements, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return ValueTask.FromResult(Evaluate(requirements));
        }
        public ValueTask EnsureAvailableAsync(IEnumerable<SchemaVersionRequirement> requirements, string? featureId = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("This fixture exercises the shared evaluation contract.");
        public IReadOnlyList<SchemaFamilyObservation> Observe() => [];
        public ValueTask<IReadOnlyList<SchemaFamilyObservation>> ReadStatusAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(Observe());
    }
}
