using Elsa.Persistence.EntityFramework;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

public static class RuntimeTriggerBindingEfModule
{
    public const string TableName = "elsa_runtime_workflow_trigger_binding";
    public const string ProjectionStateTableName = "elsa_runtime_trigger_binding_projection_state";
    public const string SchemaVersion = "1.0.0";
    public const string SchemaFamily = "RuntimeTriggerBinding";
    /// <summary>The family's one chain, from this assembly's declaration: what every reader of the family checks and
    /// upcasts through (spec 180, FR-010).</summary>
    public static readonly EfSchemaChain Chain = EfSchemaChain.Of(typeof(RuntimeTriggerBindingEfModule).Assembly, SchemaFamily);
    public const int IdentityMaximumLength = 128;
    public const int StimulusTypeMaximumLength = 240;
    public const int IdentityProjectionMaximumLength = ((IdentityMaximumLength * sizeof(char) + 2) / 3) * 4;
    public const int StimulusTypeProjectionMaximumLength = ((StimulusTypeMaximumLength * sizeof(char) + 2) / 3) * 4;
}
