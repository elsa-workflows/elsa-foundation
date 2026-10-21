using System.Net;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Design;
using Elsa.Etl.Design.Api;
using Elsa.Etl.Design.Api.Models;
using Elsa.Etl.Runtime.Bindings;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Etl.Tests;

public sealed partial class EtlDesignApiHttpTests
{
    [Fact]
    public async Task Validation_reports_role_and_scope_specific_unavailable_binding_diagnostics_without_a_hash()
    {
        await using var host = await EtlDesignApiTestHost.StartAsync();
        var cases = new (string Id, EtlPipeline Pipeline, string Path, string Tenant, string Partition)[]
        {
            ("wrong-source-role", WithBinding(EtlTestPipelines.Create(), "source", "lookup"), "bindings.source", "tenant-a", "partition-a"),
            ("wrong-lookup-role", WithBinding(EtlTestPipelines.Create(), "lookup", "source"), "bindings.lookup", "tenant-a", "partition-a"),
            ("wrong-output-role", WithBinding(EtlTestPipelines.Create(), "output", "source"), "bindings.output", "tenant-a", "partition-a"),
            ("wrong-errors-role", WithBinding(EtlTestPipelines.Create(), "errors", "source"), "bindings.errors", "tenant-a", "partition-a"),
            ("foreign-tenant", EtlTestPipelines.Create(), "bindings.source", "tenant-b", "partition-a"),
            ("foreign-partition", EtlTestPipelines.Create(), "bindings.source", "tenant-a", "partition-b")
        };

        foreach (var (id, pipeline, expectedPath, tenant, partition) in cases)
        {
            using var create = await host.SendAsync(HttpMethod.Post, "/etl/definitions", new EtlCreateDefinitionRequest(id, pipeline),
                tenant: tenant, partition: partition, permissions: AuthorPermissions);
            Assert.Equal(HttpStatusCode.Created, create.StatusCode);

            using var response = await host.SendAsync(HttpMethod.Post, $"/etl/definitions/{id}/validate",
                new EtlValidateRequest(id, 1), tenant: tenant, partition: partition, permissions: AuthorPermissions);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var validation = await ReadAsync<EtlValidationView>(response);
            Assert.Null(validation.ContentHash);
            Assert.Contains(validation.Diagnostics, diagnostic => diagnostic.Code == "binding.alias.unavailable" && diagnostic.Path == expectedPath);
            if (tenant != "tenant-a" || partition != "partition-a")
                Assert.Equal(new[] { "bindings.source", "bindings.lookup", "bindings.output", "bindings.errors" }, validation.Diagnostics.Select(diagnostic => diagnostic.Path));
            else
                Assert.Single(validation.Diagnostics);
            Assert.All(validation.Diagnostics, diagnostic => Assert.DoesNotContain(Path.GetTempPath(), diagnostic.Message, StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task Missing_binding_catalog_is_provider_unavailable_during_validation_and_new_publication()
    {
        await using var host = await EtlDesignApiTestHost.StartAsync(registerBindingCatalog: false);
        await CreateDefinitionAsync(host, "orders");

        using var response = await host.SendAsync(HttpMethod.Post, "/etl/definitions/orders/validate",
            new EtlValidateRequest("orders", 1), permissions: AuthorPermissions);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        AssertSafeProblem(await ReadAsync<EtlApiProblem>(response), HttpStatusCode.ServiceUnavailable);

        var compiled = new EtlArtifactCompiler().Compile("orders", 1, EtlTestPipelines.Create());
        using var publish = await host.SendAsync(HttpMethod.Post, "/etl/definitions/orders/publish",
            new EtlPublishRequest("orders", 1, 1, compiled.Artifact!.ContentHash), permissions: AuthorPermissions);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, publish.StatusCode);
        AssertSafeProblem(await ReadAsync<EtlApiProblem>(publish), HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Publish_rechecks_binding_approval_but_immutable_replay_does_not_depend_on_current_catalog()
    {
        var availability = new BindingAvailability();
        await using var host = await EtlDesignApiTestHost.StartAsync(configureServices: services =>
        {
            services.AddScoped<IEtlBindingCatalog>(provider => new AvailabilityControlledCatalog(
                provider.GetRequiredService<HostFileBindings>(), availability));
        });
        await CreateDefinitionAsync(host, "orders");
        var validation = await ValidateAsync(host, "orders", expectedRevision: 1);
        var request = new EtlPublishRequest("orders", validation.Revision, validation.DefinitionVersion, validation.ContentHash!);

        // Approval can change between validation and publication; an unapproved artifact is not retained.
        availability.IsAvailable = false;
        using var refused = await host.SendAsync(HttpMethod.Post, "/etl/definitions/orders/publish", request, permissions: AuthorPermissions);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        var refusedProblem = await ReadAsync<EtlApiProblem>(refused);
        Assert.Equal("etl.validation-failed", refusedProblem.Code);
        AssertSafeProblem(refusedProblem, HttpStatusCode.BadRequest);
        using var emptyVersions = await host.SendAsync(HttpMethod.Get, "/etl/definitions/orders/versions", permissions: DefinitionRead);
        Assert.Empty((await ReadAsync<EtlPage<EtlPublishedVersionView>>(emptyVersions)).Items);

        availability.IsAvailable = true;
        using var published = await host.SendAsync(HttpMethod.Post, "/etl/definitions/orders/publish", request, permissions: AuthorPermissions);
        Assert.Equal(HttpStatusCode.OK, published.StatusCode);
        var artifact = await ReadAsync<EtlPublicationView>(published);
        Assert.Equal("published", artifact.Status);
        Assert.Equal(validation.ContentHash, artifact.Version.ContentHash);

        // The exact already-published tuple is immutable and replayable even after host approval is withdrawn.
        availability.IsAvailable = false;
        var queriesBeforeReplay = availability.Queries;
        using var replay = await host.SendAsync(HttpMethod.Post, "/etl/definitions/orders/publish", request, permissions: AuthorPermissions);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var replayedArtifact = await ReadAsync<EtlPublicationView>(replay);
        Assert.Equal("replay", replayedArtifact.Status);
        Assert.Equal(artifact.Version, replayedArtifact.Version);
        Assert.Equal(queriesBeforeReplay, availability.Queries);
    }

    private static EtlPipeline WithBinding(EtlPipeline pipeline, string binding, string alias) => pipeline with
    {
        Bindings = binding switch
        {
            "source" => pipeline.Bindings with { Source = alias },
            "lookup" => pipeline.Bindings with { Lookup = alias },
            "output" => pipeline.Bindings with { Output = alias },
            "errors" => pipeline.Bindings with { Errors = alias },
            _ => throw new ArgumentOutOfRangeException(nameof(binding))
        }
    };

    private sealed class BindingAvailability
    {
        public bool IsAvailable { get; set; } = true;
        public int Queries { get; set; }
    }

    private sealed class AvailabilityControlledCatalog(HostFileBindings inner, BindingAvailability availability) : IEtlBindingCatalog
    {
        public IReadOnlyList<EtlBindingSummary> ListBindings(EtlScope scope, string? afterAlias, string? afterRole, int take) =>
            inner.ListBindings(scope, afterAlias, afterRole, take);

        public bool ContainsBinding(EtlScope scope, string alias, string role)
        {
            availability.Queries++;
            return availability.IsAvailable && inner.ContainsBinding(scope, alias, role);
        }
    }
}
