using System.Text.Json;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Filters;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.DesignPersistence.Benchmarks.Harness;
using Elsa.DesignPersistence.Benchmarks.Workload;
using Elsa.Persistence.Core.Design;
using Elsa.Primitives.Versioning;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts; // commands + mutation results
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Filters;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.DesignPersistence.Benchmarks.Stores;

/// <summary>
/// The Benchmark Acceptance Catalog operations executed through real, production-composed design
/// stores and commands. Read operations project resolved domain entities into the SAME canonical
/// DTOs the modeled physical forms emit, so a correct EF oracle, a correct Groundwork entity store,
/// and every modeled form hash identically at the 1K correctness scale. Both the EF and Groundwork
/// store targets share this code, differing only in composition; that is what makes the correctness
/// comparison meaningful.
/// </summary>
public static class DesignStoreOperations
{
    private const string Scope = DesignWorkload.Scope;
    private const string MaterializeTargetId = "bench-materialize-wf";
    private const string AddVersionTargetId = "bench-addver-act";
    private static readonly string OneZeroSortKey = SemVer.ToSortKey("1.0.0");

    // --- Seeding ----------------------------------------------------------------------------

    public static async Task SeedAsync(Func<IServiceScope> scopeFactory, DesignWorkload workload, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory();
        var services = scope.ServiceProvider;
        var addWorkflow = services.GetRequiredService<IAddWorkflowDefinitionCommand>();
        var materialize = services.GetRequiredService<IMaterializeWorkflowDefinitionVersionCommand>();
        var addActivity = services.GetRequiredService<IAddActivityDefinitionCommand>();
        var addActivityVersion = services.GetRequiredService<IAddActivityDefinitionVersionCommand>();

        foreach (var d in workload.WorkflowDefinitions())
        {
            await addWorkflow.Execute(
                Key($"seed-wf:{d.Id}"),
                WorkflowDefinition(d.Id, d.Name, d.Description),
                Draft(d.DraftId, d.Id),
                cancellationToken);
        }

        foreach (var v in workload.WorkflowVersions())
            await materialize.Execute(Key($"seed-wfv:{v.Id}"), Version(v.DefinitionId, v.Version, v.Id), cancellationToken);

        foreach (var a in workload.ActivityDefinitions())
            await addActivity.Execute(Key($"seed-act:{a.Id}"), ActivityDefinition(a), ActivityVersion(a.Id, "1.0.0", $"{a.Id}-v1.0.0"), cancellationToken);

        foreach (var v in workload.ActivityVersions().Where(v => v.Version != "1.0.0"))
            await addActivityVersion.Execute(Key($"seed-actv:{v.Id}"), ActivityVersion(v.DefinitionId, v.Version, v.Id), cancellationToken);

        // Dedicated write targets for the measured materialize / add-version operations.
        await addWorkflow.Execute(
            Key("seed-materialize-target"),
            WorkflowDefinition(MaterializeTargetId, "Materialize target", "Write-op target."),
            Draft($"{MaterializeTargetId}-draft", MaterializeTargetId),
            cancellationToken);
        await addActivity.Execute(
            Key("seed-addversion-target"),
            ActivityDefinition(new ActivityDefinitionRecord(AddVersionTargetId, "Elsa.Bench.AddVersionTarget", "Bench", "Add-version target", "Write-op target.")),
            ActivityVersion(AddVersionTargetId, "0.1.0", $"{AddVersionTargetId}-v0.1.0"),
            cancellationToken);
    }

    // --- Operations -------------------------------------------------------------------------

    public static IReadOnlyList<BenchmarkOperation> Build(Func<IServiceScope> scopeFactory, DesignWorkload workload)
    {
        var wfIdentity = workload.WorkflowIdentityRequests();
        var wfVersioned = workload.VersionedWorkflowDefinitionIds();
        var actIdentity = workload.ActivityIdentityRequests();
        var actVersioned = workload.VersionedActivityDefinitionIds();
        var projectionIds = workload.ProjectionDefinitionIds();

        BenchmarkOperation Read(string name, Func<long, IServiceProvider, Task<string>> body) =>
            new(name, OperationKind.Read, 1, true, async (i, ct) =>
            {
                using var scope = scopeFactory();
                return await body(i, scope.ServiceProvider);
            });

        // The per-operation salt (the concurrency value) namespaces write identities so the two
        // concurrency variants of one write never collide on a unique identity in the same database.
        BenchmarkOperation Write(string name, int concurrency, Func<int, long, IServiceProvider, CancellationToken, Task> body) =>
            new(name, OperationKind.Write, concurrency, false, async (i, ct) =>
            {
                using var scope = scopeFactory();
                await body(concurrency, i, scope.ServiceProvider, ct);
                return "ok";
            });

        var operations = new List<BenchmarkOperation>
        {
            Read("wf.identity.get", (i, sp) => GetWorkflowDefinition(sp, wfIdentity[Idx(i, wfIdentity.Count)].Id)),
            Read("wf.identity.version-exact", (i, sp) => GetWorkflowVersionExact(sp, wfVersioned[Idx(i, wfVersioned.Count)])),
            Read("wf.identity.version-latest", (i, sp) => GetWorkflowLatestVersion(sp, wfVersioned[Idx(i, wfVersioned.Count)])),
            Read("wf.catalog.filter-page", (_, sp) => WorkflowFilterPage(sp)),
            Read("wf.catalog.count", (_, sp) => WorkflowFilterCount(sp)),
            Read("wf.catalog.projection", (_, sp) => WorkflowProjection(sp, projectionIds)),
            Read("wf.version.exists", (i, sp) => WorkflowVersionExists(sp, wfVersioned[Idx(i, wfVersioned.Count)])),
            Read("act.identity.get", (i, sp) => GetActivityDefinition(sp, actIdentity[Idx(i, actIdentity.Count)].Id)),
            Read("act.identity.version-exact", (i, sp) => GetActivityVersionExact(sp, actVersioned[Idx(i, actVersioned.Count)])),
            Read("act.catalog.filter-page", (_, sp) => ActivityFilterPage(sp)),
            Read("act.catalog.versions-batch", (_, sp) => ActivityVersionsBatch(sp, actVersioned))
        };

        foreach (var concurrency in new[] { 1, 16 })
        {
            operations.Add(Write($"wf.lifecycle.create@c{concurrency}", concurrency, CreateWorkflow));
            operations.Add(Write($"wf.lifecycle.materialize@c{concurrency}", concurrency, MaterializeWorkflowVersion));
            operations.Add(Write($"act.lifecycle.create@c{concurrency}", concurrency, CreateActivity));
            operations.Add(Write($"act.lifecycle.add-version@c{concurrency}", concurrency, AddActivityVersion));
        }

        return operations;
    }

    // --- Read projections (identical shapes to SqliteFormTarget) ----------------------------

    private static async Task<string> GetWorkflowDefinition(IServiceProvider sp, string id)
    {
        var def = await sp.GetRequiredService<IWorkflowDefinitionStore>().FindByIdAsync(id);
        object? row = def is null ? null : new { def.Id, def.Name, def.Description };
        return CanonicalHash.Of(new { Found = def is not null, Row = row });
    }

    private static async Task<string> GetWorkflowVersionExact(IServiceProvider sp, string definitionId)
    {
        var version = await sp.GetRequiredService<IWorkflowDefinitionVersionStore>().FindByIdAsync($"{definitionId}-v1.0.0");
        object? row = version is null ? null : new { version.Id, version.DefinitionId, version.Version };
        return CanonicalHash.Of(new { Found = version is not null, Row = row });
    }

    private static async Task<string> GetWorkflowLatestVersion(IServiceProvider sp, string definitionId)
    {
        var version = await sp.GetRequiredService<IWorkflowDefinitionVersionStore>().FindLatestVersionAsync(definitionId);
        object? row = version is null ? null : new { version.Id, version.DefinitionId, version.Version };
        return CanonicalHash.Of(new { Found = version is not null, Row = row });
    }

    private static async Task<string> WorkflowVersionExists(IServiceProvider sp, string definitionId)
    {
        var exists = await sp.GetRequiredService<IWorkflowDefinitionVersionStore>().ExistsAsync(definitionId, OneZeroSortKey);
        return CanonicalHash.Of(new { Exists = exists });
    }

    private static async Task<string> WorkflowFilterPage(IServiceProvider sp)
    {
        var defs = await sp.GetRequiredService<IWorkflowDefinitionStore>().ListAsync(new WorkflowDefinitionFilter { SearchTerm = "Alpha" });
        var page = defs.Select(x => x.Id).OrderBy(x => x, StringComparer.Ordinal).Skip(450).Take(50).ToList();
        return CanonicalHash.Of(page);
    }

    private static async Task<string> WorkflowFilterCount(IServiceProvider sp)
    {
        var defs = await sp.GetRequiredService<IWorkflowDefinitionStore>().ListAsync(new WorkflowDefinitionFilter { SearchTerm = "Alpha" });
        return CanonicalHash.Of(new { Count = defs.Count });
    }

    private static async Task<string> WorkflowProjection(IServiceProvider sp, IReadOnlyList<string> ids)
    {
        var rows = await sp.GetRequiredService<IWorkflowDefinitionListProjectionStore>().ListByDefinitionIdsAsync(ids);
        var ordered = rows
            .OrderBy(x => x.WorkflowDefinitionId, StringComparer.Ordinal)
            .Select(x => new { x.WorkflowDefinitionId, x.DraftId, x.LatestVersionId, x.LatestVersion, x.VersionCount })
            .ToList();
        return CanonicalHash.Of(ordered);
    }

    private static async Task<string> GetActivityDefinition(IServiceProvider sp, string id)
    {
        var def = await sp.GetRequiredService<IActivityDefinitionStore>().FindByIdOrActivityTypeKeyAsync(id, id);
        object? row = def is null ? null : new { def.Id, def.ActivityTypeKey, def.Category, def.DisplayName };
        return CanonicalHash.Of(new { Found = def is not null, Row = row });
    }

    private static async Task<string> GetActivityVersionExact(IServiceProvider sp, string definitionId)
    {
        var version = await sp.GetRequiredService<IActivityDefinitionVersionStore>().FindByDefinitionAndSortKeyAsync(definitionId, OneZeroSortKey);
        object? row = version is null ? null : new { version.Id, version.DefinitionId, version.Version };
        return CanonicalHash.Of(new { Found = version is not null, Row = row });
    }

    private static async Task<string> ActivityFilterPage(IServiceProvider sp)
    {
        var defs = await sp.GetRequiredService<IActivityDefinitionStore>().ListAsync(new ActivityDefinitionFilter { SearchTerm = "Alpha" });
        var page = defs.Select(x => x.Id).OrderBy(x => x, StringComparer.Ordinal).Skip(450).Take(50).ToList();
        return CanonicalHash.Of(page);
    }

    private static async Task<string> ActivityVersionsBatch(IServiceProvider sp, IReadOnlyList<string> definitionIds)
    {
        var versions = await sp.GetRequiredService<IActivityDefinitionVersionStore>().ListByDefinitionIdsAsync(definitionIds);
        var ordered = versions
            .OrderBy(x => x.DefinitionId, StringComparer.Ordinal)
            .ThenBy(x => x.SemVerSortKey, StringComparer.Ordinal)
            .Select(x => new { x.DefinitionId, VersionId = x.Id, x.Version })
            .ToList();
        return CanonicalHash.Of(ordered);
    }

    // --- Write compounds --------------------------------------------------------------------

    private static async Task CreateWorkflow(int salt, long index, IServiceProvider sp, CancellationToken ct)
    {
        // Create definition + initial draft. Soft-delete/permanent-delete and promote/submit are
        // omitted from the timed write set because they require prior lifecycle state (a definition
        // must be soft-deleted before permanent deletion; promote/submit gate on draft validation),
        // which would make the per-invocation cost non-representative. The coordinator can extend
        // this set once a validation-clean fixed draft state is defined.
        var id = $"bench-wf-{salt}-{Tag(index)}";
        await sp.GetRequiredService<IAddWorkflowDefinitionCommand>().Execute(
            Key($"wf-create:{id}"),
            WorkflowDefinition(id, "Bench create", "Lifecycle create."),
            Draft($"{id}-draft", id),
            ct);
    }

    private static async Task MaterializeWorkflowVersion(int salt, long index, IServiceProvider sp, CancellationToken ct)
    {
        var version = $"{VersionMajor(index)}.{salt}.0";
        await sp.GetRequiredService<IMaterializeWorkflowDefinitionVersionCommand>().Execute(
            Key($"wf-materialize:{version}"),
            Version(MaterializeTargetId, version, $"{MaterializeTargetId}-v{version}"),
            ct);
    }

    private static async Task CreateActivity(int salt, long index, IServiceProvider sp, CancellationToken ct)
    {
        var id = $"bench-act-{salt}-{Tag(index)}";
        await sp.GetRequiredService<IAddActivityDefinitionCommand>().Execute(
            Key($"act-create:{id}"),
            ActivityDefinition(new ActivityDefinitionRecord(id, $"Elsa.Bench.Create.{salt}.{Tag(index)}", "Bench", "Bench create", "Lifecycle create.")),
            ActivityVersion(id, "1.0.0", $"{id}-v1.0.0"),
            ct);
    }

    private static async Task AddActivityVersion(int salt, long index, IServiceProvider sp, CancellationToken ct)
    {
        var version = $"{VersionMajor(index)}.{salt}.0";
        await sp.GetRequiredService<IAddActivityDefinitionVersionCommand>().Execute(
            Key($"act-addver:{version}"),
            ActivityVersion(AddVersionTargetId, version, $"{AddVersionTargetId}-v{version}"),
            ct);
    }

    // --- Entity builders --------------------------------------------------------------------

    private static int Idx(long i, int count) => (int)(((i % count) + count) % count);

    private static DesignOperationKey Key(string operation) => new($"bench:{operation}");

    private static string Tag(long index) => index >= 0 ? $"m{index}" : $"w{-index}";

    // Warm-up (negative) and measured (positive) indices map to disjoint major-version ranges so a
    // single process never reuses an immutable version identity.
    private static long VersionMajor(long index) => index >= 0 ? index : 700_000 + (-index);

    private static WorkflowDefinition WorkflowDefinition(string id, string name, string? description) => new()
    {
        Id = id,
        TenantId = Scope,
        Name = name,
        Description = description,
        CreatedAt = DesignWorkload.Epoch,
        LastModifiedAt = DesignWorkload.Epoch
    };

    private static WorkflowDefinitionDraft Draft(string draftId, string definitionId) => new()
    {
        Id = draftId,
        TenantId = Scope,
        WorkflowDefinitionId = definitionId,
        State = WorkflowDefinitionState.Empty,
        CreatedAt = DesignWorkload.Epoch,
        LastModifiedAt = DesignWorkload.Epoch
    };

    private static WorkflowDefinitionVersion Version(string definitionId, string version, string versionId) => new(definitionId, version)
    {
        Id = versionId,
        TenantId = Scope,
        CreatedAt = DesignWorkload.Epoch,
        LastModifiedAt = DesignWorkload.Epoch,
        SourceCreatedAt = DesignWorkload.Epoch
    };

    private static ActivityDefinition ActivityDefinition(ActivityDefinitionRecord record) => new()
    {
        Id = record.Id,
        TenantId = Scope,
        ActivityTypeKey = record.ActivityTypeKey,
        Category = record.Category,
        DisplayName = record.DisplayName,
        Description = record.Description,
        CreatedAt = DesignWorkload.Epoch,
        LastModifiedAt = DesignWorkload.Epoch
    };

    private static ActivityDefinitionVersion ActivityVersion(string definitionId, string version, string id) => new(version, definitionId)
    {
        Id = id,
        TenantId = Scope,
        ProviderKey = "elsa.bench",
        ProviderSchemaVersion = "1",
        ConsumerKey = "elsa.workflow",
        ConsumerSchemaVersion = "1",
        DescriptorPayload = JsonSerializer.SerializeToElement(new { kind = "bench", version }),
        SourceKind = "bench",
        SourceId = definitionId,
        Hash = $"bench-hash-{id}",
        CreatedAt = DesignWorkload.Epoch,
        LastModifiedAt = DesignWorkload.Epoch
    };
}
