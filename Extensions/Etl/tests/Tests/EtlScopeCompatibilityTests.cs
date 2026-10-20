using Elsa.Etl.Core;
using Elsa.Etl.Design;
using Elsa.Etl.Design.Stores;
using Elsa.Etl.Persistence.EntityFrameworkCore;
using Elsa.Etl.Persistence.EntityFrameworkCore.Stores;
using Elsa.Etl.Runtime.Bindings;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Etl.Tests;

public sealed class EtlScopeCompatibilityTests
{
    [Fact]
    public async Task Null_tenant_draft_and_publication_roundtrip_without_colliding_with_named_tenants()
    {
        await using var database = await EtlDesignTestDatabase.CreateAsync();
        var nullTenant = new EtlScope(null, "partition-a");
        var literalNull = new EtlScope("null", "partition-a");
        var literalDefault = new EtlScope("default", "partition-a");
        var otherPartition = new EtlScope(null, "partition-b");
        var nullPipeline = EtlTestPipelines.Create("null-tenant");
        var updatedNullPipeline = EtlTestPipelines.Create("updated-null-tenant");
        var namedPipeline = EtlTestPipelines.Create("named-null-tenant");
        var defaultPipeline = EtlTestPipelines.Create("default-tenant");

        await using (var context = database.CreateContext())
        {
            var store = Store(context);
            var created = await store.CreateDraftAsync(nullTenant, "orders", nullPipeline);
            Assert.Equal(EtlDefinitionWriteStatus.Created, created.Status);
            Assert.Equal(EtlPublicationStatus.Published,
                (await store.PublishAsync(nullTenant, "orders", created.Draft!.Revision, Compile("orders", 1, nullPipeline))).Status);
            Assert.Equal(EtlDefinitionWriteStatus.Updated,
                (await store.UpdateDraftAsync(nullTenant, "orders", created.Draft.Revision, updatedNullPipeline)).Status);

            var namedNullDraft = await store.CreateDraftAsync(literalNull, "orders", namedPipeline);
            var defaultDraft = await store.CreateDraftAsync(literalDefault, "orders", defaultPipeline);
            Assert.Equal(EtlDefinitionWriteStatus.Created, namedNullDraft.Status);
            Assert.Equal(EtlDefinitionWriteStatus.Created, defaultDraft.Status);
            Assert.Null(await store.FindDraftAsync(otherPartition, "orders"));
            Assert.Null(await store.FindPublishedAsync(otherPartition, "orders", 1));
            Assert.Null(await store.FindPublishedAsync(literalNull, "orders", 1));
            Assert.Null(await store.FindPublishedAsync(literalDefault, "orders", 1));
        }

        await using (var context = database.CreateContext())
        {
            var store = Store(context);
            var nullDraft = Assert.IsType<EtlDefinitionDraft>(await store.FindDraftAsync(nullTenant, "orders"));
            var namedNullDraft = Assert.IsType<EtlDefinitionDraft>(await store.FindDraftAsync(literalNull, "orders"));
            var defaultDraft = Assert.IsType<EtlDefinitionDraft>(await store.FindDraftAsync(literalDefault, "orders"));
            var published = Assert.IsType<EtlArtifact>(await store.FindPublishedAsync(nullTenant, "orders", 1));

            Assert.Equal("updated-null-tenant", nullDraft.Pipeline.Mapping.Single().Name);
            Assert.Equal("named-null-tenant", namedNullDraft.Pipeline.Mapping.Single().Name);
            Assert.Equal("default-tenant", defaultDraft.Pipeline.Mapping.Single().Name);
            Assert.Equal(nullPipeline.Mapping.Single().Name, EtlArtifactJson.Read(published.Pipeline).Mapping.Single().Name);
            Assert.Null((await context.Drafts.SingleAsync(row => row.TenantId == null)).TenantId);
            Assert.Null((await context.PublishedArtifacts.SingleAsync(row => row.TenantId == null)).TenantId);
            Assert.Null(await store.FindDraftAsync(otherPartition, "orders"));
            Assert.Null(await store.FindPublishedAsync(literalNull, "orders", 1));
            Assert.Null(await store.FindPublishedAsync(literalDefault, "orders", 1));
        }
    }

    [Fact]
    public void Scope_and_host_file_binding_equality_preserve_null_as_its_own_tenant_address()
    {
        var nullTenant = new EtlScope(null, "partition-a");
        var equivalentNullTenant = new EtlScope(null, "partition-a");
        var literalNull = new EtlScope("null", "partition-a");
        var literalDefault = new EtlScope("default", "partition-a");
        var registry = new HostFileBindings(
        [
            Output(nullTenant),
            Output(literalNull),
            Output(literalDefault)
        ]);

        Assert.Equal(nullTenant, equivalentNullTenant);
        Assert.NotEqual(nullTenant, literalNull);
        Assert.NotEqual(nullTenant, literalDefault);
        Assert.Null(registry.Resolve(equivalentNullTenant, "output", EtlFileRole.MainOutput).Scope.TenantId);
        Assert.Equal("null", registry.Resolve(literalNull, "output", EtlFileRole.MainOutput).Scope.TenantId);
        Assert.Equal("default", registry.Resolve(literalDefault, "output", EtlFileRole.MainOutput).Scope.TenantId);
        Assert.Throws<ArgumentException>(() => new EtlScope(string.Empty, "partition-a"));
        Assert.Throws<ArgumentException>(() => new EtlScope(null, string.Empty));
    }

    private static HostFileBinding Output(EtlScope scope) =>
        new(scope, "output", EtlFileRole.MainOutput, "v1", Path.GetTempPath(), IsCurrent: true, AtomicPublication: true);

    private static EfEtlDefinitionStore Store(EtlDesignDbContext context) => new(context, new EtlArtifactCompiler());

    private static EtlArtifact Compile(string definitionId, int version, EtlPipeline pipeline) =>
        Assert.IsType<EtlArtifact>(new EtlArtifactCompiler().Compile(definitionId, version, pipeline).Artifact);
}
