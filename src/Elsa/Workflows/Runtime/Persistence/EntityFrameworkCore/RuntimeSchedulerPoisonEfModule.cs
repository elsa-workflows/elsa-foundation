namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

/// <summary>Schema and bounded projection limits for the R23 scheduler-poison store.</summary>
public static class RuntimeSchedulerPoisonEfModule
{
    public const string TableName = "elsa_runtime_scheduler_poison";
    public const string SchemaVersion = "1.0.0";
    public const int IdentityMaximumLength = RuntimeOperationalStateEfModule.IdentityMaximumLength;
    public const int ScopeProjectionMaximumLength = RuntimeOperationalStateEfModule.ScopeProjectionMaximumLength;
    public const int OrderKeyMaximumLength = RuntimeOperationalStateEfModule.OrderKeyMaximumLength;
}
