using System.Net;
using System.Net.Http.Json;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Design.Api;
using Elsa.Etl.Design.Api.Models;
using Elsa.Etl.Runtime;
using Elsa.Etl.Runtime.Bindings;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Etl.Tests;

/// <summary>Actual scoped runtime approval through HTTP authoring and SQLite immutable publication.</summary>
public sealed class EtlConnectorBindingPublicationTests
{
    private static readonly string[] Permissions = [EtlDesignPermissions.ManageDefinitions, EtlDesignPermissions.PublishDefinitions, EtlDesignPermissions.ReadBindings];

    [Theory]
    [InlineData("tenant-a")]
    [InlineData(null)]
    public async Task Anonymous_scoped_bindings_publish_without_connections_services(string? tenant)
    {
        var scope = new EtlScope(tenant, "partition-a");
        var options = Bindings(scope);
        await using var host = await EtlDesignApiTestHost.StartAsync(configureServices: services =>
        {
            services.AddScoped(_ => new HostConnectorBindings(new HostFileBindings([]), new HostHttpBindings(options), 32));
            services.AddScoped<IEtlConnectorBindingResolver>(p => p.GetRequiredService<HostConnectorBindings>());
            services.AddScoped<IEtlConnectorBindingCatalog>(p => p.GetRequiredService<HostConnectorBindings>());
        });
        using var create = await host.SendAsync(HttpMethod.Post, "/etl/v2/definitions",
            new EtlConnectorCreateDefinitionRequest("orders", EtlV2TestPipelines.Create()), tenant: tenant, permissions: Permissions);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        using var validationResponse = await host.SendAsync(HttpMethod.Post, "/etl/v2/definitions/orders/validate",
            new EtlValidateRequest("orders", 1), tenant: tenant, permissions: Permissions);
        Assert.Equal(HttpStatusCode.OK, validationResponse.StatusCode);
        var validation = await validationResponse.Content.ReadFromJsonAsync<EtlValidationView>(EtlDesignApiTestHost.JsonOptions);
        Assert.NotNull(validation?.ContentHash);
        var request = new EtlPublishRequest("orders", 1, 1, validation.ContentHash);
        using var publish = await host.SendAsync(HttpMethod.Post, "/etl/v2/definitions/orders/publish", request, tenant: tenant, permissions: Permissions);
        Assert.Equal(HttpStatusCode.OK, publish.StatusCode);

        // Withdraw current approval after publication: replay is immutable and requires no new resolution.
        options.Clear();
        using var replay = await host.SendAsync(HttpMethod.Post, "/etl/v2/definitions/orders/publish", request, tenant: tenant, permissions: Permissions);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal("replay", (await replay.Content.ReadFromJsonAsync<EtlPublicationView>(EtlDesignApiTestHost.JsonOptions))!.Status);
    }

    [Fact]
    public async Task Authenticated_null_tenant_alias_cannot_validate_or_publish_with_an_available_anonymous_catalog()
    {
        var scope = new EtlScope(null, "partition-a");
        var options = Bindings(scope);
        options[0].ConnectionName = "private-source";
        await using var host = await EtlDesignApiTestHost.StartAsync(configureServices: services =>
        {
            services.AddScoped<IEtlConnectorBindingResolver>(_ => new HostConnectorBindings(new HostFileBindings([]), new HostHttpBindings(options), 32));
        });
        using var create = await host.SendAsync(HttpMethod.Post, "/etl/v2/definitions",
            new EtlConnectorCreateDefinitionRequest("orders", EtlV2TestPipelines.Create()), tenant: null, permissions: Permissions);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        using var validate = await host.SendAsync(HttpMethod.Post, "/etl/v2/definitions/orders/validate",
            new EtlValidateRequest("orders", 1), tenant: null, permissions: Permissions);
        Assert.Equal(HttpStatusCode.OK, validate.StatusCode);
        var body = await validate.Content.ReadAsStringAsync();
        var validation = await validate.Content.ReadFromJsonAsync<EtlValidationView>(EtlDesignApiTestHost.JsonOptions);
        Assert.Null(validation!.ContentHash);
        Assert.NotEmpty(validation.Diagnostics);
        Assert.DoesNotContain("private-source", body, StringComparison.Ordinal);
        Assert.DoesNotContain("source.example", body, StringComparison.Ordinal);
        var compiled = new Elsa.Etl.Design.EtlArtifactCompiler().Compile("orders", 1, EtlV2TestPipelines.Create());
        using var publish = await host.SendAsync(HttpMethod.Post, "/etl/v2/definitions/orders/publish",
            new EtlPublishRequest("orders", 1, 1, compiled.Artifact!.ContentHash), tenant: null, permissions: Permissions);
        Assert.Equal(HttpStatusCode.BadRequest, publish.StatusCode);
    }

    private static List<EtlHostHttpBindingOptions> Bindings(EtlScope scope) =>
    [
        new() { TenantId = scope.TenantId, PersistencePartition = scope.PersistencePartition, Alias = "source", Role = "Source",
            Revision = "v1", Endpoint = "https://source.example/data", Shards = [new() { Id = "a", SeedUri = "https://source.example/data" }] },
        new() { TenantId = scope.TenantId, PersistencePartition = scope.PersistencePartition, Alias = "output", Role = "MainOutput",
            Revision = "v1", Endpoint = "https://receiver.example/main" },
        new() { TenantId = scope.TenantId, PersistencePartition = scope.PersistencePartition, Alias = "errors", Role = "ErrorOutput",
            Revision = "v1", Endpoint = "https://receiver.example/errors" }
    ];
}
