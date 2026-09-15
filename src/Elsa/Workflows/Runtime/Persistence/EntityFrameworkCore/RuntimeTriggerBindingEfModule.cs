namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

public static class RuntimeTriggerBindingEfModule
{
    public const string TableName = "elsa_runtime_workflow_trigger_binding";
    public const string ProjectionStateTableName = "elsa_runtime_trigger_binding_projection_state";
    public const string SchemaVersion = "1.0.0";
    public const int IdentityMaximumLength = 128;
    public const int StimulusTypeMaximumLength = 240;
    public const int IdentityProjectionMaximumLength = ((IdentityMaximumLength * sizeof(char) + 2) / 3) * 4;
    public const int StimulusTypeProjectionMaximumLength = ((StimulusTypeMaximumLength * sizeof(char) + 2) / 3) * 4;
}
