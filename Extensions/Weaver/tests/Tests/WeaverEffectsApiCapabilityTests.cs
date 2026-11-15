using Elsa.Api.Capabilities.Contracts;
using Elsa.Api.Capabilities.Extensions;
using Elsa.Api.Capabilities.Models;
using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Models;
using Elsa.Cluster.Core.Services;
using Elsa.Weaver.Api;
using Elsa.Weaver.Api.Extensions;
using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;
using Elsa.Weaver.Extensions;
using Elsa.Weaver.Services;
using Elsa.Api.AspNetCore;
using Elsa.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Weaver.Tests;

public sealed class WeaverEffectsApiCapabilityTests
{
    [Fact]
    public async Task Legacy_api_remains_available_without_advertising_unselected_effects()
    {
        using var root = Build(_ => { });
        using var scope = root.CreateScope();
        var document = await scope.ServiceProvider.GetRequiredService<IApiCapabilityCatalog>().GetAsync();
        Assert.Null(Assert.Single(document.Capabilities, item => item.Id == "elsa.api.weaver").Status);
        Assert.DoesNotContain(document.Capabilities, item => item.Id == "elsa.api.weaver.effects");
    }

    [Fact]
    public async Task Memory_effects_need_neither_an_observer_nor_an_owning_module_or_store_resolution()
    {
        using var root = Build(services =>
        {
            services.SelectWeaverSessionRecordStore(_ => throw new InvalidOperationException("No store read for capabilities."), typeof(WeaverEffectsFeature));
            new WeaverEffectsFeature().ConfigureServices(services);
            services.AddScoped<IWeaverTool>(_ => throw new InvalidOperationException("No tool read for capabilities."));
            services.AddScoped<ISchemaDormancyCheck>(_ => throw new InvalidOperationException("Memory needs no observer."));
        });
        using var scope = root.CreateScope();
        var capability = await Effects(scope.ServiceProvider);
        Assert.Null(capability.Status);
        Assert.Null(capability.Reason);
        Assert.Equal("1", capability.ContractVersion);
        Assert.Equal("_elsa/weaver/bootstrap", Assert.Single(capability.Links, link => link.Rel == "weaver-bootstrap").Href);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Unobserved_or_held_or_ambiguous_observation_matches_actual_selected_feature_admission(int observers)
    {
        var observation = new Observation();
        using var root = Build(services =>
        {
            new WeaverEffectsFeatureTests.ObservedEffectsFeature().ConfigureServices(services);
            for (var index = 0; index < observers; index++) services.AddScoped<ISchemaDormancyCheck>(_ => observation);
        });
        using var scope = root.CreateScope();
        var capability = await Effects(scope.ServiceProvider);
        var actual = await scope.ServiceProvider.GetRequiredService<WeaverEffectsAdmission>().CheckAsync();
        Assert.Equal(ApiCapabilityView.DormantStatus, capability.Status);
        Assert.Equal("schema-version-not-finalized", actual?.Code);
        Assert.Equal(actual?.Message, capability.Reason);
        var document = await scope.ServiceProvider.GetRequiredService<IApiCapabilityCatalog>().GetAsync();
        Assert.Null(Assert.Single(document.Capabilities, item => item.Id == "elsa.api.weaver").Status);
    }

    [Fact]
    public async Task Advertised_effects_links_match_actual_mapped_get_routes()
    {
        using var root = Build(services => new WeaverEffectsFeature().ConfigureServices(services));
        using var scope = root.CreateScope();
        var capability = await Effects(scope.ServiceProvider);
        var routes = new TestEndpointRouteBuilder(scope.ServiceProvider);
        WeaverApi.MapWeaverApi(routes);
        foreach (var link in capability.Links)
            Assert.Contains(routes.DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>(),
                endpoint => endpoint.RoutePattern.RawText == "/" + link.Href
                    && endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.Contains("GET") == true);
    }

    [Fact]
    public async Task Current_shared_observation_changes_availability_without_rebuilding_the_catalog_or_changing_legacy_api()
    {
        var observation = new Observation();
        using var root = Build(services =>
        {
            new WeaverEffectsFeatureTests.ObservedEffectsFeature().ConfigureServices(services);
            services.AddScoped<ISchemaDormancyCheck>(_ => observation);
        });
        using var scope = root.CreateScope();
        Assert.Equal(ApiCapabilityView.DormantStatus, (await Effects(scope.ServiceProvider)).Status);
        observation.Available = true;
        Assert.Null((await Effects(scope.ServiceProvider)).Status);
        observation.Available = false;
        Assert.Equal(ApiCapabilityView.DormantStatus, (await Effects(scope.ServiceProvider)).Status);
        Assert.Equal(3, observation.Reads);
    }

    [Fact]
    public async Task Repeated_api_registration_contributes_only_one_effects_capability()
    {
        using var root = Build(services =>
        {
            new WeaverEffectsFeature().ConfigureServices(services);
            services.AddWeaverApi();
            services.AddWeaverApi();
        });
        using var scope = root.CreateScope();
        var effects = await Effects(scope.ServiceProvider);
        Assert.Null(effects.Status);
        Assert.Equal(1, scope.ServiceProvider.GetServices<IApiCapabilitySource>().Count());
    }

    [Fact]
    public async Task Cancellation_does_not_wait_for_an_uncooperative_shared_refresh_or_advertise_a_late_answer()
    {
        using var stop = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var answer = new TaskCompletionSource<SchemaAvailability>(TaskCreationOptions.RunContinuationsAsynchronously);
        var observation = new Observation { Pending = _ => { entered.SetResult(); return new(answer.Task); } };
        using var root = Build(services =>
        {
            new WeaverEffectsFeatureTests.ObservedEffectsFeature().ConfigureServices(services);
            services.AddScoped<ISchemaDormancyCheck>(_ => observation);
        });
        using var scope = root.CreateScope();
        var pending = scope.ServiceProvider.GetRequiredService<IApiCapabilityCatalog>().GetAsync(stop.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            stop.Cancel();
            var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Equal(stop.Token, error.CancellationToken);
        }
        finally { answer.TrySetResult(observation.Evaluate([])); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Observer_failure_fails_the_document_with_a_localized_domain_exception_and_original_cause(bool alreadyDomainFailure)
    {
        Exception failure = alreadyDomainFailure ? new WeaverContributionException("Already localized effects observation failure.")
            : new IOException("Observer storage unavailable.");
        var observation = new Observation { Pending = _ => ValueTask.FromException<SchemaAvailability>(failure) };
        using var root = Build(services =>
        {
            new WeaverEffectsFeatureTests.ObservedEffectsFeature().ConfigureServices(services);
            services.AddScoped<ISchemaDormancyCheck>(_ => observation);
        });
        using var scope = root.CreateScope();
        var error = await Assert.ThrowsAsync<WeaverContributionException>(() => scope.ServiceProvider.GetRequiredService<IApiCapabilityCatalog>().GetAsync());
        if (alreadyDomainFailure) Assert.Same(failure, error);
        else
        {
            Assert.Same(failure, error.InnerException);
            Assert.Contains("elsa.api.weaver.effects", error.Message);
        }
    }

    private static ServiceProvider Build(Action<IServiceCollection> configure) => WeaverTestServices.Create(services =>
    {
        services.AddRouting().AddElsaEndpoints();
        services.AddApiCapabilities();
        new WeaverApiFeature().ConfigureServices(services);
        configure(services);
    });

    private static async Task<ApiCapabilityView> Effects(IServiceProvider services)
        => Assert.Single((await services.GetRequiredService<IApiCapabilityCatalog>().GetAsync()).Capabilities, item => item.Id == "elsa.api.weaver.effects");

    private sealed class Observation : ISchemaDormancyCheck
    {
        internal bool Available { get; set; }
        internal int Reads { get; private set; }
        internal Func<CancellationToken, ValueTask<SchemaAvailability>>? Pending { get; init; }
        public SchemaAvailability Evaluate(IEnumerable<SchemaVersionRequirement> requirements) => SchemaDormancyRule.Evaluate(requirements,
            family => new(family, null, ["0.0.0", "1.0.0"], Available ? "1.0.0" : "0.0.0", Available ? "1.0.0" : "0.0.0", false, null, [], null, DateTimeOffset.UnixEpoch));
        public ValueTask<SchemaAvailability> EvaluateAsync(IEnumerable<SchemaVersionRequirement> requirements, CancellationToken cancellationToken = default)
        {
            Reads++;
            return Pending is { } pending ? pending(cancellationToken) : ValueTask.FromResult(Evaluate(requirements));
        }
        public ValueTask EnsureAvailableAsync(IEnumerable<SchemaVersionRequirement> requirements, string? featureId = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public IReadOnlyList<SchemaFamilyObservation> Observe() => [];
        public ValueTask<IReadOnlyList<SchemaFamilyObservation>> ReadStatusAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(Observe());
    }
}
