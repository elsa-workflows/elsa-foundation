
namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

/// <summary>
/// Names and provider-neutral limits for the R07-R09 activity execution persistence family.
/// </summary>
public static class RuntimeActivityExecutionEfModule
{
    public const string SchemaVersion = "1.0.0";
    public const string ActivityExecutionStateTableName = "elsa_runtime_activity_execution_state";
    public const string ActivityExecutionInspectionTableName = "elsa_runtime_activity_execution_inspection";
    public const string ActivityExecutionHierarchyTableName = "elsa_runtime_activity_execution_hierarchy";
    public const int IdentityMaximumLength = 128;
    // EfRelationalIdentity encodes UTF-16 code units as base64. 450 leaves room
    // for the 344 characters needed by a 128-code-unit identity plus framing.
    public const int EncodedIdentityMaximumLength = 450;
    public const int HashMaximumLength = 64;
    public const int OrderKeyMaximumLength = 655;
}
