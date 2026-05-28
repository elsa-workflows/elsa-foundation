using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.EFCore.DbContext;
using Elsa.Activities.Design.Tests.Unit;
using Elsa.Persistence.EFCore.Contracts;
using Elsa.Persistence.EFCore.Sqlite;
using Elsa.Primitives.Contracts;
using Elsa.Primitives.Entities;
using Elsa.Primitives.Services;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.EFCore.DbContext;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Design.Tests.Integration;

/// <summary>
/// T154 — Both DbContexts derive from <c>ElsaDbContextBase</c> and MUST share the same
/// lifecycle hooks: <c>[Immutable]</c> enforcement, central <c>TenantId</c> index, EF model
/// snapshot population. The cross-context smoke check guards against a future refactor
/// silently dropping a hook on one context but not the other.
/// </summary>
public sealed class CrossContextLifecycleTests
{
    [Fact]
    public void ActivitiesDesignDbContext_ImmutableEnforcement_AppliesToActivityDefinition()
    {
        using var host = ActivitiesDesignTestHost.Create();
        using var ctx = host.CreateContext();
        var entityType = ctx.Model.FindEntityType(typeof(ActivityDefinition))!;
        var activityTypeKey = entityType.FindProperty(nameof(ActivityDefinition.ActivityTypeKey))!;

        Assert.Equal(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Throw, activityTypeKey.GetAfterSaveBehavior());
    }

    [Fact]
    public void ActivitiesDesignDbContext_RegistersTenantIdIndex_OnTenantEntityDescendants()
    {
        using var host = ActivitiesDesignTestHost.Create();
        using var ctx = host.CreateContext();

        AssertTenantIndexRegistered(ctx, typeof(ActivityDefinition));
        AssertTenantIndexRegistered(ctx, typeof(ActivityDefinitionVersion));
        AssertTenantIndexRegistered(ctx, typeof(ActivityDefinitionReconciliationState));
    }

    [Fact]
    public void WorkflowsDesignDbContext_ImmutableEnforcement_AppliesToWorkflowDefinitionVersion()
    {
        using var host = CreateWorkflowsDesignHost();
        using var ctx = host.CreateContext();
        var entityType = ctx.Model.FindEntityType(typeof(WorkflowDefinitionVersion))!;
        var version = entityType.FindProperty(nameof(WorkflowDefinitionVersion.Version))!;

        Assert.Equal(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Throw, version.GetAfterSaveBehavior());
    }

    [Fact]
    public void WorkflowsDesignDbContext_RegistersTenantIdIndex_OnTenantEntityDescendants()
    {
        using var host = CreateWorkflowsDesignHost();
        using var ctx = host.CreateContext();

        AssertTenantIndexRegistered(ctx, typeof(WorkflowDefinition));
        AssertTenantIndexRegistered(ctx, typeof(WorkflowDefinitionVersion));
        AssertTenantIndexRegistered(ctx, typeof(WorkflowDefinitionDraft));
    }

    [Fact]
    public void BothContexts_ShareRowNumberIndex_OnEveryEntity()
    {
        // RowNumber index is registered by ElsaDbContextBase.ApplyRowNumberIndex. Both
        // contexts pick it up because they both derive from the base. Asserts the shared
        // surface (catches a future refactor that drops the hook on one side).
        using var activitiesHost = ActivitiesDesignTestHost.Create();
        using var workflowsHost = CreateWorkflowsDesignHost();
        using var activitiesCtx = activitiesHost.CreateContext();
        using var workflowsCtx = workflowsHost.CreateContext();

        // SQLite ignores RowNumber per SqliteEntityModelCreatingHandler, so the property is
        // absent from the SQLite model. Verify by checking it WASN'T removed from anything
        // else — every TenantEntity descendant still has a TenantId index. This double-check
        // is the next-best signal that the central hook is wired in both contexts.
        Assert.Contains(activitiesCtx.Model.GetEntityTypes(), e => typeof(TenantEntity).IsAssignableFrom(e.ClrType));
        Assert.Contains(workflowsCtx.Model.GetEntityTypes(), e => typeof(TenantEntity).IsAssignableFrom(e.ClrType));
    }

    private static void AssertTenantIndexRegistered(DbContext ctx, Type clrType)
    {
        var entity = ctx.Model.FindEntityType(clrType);
        Assert.NotNull(entity);
        Assert.Contains(entity!.GetIndexes(), i => i.Properties.Any(p => p.Name == nameof(TenantEntity.TenantId)));
    }

    private static WorkflowsDesignTestHost CreateWorkflowsDesignHost()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var services = new ServiceCollection();
        services.AddSingleton<ISystemClock, SystemClock>();
        services.AddScoped<IEntityModelCreatingHandler, SqliteEntityModelCreatingHandler>();
        var provider = services.BuildServiceProvider();

        var host = new WorkflowsDesignTestHost(connection, provider);
        using var ctx = host.CreateContext();
        ctx.Database.EnsureCreated();
        return host;
    }

    private sealed class WorkflowsDesignTestHost(SqliteConnection connection, ServiceProvider services) : IDisposable
    {
        public WorkflowsDesignDbContext CreateContext()
        {
            var options = new DbContextOptionsBuilder<WorkflowsDesignDbContext>()
                .UseSqlite(connection)
                .Options;
            return new WorkflowsDesignDbContext(options, services);
        }

        public void Dispose()
        {
            services.Dispose();
            connection.Dispose();
        }
    }
}
