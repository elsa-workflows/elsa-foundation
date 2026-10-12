using Elsa.Persistence.EntityFramework;

namespace Elsa.Workflows.Design.Persistence.EntityFrameworkCore;

public static class WorkflowsDesignEfModule
{
    public const string HistoryModuleName = "ElsaWorkflowsDesign";
    public const string DefaultConnectionName = EfConnectionDefaults.ConnectionName;
    public const string DefaultSqliteConnectionString = EfConnectionDefaults.SqliteConnectionString;
    public const string DefinitionTable = "elsa_workflow_definitions_v2";
    public const string VersionTable = "elsa_workflow_definition_versions";
    public const string DraftTable = "elsa_workflow_definition_drafts";
    public const string DraftLayoutTable = "elsa_workflow_definition_draft_layouts";
    public const string VersionLayoutTable = "elsa_workflow_definition_version_layouts";
    public const string OperationTable = "elsa_design_operations";
    public const string VersionIdentityIndex = "UX_elsa_workflow_definition_versions_semver_identity";

    /// <summary>The persisted-schema version every workflow-design row is stamped with, and checked against when read.</summary>
    public const string SchemaVersion = "1.0.0";

    /// <summary>
    /// The schema family this module's rows belong to. The module maps domain types directly, so its stamp is a shadow
    /// property that <see cref="EfSchemaVersionMaterializationInterceptor"/> writes and checks.
    /// </summary>
    public const string SchemaFamily = "WorkflowsDesign";
    /// <summary>The family's one chain, from this assembly's declaration: what every reader of the family checks and
    /// upcasts through (spec 180, FR-010).</summary>
    public static readonly EfSchemaChain Chain = EfSchemaChain.Of(typeof(WorkflowsDesignEfModule).Assembly, SchemaFamily);

    public static string HistoryTableName => EfMigrationsHistory.TableName(HistoryModuleName);
}
