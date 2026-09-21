namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

/// <summary>Names and bounded projection sizes for the EF workflow activation-slot authority.</summary>
public static class RuntimeActivationSlotEfModule
{
    public const string TableName = "elsa_runtime_workflow_activation_slot";
    public const string SchemaVersion = "1.0.0";
    // WorkflowActivationSlotIdentity formats two legal 128-code-unit identities into a
    // composite identity of 280 code units. Keep one defensive spare code unit for the
    // formatter contract, then size both lossless projections from that bound.
    public const int SlotIdMaximumLength = 281;
    public const int SlotIdProjectionMaximumLength = ((SlotIdMaximumLength * sizeof(char) + 2) / 3) * 4;
    public const int SlotIdOrderKeyMaximumLength = (SlotIdMaximumLength + 1) * sizeof(char) * 2;
}
