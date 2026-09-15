namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

/// <summary>Names and bounded projection sizes for the EF workflow-dispatch lifecycle.</summary>
public static class RuntimeWorkflowDispatchEfModule
{
    public const string TableName = "elsa_runtime_workflow_dispatch";
    public const string SchemaVersion = "1.0.0";
    public const int IdentityMaximumLength = 450;
    public const int IdentityProjectionMaximumLength = ((IdentityMaximumLength * sizeof(char) + 2) / 3) * 4;
    public const int ScopeProjectionMaximumLength = RuntimeOperationalStateEfModule.ScopeProjectionMaximumLength;
    // Auxiliary parent/child projection only. DispatchId has a full ordinal text key; the candidate indexes
    // exclude that column to remain within SQL Server/MySQL key-width limits.
    public const int OrderKeyPrefixMaximumLength = 32;
    public const int OrderKeyMaximumLength = (OrderKeyPrefixMaximumLength + 1) * sizeof(char) * 2 + 64;
}
