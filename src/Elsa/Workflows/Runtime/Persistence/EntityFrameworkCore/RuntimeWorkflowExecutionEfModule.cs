
namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

/// <summary>Names and provider-neutral limits for R10 workflow execution persistence.</summary>
public static class RuntimeWorkflowExecutionEfModule
{
    public const string DefaultConnectionName = "ElsaRuntimeWorkflowExecutions";
    public const string DefaultSqliteConnectionString = "Data Source=elsa-runtime-workflow-executions.db";
    public const string TableName = "elsa_runtime_workflow_execution_state";
    public const string SchemaVersion = "1.0.0";
    public const int IdentityMaximumLength = 128;
    public const int TenantMaximumLength = 256;
    public const int IdentityProjectionMaximumLength = 450;
    // EfRelationalIdentity encodes each UTF-16 code unit as two bytes and then
    // base64.  256 code units therefore require 684 encoded characters.
    public const int TenantProjectionMaximumLength = ((TenantMaximumLength * sizeof(char) + 2) / 3) * 4;
    // Order keys are the hexadecimal representation of the UTF-16 ordinal key
    // (128 code units plus a two-byte length suffix).  Keeping this projection
    // bounded to its actual width makes every composite index valid on SQL
    // Server and MySQL while retaining lossless ordinal ordering.
    public const int OrderKeyMaximumLength = (IdentityMaximumLength + 1) * sizeof(char) * 2;
}
