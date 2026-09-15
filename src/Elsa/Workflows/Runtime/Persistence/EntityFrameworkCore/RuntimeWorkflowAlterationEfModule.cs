namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

/// <summary>Schema and projection limits for the R11/R12 workflow alteration ledger.</summary>
public static class RuntimeWorkflowAlterationEfModule
{
    public const string PlanTableName = "elsa_runtime_workflow_alteration_plan";
    public const string JobTableName = "elsa_runtime_workflow_alteration_job";
    public const string SchemaVersion = "1.0.0";
    public const int IdentityMaximumLength = 128;
    public const int TenantMaximumLength = 256;
    public const int IdentityProjectionMaximumLength = 450;
    public const int TenantProjectionMaximumLength = 684;
    // The active-plan order key prefixes the provider-neutral hexadecimal identity projection
    // with a fixed-width UTC tick value and a separator.
    public const int OrderKeyMaximumLength = 19 + 1 + ((IdentityMaximumLength + 1) * sizeof(char) * 2);
    public const int MaximumPageSize = 2000;
    public const int UnsealedCleanupPageSize = 100;
}
