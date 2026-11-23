using Elsa.Studio.Dashboard.Core.Resources;
using Elsa.Studio.Dashboard.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Studio.Dashboard.ResourceTests;

public sealed class DashboardMetadataSchemaTests
{
    [Fact]
    public async Task Native_writes_stamp_resource_key_and_quota_metadata_with_the_current_declared_schema()
    {
        await using var host = await ResourceHost.CreateAsync();
        await host.With(service => service.CreateAsync(DashboardResourceTests.Actor("owner"), "question", DashboardResourceTests.Question("stamped")));
        await host.WithContext(async context => {
            Assert.Equal(DashboardResourcesEfModule.Chain.CurrentVersion, (await context.Resources.SingleAsync()).SchemaVersion);
            Assert.Equal(DashboardResourcesEfModule.Chain.CurrentVersion, (await context.Operations.SingleAsync()).SchemaVersion);
            Assert.Equal(DashboardResourcesEfModule.Chain.CurrentVersion, (await context.Scopes.SingleAsync()).SchemaVersion);
        });
    }

    [Theory]
    [InlineData("9.0.0")]
    [InlineData("unknown")]
    [InlineData("")]
    public async Task Unreadable_operation_schema_is_refused_before_its_fingerprint_or_resource_reference_is_used(string version)
    {
        await using var host = await ResourceHost.CreateAsync();
        var actor = DashboardResourceTests.Actor("owner");
        var input = DashboardResourceTests.Question("operation-schema");
        var saved = await host.With(service => service.CreateAsync(actor, "question", input));
        string fence = "";
        await host.WithContext(async context => {
            fence = (await context.Scopes.SingleAsync()).Revision;
            await context.Operations.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.SchemaVersion, version)
                .SetProperty(row => row.Fingerprint, "not-this-fingerprint").SetProperty(row => row.ResourceKey, "not-a-resource-reference"));
        });
        Assert.Equal("schemaIncompatible", (await Assert.ThrowsAsync<DashboardResourceException>(() => host.With(service => service.CreateAsync(actor, "question", input)))).Code);
        await host.WithContext(async context => {
            var operation = await context.Operations.SingleAsync();
            Assert.Equal(version, operation.SchemaVersion); Assert.Equal("not-this-fingerprint", operation.Fingerprint);
            Assert.Equal("not-a-resource-reference", operation.ResourceKey);
            Assert.Equal(fence, (await context.Scopes.SingleAsync()).Revision);
            Assert.Equal(saved.Revision, (await context.Resources.SingleAsync()).Revision);
        });
    }

    [Theory]
    [InlineData("9.0.0")]
    [InlineData("unknown")]
    [InlineData("")]
    public async Task Unreadable_fence_schema_refuses_new_keyed_create_and_CAS_without_restamping_or_changing_any_row(string version)
    {
        await using var host = await ResourceHost.CreateAsync();
        var actor = DashboardResourceTests.Actor("owner");
        var saved = await host.With(service => service.CreateAsync(actor, "question", DashboardResourceTests.Question("before-future-fence")));
        string fence = "";
        await host.WithContext(async context => {
            fence = (await context.Scopes.SingleAsync()).Revision;
            await context.Scopes.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.SchemaVersion, version));
        });
        Assert.Equal("schemaIncompatible", (await Assert.ThrowsAsync<DashboardResourceException>(() => host.With(service =>
            service.CreateAsync(actor, "question", DashboardResourceTests.Question("new-key"))))).Code);
        Assert.Equal("schemaIncompatible", (await Assert.ThrowsAsync<DashboardResourceException>(() => host.With(service =>
            service.UpdateAsync(actor, "question", saved.Id, saved.Revision, new() { Name = "Must not save", PreserveDefinition = true })))).Code);
        await host.WithContext(async context => {
            var scope = await context.Scopes.SingleAsync();
            Assert.Equal(version, scope.SchemaVersion); Assert.Equal(fence, scope.Revision);
            Assert.Single(await context.Operations.ToArrayAsync());
            Assert.Equal(saved.Revision, (await context.Resources.SingleAsync()).Revision);
        });
    }
}
