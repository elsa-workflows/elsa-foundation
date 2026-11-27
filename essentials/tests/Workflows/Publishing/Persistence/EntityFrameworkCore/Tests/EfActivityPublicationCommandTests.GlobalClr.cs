using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

using static Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Tests.ActivityPublicationTestMaterial;

namespace Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Tests;

public sealed partial class EfActivityPublicationCommandTests
{
    [Fact]
    public async Task Named_tenant_publication_retains_the_exact_global_clr_dependency_after_reopen()
    {
        const string tenant = "tenant-a";
        var source = SourceCommit(Published, clr: true);
        await using (var owner = Open())
            await owner.SourceCommand.ExecuteAsync(source);

        await SeedAsync("tenant-graph", "tenant-graph-draft", tenant);
        var version = source.Publication;
        var dependency = new DependencySpec(version.DefinitionId, version.DefinitionVersionId, version.Version,
            version.TemplateId, version.TemplateHash, "built-in");

        var graph = Commit("tenant-graph", "tenant-graph-draft", operationTenant: tenant, resourceTenant: tenant,
            dependency: dependency);
        await using var defaultScope = GlobalClrSourceServices();
        await using (var author = Open(tenant))
        {
            var access = TestAccess.Scoped(tenant);
            await using var publicationOperation = OpenGlobalClrOperation(defaultScope, tenant);
            var reader = publicationOperation.ServiceProvider.GetRequiredService<IGlobalClrActivityMaterialReader>();
            Assert.NotNull(await ((IActivityDefinitionVersionPublicationStore)author.DesignStores).FindAsync(version.DefinitionVersionId));
            Assert.Null(await author.Templates.FindAsync(version.TemplateId));
            var available = await reader.FindAsync(version);
            Assert.True(available.HasValue);
            Assert.Equal(version.TemplateHash, available.Value.Template.TemplateHash);
            Assert.Equal(version.SourceReferenceId, available.Value.Reference.SourceReferenceId);
            Assert.Null(await author.Templates.FindAsync(version.TemplateId));

            var command = new EfActivityPublicationCommand(author.Receipts, author.Templates, author.SourceReferences,
                author.DesignStores, access, reader);
            await command.ExecuteAsync(graph);
        }

        await using (var reopened = Open(tenant))
        {
            var copy = await reopened.Templates.FindAsync(version.TemplateId);
            Assert.NotNull(copy);
            Assert.Equal(version.TemplateHash, copy.TemplateHash);
            Assert.False(await reopened.Templates.TryDeleteUnreferencedAsync(copy));
            Assert.Null(await reopened.SourceReferences.FindAsync(version.SourceReferenceId));
            Assert.NotNull(await reopened.Templates.FindAsync(graph.ExecutableTemplate.TemplateId));
        }

        await using var other = Open("tenant-b");
        Assert.Null(await other.Templates.FindAsync(version.TemplateId));
        Assert.Null(await other.Templates.FindAsync(graph.ExecutableTemplate.TemplateId));
    }
    [Fact]
    public async Task A_tenant_owned_clr_version_has_no_reconciliation_scope_fallback()
    {
        const string tenant = "tenant-a";
        var source = SourceCommit(Published, clr: true);
        source.Definition.TenantId = source.AuthoringState.TenantId = source.CatalogVersion.TenantId =
            source.Publication.TenantId = source.Layout.TenantId = tenant;
        source = source with { SourceReference = source.SourceReference with { TenantId = tenant } };
        await using (var owner = Open(tenant))
            await owner.SourceCommand.ExecuteAsync(source);

        await using var defaultScope = GlobalClrSourceServices();
        await using var reopened = Open(tenant);
        await using var publicationOperation = OpenGlobalClrOperation(defaultScope, tenant);
        var reader = publicationOperation.ServiceProvider.GetRequiredService<IGlobalClrActivityMaterialReader>();
        Assert.NotNull(await reopened.Templates.FindAsync(source.ExecutableTemplate.TemplateId));
        Assert.Null(await reader.FindAsync(source.Publication));
        source.Publication.TenantId = null;
        Assert.Null(await reader.FindAsync(source.Publication));
    }

    private ServiceProvider GlobalClrSourceServices()
    {
        var services = new ServiceCollection();
        new WorkflowsPublishingFeature().ConfigureServices(services);
        services.AddPersistenceCore();
        services.AddScoped<ActivitiesDesignDbContext>(_ => design.Open<ActivitiesDesignSqliteDbContext>(options => new(options)));
        services.AddScoped<IActivityDefinitionVersionPublicationStore>(provider => new EfActivityDesignStores(
            provider.GetRequiredService<ActivitiesDesignDbContext>(), provider.GetRequiredService<IPersistenceAccessContextAccessor>()));
        services.AddScoped<RuntimeDbContext>(_ => runtime.Open<RuntimeSqliteDbContext>(options => new(options)));
        services.AddScoped<IExecutableActivityTemplateReader>(provider => new EfExecutableActivityTemplateStore(
            provider.GetRequiredService<RuntimeDbContext>(), provider.GetRequiredService<IPersistenceAccessContextAccessor>(),
            ActivityPublicationScope.RecoveryCodec));
        services.AddScoped<IWorkflowExecutableSourceReferenceReader>(provider => new EfWorkflowExecutableSourceReferenceStore(
            provider.GetRequiredService<RuntimeDbContext>(), provider.GetRequiredService<IPersistenceAccessContextAccessor>(),
            ActivityPublicationScope.RecoveryCodec));
        return services.BuildServiceProvider();
    }

    private static AsyncServiceScope OpenGlobalClrOperation(IServiceProvider services, string tenant)
    {
        var operation = services.CreateAsyncScope();
        operation.ServiceProvider.GetRequiredService<IPersistenceAccessContextBinder>()
            .Bind(PersistenceAccessContext.Scoped(new PersistenceScope(tenant)));
        return operation;
    }
}
