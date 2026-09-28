using Elsa.Persistence.EntityFramework;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

/// <summary>Schema and projection limits for the R13 workflow test-scope ledger.</summary>
public static class RuntimeWorkflowTestScopeEfModule
{
    public const string TableName = "elsa_runtime_workflow_test_scope";
    public const string SchemaVersion = "1.0.0";
    public const string SchemaFamily = "RuntimeWorkflowTestScope";
    /// <summary>The family's one chain, from this assembly's declaration: what every reader of the family checks and
    /// upcasts through (spec 180, FR-010).</summary>
    public static readonly EfSchemaChain Chain = EfSchemaChain.Of(typeof(RuntimeWorkflowTestScopeEfModule).Assembly, SchemaFamily);
    public const int IdentityMaximumLength = 128;
    public const int TenantMaximumLength = 256;
    public const int IdentityProjectionMaximumLength = 450;
    public const int TenantProjectionMaximumLength = 684;
    public const int OrderKeyMaximumLength = 516;
    public const int MaximumPageSize = 100;
}
