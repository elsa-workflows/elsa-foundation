using Elsa.Persistence.EntityFramework;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

public static class RuntimeArtifactEfModule
{
    public const string SchemaVersion = "1.0.0";
    public const string SchemaFamily = "RuntimeArtifact";
    /// <summary>The family's one chain, from this assembly's declaration: what every reader of the family checks and
    /// upcasts through (spec 180, FR-010).</summary>
    public static readonly EfSchemaChain Chain = EfSchemaChain.Of(typeof(RuntimeArtifactEfModule).Assembly, SchemaFamily);
    public const string WorkflowExecutableTableName = "elsa_runtime_workflow_executable";
    public const string WorkflowExecutableCoordinationTableName = "elsa_runtime_workflow_executable_coordination";
    public const string ExecutableActivityTemplateTableName = "elsa_runtime_executable_activity_template";
    public const string ExecutableActivityTemplateHashClaimTableName = "elsa_runtime_executable_activity_template_hash_claim";
    public const string SourceReferenceTableName = "elsa_runtime_workflow_executable_source_reference";
    public const int IdentityMaximumLength = 128;
    public const int IdentityProjectionMaximumLength = IdentityMaximumLength * sizeof(char) * 2;
    public const int HashMaximumLength = 450;
    public const int ScopeMaximumLength = 32;
}
