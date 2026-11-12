using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Elsa.Persistence.Schema.SchemaFinalization;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>
/// Applies the selected test protocol's physical minimum to every affected continuation before the provider saves.
/// The requirement comes from typed admission content or the actual execution header, never authored metadata.
/// </summary>
internal static class RuntimeTestEntrySchemaGuard
{
    internal static async ValueTask PrepareAsync(RuntimeDbContext context, bool synchronous, CancellationToken cancellationToken)
    {
        var entries = context.ChangeTracker.Entries().Where(x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Select(x => (Entry: x, Family: Family(x.Entity))).Where(x => x.Family is not null).ToArray();
        if (entries.Length == 0) return;
        // Queue enqueue already reads the exact header in its existing root/ownership query and its writer carries
        // that requirement into the row stamp. Claims preserve that stamp. Do not add a second header query there.
        var gate = context.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()?.ApplicationServiceProvider?
            .GetService<EfSchemaFinalizationGates>()?.FindModuleGate(context.GetType());
        // Once the live gate admits a protected format, all writes in that family already use it.
        // Classifying the owning header adds no protection there and would add a round trip to ordinary checkpoints.
        var bindings = entries.Where(x => x.Entry.Entity is not SchedulerWorkItemEntity &&
                (gate?.StateOf(x.Family!) is not { WritesRefused: false } state ||
                 Version.Parse(state.WriteVersion) < Version.Parse(Minimum(x.Family!)))).Select(x => Binding(x.Entry))
            .Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToArray();
        var testExecutions = entries.Where(x => HasAdmission(x.Entry.Entity)).Select(x => Binding(x.Entry))
            .Where(x => x.HasValue).Select(x => x!.Value).ToHashSet();
        foreach (var binding in bindings.Where(x => !testExecutions.Contains(x)))
        {
            var id = EfWorkflowExecutionStateStore.CreateId(binding.Scope, binding.Workflow);
            var local = context.WorkflowExecutionStates.Local.SingleOrDefault(x => x.Id == id);
            var query = context.WorkflowExecutionStates.AsNoTracking().Where(x => x.Id == id);
            var root = local ?? (synchronous ? query.SingleOrDefault() : await query.SingleOrDefaultAsync(cancellationToken));
            if (root is not null && HasAdmission(root))
                testExecutions.Add(binding);
        }

        foreach (var (entry, family) in entries)
        {
            var stamp = entry.Property(EfSchemaVersion.ColumnName);
            var written = stamp.CurrentValue as string;
            if (!Version.TryParse(written, out _)) continue; // The owning reader diagnoses invalid ordinary storage.
            var original = entry.State == EntityState.Modified ? stamp.OriginalValue as string : null;
            var minimum = written!;
            var testOwned = HasAdmission(entry.Entity) || Binding(entry) is { } binding && testExecutions.Contains(binding);
            if (Version.TryParse(original, out var stored) && (testOwned || stored >= Version.Parse(Minimum(family!))))
                minimum = RuntimeTestEntryWriteVersion.Preserve(minimum, original);
            if (testOwned)
                minimum = RuntimeTestEntryWriteVersion.Preserve(minimum, Minimum(family!));
            if (entry.State == EntityState.Deleted)
            {
                // The predecessor delete behavior stays available for ordinary predecessor formats. A protected
                // row cannot be acknowledged/removed by a host whose admitted writer still uses an older format.
                if (gate is not null && (testOwned || Version.Parse(written!) >= Version.Parse(Minimum(family!))))
                    RuntimeTestEntryWriteVersion.Require(context, family!, minimum);
                continue;
            }
            // Ordinary rows keep their existing minimum and ungated stamp. A live gate's selected write format
            // remains authoritative, including when this reader also understands a later test-only revision.
            stamp.CurrentValue = gate is null ? minimum : RuntimeTestEntryWriteVersion.Require(context, family!, minimum);
        }
    }

    private static (string Scope, string Workflow)? Binding(EntityEntry entry)
    {
        if (entry.Metadata.FindProperty("ScopeKey") is null || entry.Metadata.FindProperty("WorkflowExecutionId") is null)
            return null;
        var scope = entry.Property("ScopeKey").CurrentValue as string;
        var workflow = entry.Property("WorkflowExecutionId").CurrentValue as string;
        if (scope is null || workflow is null) return null;
        try
        {
            return (EfRuntimeOperationalStoreSupport.Decode(scope), entry.Entity is BackgroundActivityWorkEntity ? workflow : EfRuntimeOperationalStoreSupport.Decode(workflow));
        }
        catch (InvalidOperationException) { return null; } // Not an alternative integrity validator for ordinary data.
    }

    private static bool HasAdmission(object entity)
    {
        try
        {
            return entity switch
            {
                WorkflowExecutionStateEntity row => RuntimeTestEntryWriteVersion.ContentHasEntry(row.ContentJson),
                SchedulerWorkItemEntity row => RuntimeTestEntryWriteVersion.Requires(RuntimeArtifactJson.Deserialize<RuntimeSchedulerWorkItem>(row.ContentJson)),
                RuntimePostCommitOutboxEntity row => RuntimeTestEntryWriteVersion.Requires(RuntimeArtifactJson.Deserialize<RuntimePostCommitOutboxItem>(row.ContentJson)),
                _ => false
            };
        }
        // Format classification never makes invalid content runnable; every existing owning reader still checks it.
        // Do not turn this format-only guard into a second serializer/validator for ordinary payload writes.
        catch (System.Text.Json.JsonException) { return false; }
        catch (ArgumentException) { return false; }
    }

    private static string? Family(object entity) => entity switch
    {
        WorkflowExecutionStateEntity => RuntimeWorkflowExecutionEfModule.SchemaFamily,
        SchedulerWorkItemEntity => RuntimeSchedulerWorkEfModule.SchemaFamily,
        RuntimePostCommitOutboxEntity => RuntimePostCommitOutboxEfModule.SchemaFamily,
        BackgroundActivityWorkEntity => RuntimeBackgroundActivityWorkEfModule.SchemaFamily,
        DurableValueStateEntity or SchedulerStateEntity or DurableTimerEntity or ExecutionLivenessStateEntity or
            WorkflowHoldStateEntity or IncidentStateEntity or WorkflowRunHealthStateEntity or RuntimeCheckpointCommitEntity or
            RecurringTriggerScheduleEntity or RecurringTriggerScheduleProjectionStateEntity => RuntimeOperationalStateEfModule.SchemaFamily,
        _ => null
    };

    private static string Minimum(string family) => family switch
    {
        RuntimeWorkflowExecutionEfModule.SchemaFamily => RuntimeWorkflowExecutionEfModule.TestEntrySchemaVersion,
        RuntimeSchedulerWorkEfModule.SchemaFamily => RuntimeSchedulerWorkEfModule.TestEntrySchemaVersion,
        RuntimePostCommitOutboxEfModule.SchemaFamily => RuntimePostCommitOutboxEfModule.TestEntrySchemaVersion,
        RuntimeBackgroundActivityWorkEfModule.SchemaFamily => RuntimeBackgroundActivityWorkEfModule.TestEntrySchemaVersion,
        RuntimeOperationalStateEfModule.SchemaFamily => RuntimeOperationalStateEfModule.TestEntrySchemaVersion,
        _ => throw new InvalidOperationException("The selected Runtime family has no test format.")
    };
}
