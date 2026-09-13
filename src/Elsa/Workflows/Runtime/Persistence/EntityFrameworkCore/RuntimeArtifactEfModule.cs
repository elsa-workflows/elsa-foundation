namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

public static class RuntimeArtifactEfModule
{
    public const string SchemaVersion = "1.0.0";
    public const string HistoryModuleName = "ElsaRuntimeArtifacts";
    public const string DefaultConnectionName = "ElsaRuntimeArtifacts";
    public const string DefaultSqliteConnectionString = "Data Source=elsa-runtime-artifacts.db";
    public const string WorkflowExecutableTableName = "elsa_runtime_workflow_executable";
    public const string WorkflowExecutableCoordinationTableName = "elsa_runtime_workflow_executable_coordination";
    public const string ExecutableActivityTemplateTableName = "elsa_runtime_executable_activity_template";
    public const string ExecutableActivityTemplateHashClaimTableName = "elsa_runtime_executable_activity_template_hash_claim";
    public const string SourceReferenceTableName = "elsa_runtime_workflow_executable_source_reference";
    public const int IdentityMaximumLength = 128;
    public static string HistoryTableName => Elsa.Persistence.EntityFramework.EfMigrationsHistory.TableName(HistoryModuleName);
}
