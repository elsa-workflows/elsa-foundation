using CShells.Features;
using Elsa.Api.FastEndpoints;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Workflows.ExecutionEvidence.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.ExecutionEvidence;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Runtime")]
[ManifestFeatureCategory("Diagnostics")]
[ShellFeature(
    name: "WorkflowsExecutionEvidence",
    DisplayName = "Workflow Execution Evidence",
    Description =
        "Records what a workflow execution did into a process-local, in-memory buffer and exposes it over HTTP so an "
        + "automated test can assert over it. Evidence is never persisted, is not replicated across nodes, and is lost "
        + "on restart or once the buffer caps evict it — enable this on test hosts only, not in production.")]
public sealed class WorkflowsExecutionEvidenceFeature : FastEndpointsFeatureBase
{
    [ManifestSetting(
        DisplayName = "Maximum records per workflow",
        Description = "Oldest facts are dropped once one workflow execution exceeds this many.",
        Category = "Diagnostics",
        DefaultValue = "10000")]
    public int MaxRecordsPerWorkflow { get; set; } = 10_000;

    [ManifestSetting(
        DisplayName = "Maximum tracked workflows",
        Description = "Least-recently-written workflow executions are evicted whole once this many are tracked.",
        Category = "Diagnostics",
        DefaultValue = "1000")]
    public int MaxWorkflows { get; set; } = 1_000;

    [ManifestSetting(
        DisplayName = "Maximum tracked checkpoints per workflow",
        Description = "Replay de-duplication window. A replayed checkpoint older than this window can be recorded twice.",
        Category = "Diagnostics",
        DefaultValue = "10000")]
    public int MaxTrackedCheckpointsPerWorkflow { get; set; } = 10_000;

    [ManifestSetting(
        DisplayName = "Maximum inline value length",
        Description = "Variable values whose raw JSON exceeds this many characters are recorded as Truncated.",
        Category = "Diagnostics",
        DefaultValue = "8192")]
    public int MaxInlineValueLength { get; set; } = 8 * 1024;

    [ManifestSetting(
        DisplayName = "Redact sensitive values",
        Description = "Withholds inline values whose protection policy marks them sensitive, recording the Sensitive disposition instead.",
        Category = "Diagnostics",
        DefaultValue = "true")]
    public bool RedactSensitiveValues { get; set; } = true;

    public override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.AddExecutionEvidence(options =>
        {
            options.MaxRecordsPerWorkflow = MaxRecordsPerWorkflow;
            options.MaxWorkflows = MaxWorkflows;
            options.MaxTrackedCheckpointsPerWorkflow = MaxTrackedCheckpointsPerWorkflow;
            options.MaxInlineValueLength = MaxInlineValueLength;
            options.RedactSensitiveValues = RedactSensitiveValues;
        });
    }
}
