using Elsa.Persistence.EntityFramework.Tests;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Persistence.Core.Services;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Exceptions;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Tests;

public sealed partial class EfWorkflowDesignPersistenceTests
{
    [Fact]
    public async Task Template_port_refuses_implicit_replacement_and_allows_absence()
    {
        var options = new WorkflowsDesignEntityFrameworkCoreOptions { Provider = "Sqlite", ConnectionString = "Data Source=:memory:" };
        var early = new ServiceCollection();
        early.AddScoped<IWorkflowTemplateStore>(_ => throw new NotSupportedException());
        Assert.Throws<InvalidOperationException>(() => early.AddWorkflowsDesignEntityFrameworkCore(options));
        var services = new ServiceCollection();
        services.AddWorkflowsDesignEntityFrameworkCore(options);
        services.AddWorkflowsDesignEntityFrameworkCore(options);
        services.AddScoped<IWorkflowTemplateStore>(_ => throw new NotSupportedException());
        var validator = new ValidateDesignPersistenceReplacementContractsStartupTask(services);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => validator.ExecuteAsync(CancellationToken.None));
        Assert.Contains(nameof(IWorkflowTemplateStore), error.Message);
        services.RemoveAll<IWorkflowTemplateStore>();
        await validator.ExecuteAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Templates_survive_restart_reject_stale_edits_and_are_isolated_from_workflows_and_other_tenants()
    {
        await using var database = new TemporarySqliteDatabase("workflow-templates");
        ServiceProvider Host(string tenant)
        {
            var services = new ServiceCollection();
            services.AddSingleton<IPayloadSerializer, TestSerializer>();
            services.AddSingleton<IPersistenceAccessContextAccessor>(new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope(tenant))));
            services.AddWorkflowsDesignEntityFrameworkCore(new() { Provider = "Sqlite", ConnectionString = database.ConnectionString });
            return services.BuildServiceProvider();
        }
        WorkflowTemplate saved;
        var content = new WorkflowTemplateContent(WorkflowDefinitionState.Empty, [], [new("root", "A label")]);
        await using (var host = Host("tenant-a"))
        {
            using var scope = host.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<WorkflowsDesignDbContext>();
            await db.Database.MigrateAsync();
            var store = scope.ServiceProvider.GetRequiredService<IWorkflowTemplateStore>();
            saved = await store.SaveAsync(new("create-template"), null, "Starter", "Description", content, null);
            Assert.Equal(saved.Id, (await store.SaveAsync(new("create-template"), null, "Starter", "Description", content, null)).Id);
            Assert.Single(await store.ListAsync());
            Assert.Equal(0, await db.Definitions.CountAsync());
            Assert.Equal(0, await db.Drafts.CountAsync());
        }
        await using (var otherTenant = Host("tenant-b"))
        {
            using var scope = otherTenant.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IWorkflowTemplateStore>();
            Assert.Empty(await store.ListAsync());
            Assert.Null(await store.FindAsync(saved.Id));
            await Assert.ThrowsAsync<Elsa.Primitives.Exceptions.EntityNotFoundException>(() => store.SaveAsync(new("cross-tenant-update"), saved.Id, "foreign edit", null, content, saved.Revision));
            await Assert.ThrowsAsync<Elsa.Primitives.Exceptions.EntityNotFoundException>(() => store.DeleteAsync(new("cross-tenant-delete"), saved.Id, saved.Revision));
        }
        await using (var host = Host("tenant-a"))
        {
            using var scope = host.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IWorkflowTemplateStore>();
            var reloaded = await store.FindAsync(saved.Id);
            Assert.Equal("A label", Assert.Single(reloaded!.Content.ActivityPresentation).DisplayName);
            Assert.Equal(saved.Revision, reloaded.Revision);
            var updated = await store.SaveAsync(new("update-template"), saved.Id, "Changed", null, content, saved.Revision);
            Assert.NotEqual(saved.Revision, updated.Revision);
            await Assert.ThrowsAsync<WorkflowTemplateConflictException>(() => store.SaveAsync(new("stale-update"), saved.Id, "Lost edit", null, content, saved.Revision));
            await Assert.ThrowsAsync<WorkflowTemplateConflictException>(() => store.DeleteAsync(new("stale-delete"), saved.Id, saved.Revision));
            Assert.Equal("Changed", (await store.FindAsync(saved.Id))!.Name);
            await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(new("builtin-update"), "http", "overwrite", null, content, "old"));
            await Assert.ThrowsAsync<ArgumentException>(() => store.DeleteAsync(new("builtin-delete"), "blank", "old"));
            await store.DeleteAsync(new("delete-template"), saved.Id, updated.Revision);
            await store.DeleteAsync(new("delete-template"), saved.Id, updated.Revision);
            Assert.Null(await store.FindAsync(saved.Id));
        }
        await using (var host = Host("tenant-b"))
        {
            using var scope = host.CreateScope();
            Assert.Empty(await scope.ServiceProvider.GetRequiredService<IWorkflowTemplateStore>().ListAsync());
        }
    }
}
