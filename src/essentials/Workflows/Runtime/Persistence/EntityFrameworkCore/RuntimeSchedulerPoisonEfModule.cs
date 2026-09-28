using Elsa.Persistence.EntityFramework;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

/// <summary>Schema and bounded projection limits for the R23 scheduler-poison store.</summary>
public static class RuntimeSchedulerPoisonEfModule
{
    public const string TableName = "elsa_runtime_scheduler_poison";
    public const string SchemaVersion = "1.0.0";
    public const string SchemaFamily = "RuntimeSchedulerPoison";
    /// <summary>The family's one chain, from this assembly's declaration: what every reader of the family checks and
    /// upcasts through (spec 180, FR-010).</summary>
    public static readonly EfSchemaChain Chain = EfSchemaChain.Of(typeof(RuntimeSchedulerPoisonEfModule).Assembly, SchemaFamily);
    public const int IdentityMaximumLength = RuntimeOperationalStateEfModule.IdentityMaximumLength;
    public const int ScopeProjectionMaximumLength = RuntimeOperationalStateEfModule.ScopeProjectionMaximumLength;
    public const int OrderKeyMaximumLength = RuntimeOperationalStateEfModule.OrderKeyMaximumLength;

    /// <summary>
    /// Scheduler work-item ids are composed by the runtime from an execution id, a command kind and an
    /// activity path, so they run past <see cref="IdentityMaximumLength"/>. A document store keys them by document
    /// id, which allows 450.
    /// </summary>
    public const int WorkItemIdentityMaximumLength = 450;
    public const int WorkItemIdentityProjectionMaximumLength = ((WorkItemIdentityMaximumLength * sizeof(char) + 2) / 3) * 4;
}
