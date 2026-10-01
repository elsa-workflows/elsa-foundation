using Elsa.Workflows.Design.Persistence.Core.Constants;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Commands;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Infrastructure;

// Shared by the SQLite tests and the native provider tests, which link this file.

/// <summary>
/// Updates and deletes rows whose LastModifiedAt is their concurrency token (#2204). A provider that stores or reads the
/// timestamp at a lower precision than the 100 ns ticks the application holds fails the check with "expected 1 row,
/// affected 0". It fails differently for a row this scope materialized, whose original value still has every tick, and
/// for one a fresh scope read back, so each scenario runs both ways.
/// </summary>
internal static class ConcurrencyTokenScenarios
{
    // Ticks below a microsecond: a provider that keeps less than the full 100 ns resolution cannot return this exactly.
    private static readonly DateTimeOffset Precise = new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero).AddTicks(1_234_567);

    /// <summary>Saves a definition twice and then deletes it permanently, through the commands the reconciler and the API use.</summary>
    public static async Task UpdateAndDeleteDefinitionAsync(Func<WorkflowsDesignDbContext> createContext, bool sameScope)
    {
        var tenant = $"token-tenant-{Guid.NewGuid():N}";
        var definitionId = $"token-definition-{Guid.NewGuid():N}";
        var access = new FixedAccess(PersistenceAccessContext.Scoped(new PersistenceScope(tenant)));
        await using var scopes = await TestScopes.CreateAsync(createContext, sameScope);

        await scopes.RunAsync(context => new EfMaterializeWorkflowDefinitionCommand(context, access, new EfDesignAtomicWriter(context, access)).Execute(
            WorkflowReconciliationOperationKeys.Definition(definitionId),
            new WorkflowDefinition { Id = definitionId, Name = "Materialized", IsSourceOwned = true }));
        await scopes.RunAsync(context => new EfSaveWorkflowDefinitionCommand(context, access, new EfDesignAtomicWriter(context, access)).Execute(
            WorkflowReconciliationOperationKeys.DefinitionMetadataWrite(definitionId),
            new WorkflowDefinition { Id = definitionId, Name = "Renamed", IsSourceOwned = true }));
        await scopes.RunAsync(context => new EfSaveWorkflowDefinitionCommand(context, access, new EfDesignAtomicWriter(context, access)).Execute(
            WorkflowReconciliationOperationKeys.DefinitionMetadataWrite(definitionId),
            new WorkflowDefinition { Id = definitionId, Name = "Renamed", DeletedAt = DateTimeOffset.UtcNow, IsSourceOwned = true }));
        await scopes.RunAsync(context => new EfDeleteWorkflowDefinitionPermanentlyCommand(context, access, new EfDesignAtomicWriter(context, access), [new NeverPublishedGuard()])
            .Execute(new DesignOperationKey($"token-permanent-delete-{Guid.NewGuid():N}"), definitionId));

        await using var reopened = createContext();
        Assert.Null(await new EfWorkflowDefinitionStore(reopened, access).FindByIdAsync(definitionId));
    }

    /// <summary>Updates and then deletes one row of every entity that carries the token, with the ticks of <see cref="Precise"/>.</summary>
    public static async Task UpdateAndDeleteTokenEntitiesAsync(Func<WorkflowsDesignDbContext> createContext, bool sameScope)
    {
        var tenant = $"token-tenant-{Guid.NewGuid():N}";
        var definitionId = $"token-definition-{Guid.NewGuid():N}";
        var versionId = $"token-version-{Guid.NewGuid():N}";
        var draftId = $"token-draft-{Guid.NewGuid():N}";
        var draftLayoutId = $"token-draft-layout-{Guid.NewGuid():N}";
        var versionLayoutId = $"token-version-layout-{Guid.NewGuid():N}";
        await using var scopes = await TestScopes.CreateAsync(createContext, sameScope);

        await scopes.RunAsync(context =>
        {
            context.Definitions.Add(new WorkflowDefinition { Id = definitionId, TenantId = tenant, Name = "Token", CreatedAt = Precise, LastModifiedAt = Precise });
            context.Versions.Add(new WorkflowDefinitionVersion(definitionId, "1.0.0", "{}") { Id = versionId, TenantId = tenant, CreatedAt = Precise, LastModifiedAt = Precise });
            context.Drafts.Add(new WorkflowDefinitionDraft { Id = draftId, TenantId = tenant, WorkflowDefinitionId = definitionId, StateSource = "{}", CreatedAt = Precise, LastModifiedAt = Precise });
            context.DraftLayouts.Add(new WorkflowDefinitionDraftLayout { Id = draftLayoutId, TenantId = tenant, WorkflowDefinitionDraftId = draftId, CreatedAt = Precise, LastModifiedAt = Precise });
            context.VersionLayouts.Add(new WorkflowDefinitionVersionLayout { Id = versionLayoutId, TenantId = tenant, WorkflowDefinitionVersionId = versionId, CreatedAt = Precise, LastModifiedAt = Precise });
            return context.SaveChangesAsync();
        });
        await scopes.RunAsync(async context =>
        {
            var modifiedAt = Precise.AddTicks(1_111_111);
            (await context.Definitions.SingleAsync(x => x.TenantId == tenant && x.Id == definitionId)).LastModifiedAt = modifiedAt;
            (await context.Versions.SingleAsync(x => x.TenantId == tenant && x.Id == versionId)).LastModifiedAt = modifiedAt;
            (await context.DraftLayouts.SingleAsync(x => x.TenantId == tenant && x.Id == draftLayoutId)).LastModifiedAt = modifiedAt;
            (await context.VersionLayouts.SingleAsync(x => x.TenantId == tenant && x.Id == versionLayoutId)).LastModifiedAt = modifiedAt;
            await context.SaveChangesAsync();
        });
        await scopes.RunAsync(async context =>
        {
            context.DraftLayouts.Remove(await context.DraftLayouts.SingleAsync(x => x.TenantId == tenant && x.Id == draftLayoutId));
            context.VersionLayouts.Remove(await context.VersionLayouts.SingleAsync(x => x.TenantId == tenant && x.Id == versionLayoutId));
            context.Versions.Remove(await context.Versions.SingleAsync(x => x.TenantId == tenant && x.Id == versionId));
            context.Definitions.Remove(await context.Definitions.SingleAsync(x => x.TenantId == tenant && x.Id == definitionId));
            await context.SaveChangesAsync();
        });

        await using var reopened = createContext();
        Assert.False(await reopened.Definitions.AnyAsync(x => x.TenantId == tenant && x.Id == definitionId));
        Assert.False(await reopened.Versions.AnyAsync(x => x.TenantId == tenant && x.Id == versionId));
        Assert.False(await reopened.DraftLayouts.AnyAsync(x => x.TenantId == tenant && x.Id == draftLayoutId));
        Assert.False(await reopened.VersionLayouts.AnyAsync(x => x.TenantId == tenant && x.Id == versionLayoutId));
    }

    /// <summary>Hands each step the one context of the scenario, or a context of its own, as each request gets its own scope.</summary>
    private sealed class TestScopes : IAsyncDisposable
    {
        private readonly Func<WorkflowsDesignDbContext> _createContext;
        private readonly WorkflowsDesignDbContext? _shared;

        private TestScopes(Func<WorkflowsDesignDbContext> createContext, WorkflowsDesignDbContext? shared)
        {
            _createContext = createContext;
            _shared = shared;
        }

        public static async Task<TestScopes> CreateAsync(Func<WorkflowsDesignDbContext> createContext, bool sameScope)
        {
            await using (var setup = createContext())
                await setup.Database.EnsureCreatedAsync();
            return new TestScopes(createContext, sameScope ? createContext() : null);
        }

        public async Task RunAsync(Func<WorkflowsDesignDbContext, Task> step)
        {
            if (_shared is not null)
            {
                await step(_shared);
                return;
            }

            await using var context = _createContext();
            await step(context);
        }

        public ValueTask DisposeAsync() => _shared?.DisposeAsync() ?? ValueTask.CompletedTask;
    }
}

internal sealed class FixedAccess(PersistenceAccessContext current) : IPersistenceAccessContextAccessor
{
    public PersistenceAccessContext Current { get; } = current;
}

/// <summary>Stands in for the Publishing module's guard: these definitions were never published.</summary>
internal sealed class NeverPublishedGuard : IWorkflowDefinitionPublicationDeletionGuard
{
    public Task EnsureCanDeleteAsync(string definitionId, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
