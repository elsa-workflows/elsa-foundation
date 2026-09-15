namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

/// <summary>Names and bounded projection sizes for the EF workflow activation-slot authority.</summary>
public static class RuntimeActivationSlotEfModule
{
    public const string TableName = "elsa_runtime_workflow_activation_slot";
    public const string SchemaVersion = "1.0.0";
    public const int SlotIdProjectionMaximumLength = 400;
}
