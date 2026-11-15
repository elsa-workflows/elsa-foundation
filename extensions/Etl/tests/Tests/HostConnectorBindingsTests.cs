using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Runtime;
using Elsa.Etl.Runtime.Bindings;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Etl.Tests;

public sealed class HostConnectorBindingsTests
{
    private static readonly EtlScope Scope = new("tenant-a", "partition-a");

    [Fact]
    public async Task Real_runtime_composition_resolves_mixed_kinds_without_exposing_http_to_v1()
    {
        var feature = Feature();
        using var provider = Compose(feature);
        using var serviceScope = provider.CreateScope();
        var services = serviceScope.ServiceProvider;
        var catalog = services.GetRequiredService<IEtlConnectorBindingCatalog>();
        var resolver = services.GetRequiredService<IEtlConnectorBindingResolver>();
        var pipeline = EtlV2TestPipelines.Create() with { Routes = new(new EtlHttpDestination("output"), new EtlJsonlDestination("errors")) };
        var result = await resolver.ResolveAsync(Scope, pipeline);
        Assert.Null(result.FailureCode);
        Assert.NotNull(result.Bindings);
        Assert.Equal(EtlConnectorKind.Http, result.Bindings.Source.Kind);
        Assert.Equal(EtlConnectorKind.Jsonl, result.Bindings.Errors.Kind);
        Assert.Equal(["shard-a"], result.Bindings.HttpShardIds);
        Assert.Null(result.Bindings.Lookup);
        Assert.False(services.GetRequiredService<IEtlBindingCatalog>().ContainsBinding(Scope, "source", "Source"));
        var entries = catalog.ListBindings(Scope, null, null, 100);
        Assert.Equal(["errors", "output", "source"], entries.Select(x => x.Alias));
        Assert.Equal(entries.Skip(1), catalog.ListBindings(Scope, "errors", "ErrorOutput", 100));
        Assert.Empty(catalog.ListBindings(new("tenant-b", "partition-a"), null, null, 100));
        Assert.Equal("etl.binding-unavailable", (await resolver.ResolveAsync(new("tenant-a", "partition-b"), pipeline)).FailureCode);
    }

    [Fact]
    public async Task One_source_file_approval_pins_the_format_each_pipeline_declares()
    {
        var feature = Feature();
        feature.FileBindings.Add(new()
        {
            TenantId = Scope.TenantId, PersistencePartition = Scope.PersistencePartition, Alias = "orders-file", Role = "Source",
            Revision = "v7", Path = Path.Combine(Path.GetTempPath(), "etl-host-connector-orders"), ImmutableInput = true,
            ExpectedSha256 = new string('a', 64)
        });
        using var provider = Compose(feature);
        using var serviceScope = provider.CreateScope();
        var resolver = serviceScope.ServiceProvider.GetRequiredService<IEtlConnectorBindingResolver>();
        var routes = new EtlRoutes(new EtlHttpDestination("output"), new EtlJsonlDestination("errors"));
        var csv = EtlV2TestPipelines.Create() with
        {
            Source = new EtlCsvSource("orders-file", [new EtlColumn("name", EtlColumnType.String, true)]), Routes = routes
        };
        var jsonl = csv with { Source = new EtlJsonlSource("orders-file", [new EtlJsonField("name", "/name", EtlColumnType.String, true)]) };

        var csvPin = (await resolver.ResolveAsync(Scope, csv)).Bindings!.Source;
        var jsonlPin = (await resolver.ResolveAsync(Scope, jsonl)).Bindings!.Source;

        Assert.Equal(EtlConnectorKind.Csv, csvPin.Kind);
        Assert.Equal(EtlConnectorKind.Jsonl, jsonlPin.Kind);
        Assert.Equal(csvPin.Revision, jsonlPin.Revision);
        Assert.Equal(new string('a', 64), jsonlPin.Revision.ExpectedSha256);
        // Clients keep seeing source file approvals under the csv kind; the pin carries the declared format.
        var entry = Assert.Single(serviceScope.ServiceProvider.GetRequiredService<IEtlConnectorBindingCatalog>()
            .ListBindings(Scope, null, null, 100), x => x.Alias == "orders-file");
        Assert.Equal(EtlConnectorKind.Csv, entry.Kind);
    }

    [Fact]
    public async Task A_csv_destination_pins_the_output_folder_approval_as_csv_without_a_digest()
    {
        using var provider = Compose(Feature());
        using var serviceScope = provider.CreateScope();
        var resolver = serviceScope.ServiceProvider.GetRequiredService<IEtlConnectorBindingResolver>();
        var pipeline = EtlV2TestPipelines.Create() with { Routes = new(new EtlHttpDestination("output"), new EtlCsvDestination("errors")) };

        var errors = (await resolver.ResolveAsync(Scope, pipeline)).Bindings!.Errors;

        Assert.Equal(EtlConnectorKind.Csv, errors.Kind);
        Assert.Null(errors.Revision.ExpectedSha256);
        Assert.Equal(EtlConnectorKind.Jsonl, Assert.Single(serviceScope.ServiceProvider.GetRequiredService<IEtlConnectorBindingCatalog>()
            .ListBindings(Scope, null, null, 100), x => x.Alias == "errors").Kind);
    }

    [Fact]
    public void Current_alias_role_collisions_are_refused_during_host_composition()
    {
        var feature = Feature();
        feature.HttpBindings.Add(new EtlHostHttpBindingOptions
        {
            TenantId = Scope.TenantId, PersistencePartition = Scope.PersistencePartition, Alias = "errors",
            Role = "ErrorOutput", Revision = "v1", Endpoint = "https://receiver.example/errors"
        });
        Assert.Throws<InvalidOperationException>(() => feature.ConfigureServices(new ServiceCollection()));
    }

    [Fact]
    public async Task Composed_registry_snapshots_mutable_options_and_enforces_shard_ceiling()
    {
        var feature = Feature();
        using var provider = Compose(feature);
        feature.HttpBindings[0].Endpoint = "https://changed.example/data";
        feature.HttpBindings[0].Shards.Clear();
        feature.FileBindings.Clear();
        using var serviceScope = provider.CreateScope();
        var resolver = serviceScope.ServiceProvider.GetRequiredService<IEtlConnectorBindingResolver>();
        var pipeline = EtlV2TestPipelines.Create() with { Routes = new(new EtlHttpDestination("output"), new EtlJsonlDestination("errors")) };
        var first = await resolver.ResolveAsync(Scope, pipeline);
        Assert.NotNull(first.Bindings);
        Assert.Equal(first.Bindings, (await resolver.ResolveAsync(Scope, pipeline)).Bindings! with { HttpShardIds = first.Bindings.HttpShardIds });

        feature = Feature();
        feature.HttpBindings[0].Shards.Add(new() { Id = "shard-b", SeedUri = "https://source.example/data?shard=b" });
        feature.MaxPartitions = 1;
        using var boundedProvider = Compose(feature);
        using var boundedScope = boundedProvider.CreateScope();
        var limited = await boundedScope.ServiceProvider.GetRequiredService<IEtlConnectorBindingResolver>().ResolveAsync(Scope, pipeline);
        Assert.Null(limited.Bindings);
        Assert.Equal("etl.http-shard-limit", limited.FailureCode);
    }

    [Fact]
    public async Task Wrong_connector_kind_is_not_approved_by_a_matching_alias_and_role()
    {
        using var provider = Compose(Feature());
        using var serviceScope = provider.CreateScope();
        var result = await serviceScope.ServiceProvider.GetRequiredService<IEtlConnectorBindingResolver>().ResolveAsync(Scope,
            EtlV2TestPipelines.Create()); // errors is approved only as JSONL, not HTTP.
        Assert.Null(result.Bindings);
        Assert.Equal("etl.binding-unavailable", result.FailureCode);
    }

    private static ServiceProvider Compose(EtlRuntimeFeature feature)
    {
        var services = new ServiceCollection();
        feature.ConfigureServices(services);
        return services.BuildServiceProvider();
    }

    private static EtlRuntimeFeature Feature() => new()
    {
        SnapshotRoot = Path.Combine(Path.GetTempPath(), "etl-host-connector-test"),
        FileBindings = [new()
        {
            TenantId = Scope.TenantId, PersistencePartition = Scope.PersistencePartition, Alias = "errors", Role = "ErrorOutput",
            Revision = "v1", Path = Path.Combine(Path.GetTempPath(), "etl-host-connector-errors"), AtomicPublication = true
        }],
        HttpBindings = [new()
        {
            TenantId = Scope.TenantId, PersistencePartition = Scope.PersistencePartition, Alias = "source", Role = "Source",
            Revision = "v1", Endpoint = "https://source.example/data", Shards = [new() { Id = "shard-a", SeedUri = "https://source.example/data?shard=a" }]
        }, new()
        {
            TenantId = Scope.TenantId, PersistencePartition = Scope.PersistencePartition, Alias = "output", Role = "MainOutput",
            Revision = "v1", Endpoint = "https://receiver.example/receive"
        }]
    };
}
