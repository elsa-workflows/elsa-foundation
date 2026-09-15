namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

/// <summary>Schema and projection limits for the R13 workflow test-scope ledger.</summary>
public static class RuntimeWorkflowTestScopeEfModule
{
    public const string DefaultConnectionName = "ElsaRuntimeWorkflowTestScopes";
    public const string DefaultSqliteConnectionString = "Data Source=elsa-runtime-workflow-test-scopes.db";
    public const string TableName = "elsa_runtime_workflow_test_scope";
    public const string SchemaVersion = "1.0.0";
    public const int IdentityMaximumLength = 128;
    public const int TenantMaximumLength = 256;
    public const int IdentityProjectionMaximumLength = 450;
    public const int TenantProjectionMaximumLength = 684;
    public const int OrderKeyMaximumLength = 516;
    public const int MaximumPageSize = 100;
}
