using Elsa.Persistence.EntityFramework;

namespace Elsa.Workflows.Design.Persistence.EntityFrameworkCore;

public static class WorkflowsDesignEfModule
{
    public const string HistoryModuleName = "ElsaWorkflowsDesign";
    public const string DefaultConnectionName = "ElsaWorkflowsDesign";
    public const string DefaultSqliteConnectionString = "Data Source=elsa-workflows-design.db";
    public const string DefinitionTable = "elsa_workflow_definitions_v2";
    public const string VersionTable = "elsa_workflow_definition_versions";
    public const string DraftTable = "elsa_workflow_definition_drafts";
    public const string DraftLayoutTable = "elsa_workflow_definition_draft_layouts";
    public const string VersionLayoutTable = "elsa_workflow_definition_version_layouts";
    public const string OperationTable = "elsa_design_operations";
    public const string VersionIdentityIndex = "UX_elsa_workflow_definition_versions_semver_identity";
    public static string HistoryTableName => EfMigrationsHistory.TableName(HistoryModuleName);
}
