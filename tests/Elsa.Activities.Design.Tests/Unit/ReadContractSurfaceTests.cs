using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.EFCore.DbContext;
using Elsa.Persistence.Core;
using Elsa.Persistence.EFCore.Extensions;
using Elsa.Persistence.EFCore.Sqlite;
using Elsa.Persistence.EFCore.Contracts;
using Elsa.Primitives.Contracts;
using Elsa.Primitives.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Design.Tests.Unit;

/// <summary>
/// US6 — Read-contract surface pinning. The picker/designer/runtime consumer-set sees
/// <see cref="IActivityDefinition"/> only; operational reconciliation state lives on the
/// separate <see cref="IActivityDefinitionReconciliationState"/> contract. Reflection tests
/// fail loudly if a future refactor leaks reconciliation-state fields onto the parent
/// contract (or vice versa).
/// </summary>
public sealed class ReadContractSurfaceTests
{
    private static readonly string[] ReconciliationStateFieldNames =
    [
        nameof(IActivityDefinitionReconciliationState.SourceVersion),
        nameof(IActivityDefinitionReconciliationState.ProvisioningHash),
        nameof(IActivityDefinitionReconciliationState.LastSeenAt),
        nameof(IActivityDefinitionReconciliationState.LastProvisionedAt),
        nameof(IActivityDefinitionReconciliationState.LastProvisionedBy),
        nameof(IActivityDefinitionReconciliationState.IsStale),
        nameof(IActivityDefinitionReconciliationState.RemovedAt),
    ];

    [Fact]
    public void IActivityDefinition_DoesNotExposeReconciliationStateFields()
    {
        var properties = typeof(IActivityDefinition)
            .GetProperties()
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var fieldName in ReconciliationStateFieldNames)
        {
            Assert.DoesNotContain(fieldName, properties);
        }
    }

    [Fact]
    public void IActivityDefinitionReconciliationState_ExposesAllReconciliationStateFields()
    {
        var properties = typeof(IActivityDefinitionReconciliationState)
            .GetProperties()
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var fieldName in ReconciliationStateFieldNames)
        {
            Assert.Contains(fieldName, properties);
        }
    }

    [Fact]
    public void IActivityDefinition_DoesNotExposeIsBrowsable()
    {
        // IsBrowsable was removed entirely (catalog presence is the picker rule, not a
        // mutable flag). This test pins that removal — a regression that re-adds it would
        // fail here.
        var properties = typeof(IActivityDefinition)
            .GetProperties()
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("IsBrowsable", properties);
    }

    [Fact]
    public void IQueries_OfReconciliationState_IsResolvable_FromDI()
    {
        // The reconciliation-state sibling is a first-class entity reachable via the
        // standard IQueries<TEntity> contract. Audit endpoints and the stale-removal sweep
        // (Unit F) consume it through this surface.
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var services = new ServiceCollection();
        services.AddSingleton<ISystemClock, SystemClock>();
        services.AddScoped<IEntityModelCreatingHandler, SqliteEntityModelCreatingHandler>();
        services.AddDbContextFactory<ActivitiesDesignDbContext>(o => o.UseSqlite(connection));
        services.ConfigureQueries<ActivitiesDesignDbContext>();

        using var provider = services.BuildServiceProvider();
        var queries = provider.GetService<IQueries<ActivityDefinitionReconciliationState>>();

        Assert.NotNull(queries);
    }
}
