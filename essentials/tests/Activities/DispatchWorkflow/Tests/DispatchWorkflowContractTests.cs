using System.Reflection;
using Elsa.Activities.DispatchWorkflow.Design;
using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Activities.DispatchWorkflow.Runtime.Models;
using Elsa.Activities.Runtime.Core;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Design.Reconciliation.Clr.Services;
using Elsa.Activities.Design.Core.Reconciliation.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Activity = Elsa.Activities.DispatchWorkflow.Runtime.Activities.DispatchWorkflow;

namespace Elsa.Activities.DispatchWorkflow.Tests;

public sealed class DispatchWorkflowContractTests
{
    [Fact]
    public void Design_feature_remains_inheritable_with_virtual_configuration()
    {
        AssertFeatureCanBeSpecialized(typeof(DispatchWorkflowDesignFeature));
    }

    [Fact]
    public void Activity_exposes_stable_schema_defaults_editor_and_outcomes()
    {
        Assert.Equal(DispatchWorkflowConstants.ActivityType, Activity.ActivityType);

        var inputs = typeof(Activity).GetProperties()
            .Where(property => property.GetCustomAttribute<ActivityInputAttribute>() is not null)
            .ToDictionary(property => property.Name, StringComparer.Ordinal);
        var outputs = typeof(DispatchWorkflowActivityResult).GetProperties()
            .Where(property => property.GetCustomAttribute<OutputAttribute>() is not null)
            .ToDictionary(property => property.Name, StringComparer.Ordinal);

        Assert.Equal(
            new[] { nameof(Activity.WorkflowDefinitionId), nameof(Activity.Inputs), nameof(Activity.WaitForCompletion), nameof(Activity.CancelChildOnParentCancellation), nameof(Activity.CorrelationId) },
            inputs.Keys.OrderBy(InputOrder).ToArray());
        Assert.Equal(
            new[] { nameof(DispatchWorkflowActivityResult.ChildWorkflowExecutionId), nameof(DispatchWorkflowActivityResult.Result) },
            outputs.Keys.Order(StringComparer.Ordinal).ToArray());

        var workflowId = inputs[nameof(Activity.WorkflowDefinitionId)].GetCustomAttribute<ActivityInputAttribute>();
        Assert.NotNull(workflowId);
        Assert.Equal(ActivityInputUIHints.Dropdown, workflowId!.UIHint);
        Assert.Equal(DispatchWorkflowConstants.WorkflowDefinitionOptionsKey, workflowId.OptionsProvider);

        Assert.Equal("false", inputs[nameof(Activity.WaitForCompletion)].GetCustomAttribute<ActivityInputAttribute>()?.DefaultValue);
        Assert.Equal("true", inputs[nameof(Activity.CancelChildOnParentCancellation)].GetCustomAttribute<ActivityInputAttribute>()?.DefaultValue);
        Assert.Equal(typeof(string), outputs[nameof(DispatchWorkflowActivityResult.ChildWorkflowExecutionId)].PropertyType);
        Assert.Equal(typeof(DispatchWorkflowResult), outputs[nameof(DispatchWorkflowActivityResult.Result)].PropertyType);

        Assert.Equal(
            new[] { "Dispatched", "Completed", "Faulted", "Cancelled", "DispatchFailed" },
            typeof(Activity).GetCustomAttributes<ActivityOutcomeAttribute>().Select(attribute => attribute.Key).ToArray());
    }

    [Fact]
    public void Clr_catalog_discovers_one_dispatch_workflow_contract()
    {
        var folder = Directory.CreateTempSubdirectory("dispatch-workflow-catalog-");
        try
        {
            File.Copy(typeof(Activity).Assembly.Location, Path.Join(folder.FullName, Path.GetFileName(typeof(Activity).Assembly.Location)));
            var scanner = new ClrAssemblyScanner(
                new ActivityTypeVersionResolver(),
                new ActivityTypeCategoryResolver(),
                NullLogger<ClrAssemblyScanner>.Instance);

            var model = Assert.Single(scanner.Scan(folder.FullName), candidate => candidate.ActivityTypeKey == typeof(Activity).FullName);

            Assert.Equal(5, model.Inputs.Count());
            Assert.Equal(2, model.Outputs.Count());
            var workflowId = model.Inputs.Single(input => input.Name == nameof(Activity.WorkflowDefinitionId));
            Assert.Equal(ActivityInputUIHints.Dropdown, workflowId.UiHint);
            Assert.Equal(
                DispatchWorkflowConstants.WorkflowDefinitionOptionsKey,
                workflowId.UISpecifications?.GetProperty("optionsProvider").GetProperty("key").GetString());
            Assert.False(model.Inputs.Single(input => input.Name == nameof(Activity.WaitForCompletion)).DefaultValue?.GetBoolean());
            Assert.True(model.Inputs.Single(input => input.Name == nameof(Activity.CancelChildOnParentCancellation)).DefaultValue?.GetBoolean());
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public void Wait_mode_uses_stable_nonexpiring_bookmark_contract()
    {
        Assert.Equal("Elsa.Activities.DispatchWorkflow.ChildCompleted", DispatchWorkflowConstants.WaitStimulusType);
        Assert.Equal("resume-target:dispatch-workflow-completed", DispatchWorkflowConstants.CompletionResumeTargetId);

        var identity = new WorkflowDispatchIdentity("parent-1", "activity-1");
        Assert.StartsWith("bookmark:dispatch-wait:v1:", identity.WaitBookmarkId, StringComparison.Ordinal);
        Assert.StartsWith("stimulus:dispatch-wait:v1:", identity.WaitStimulusHash, StringComparison.Ordinal);
    }

    [Fact]
    public void Result_snapshots_json_safe_terminal_output_contract()
    {
        var metadata = new Dictionary<string, string> { ["trace"] = "abc" };
        var outputs = new[]
        {
            new DispatchWorkflowOutput("Total", System.Text.Json.JsonSerializer.SerializeToElement(42), "System.Int32", false)
        };

        var result = new DispatchWorkflowResult("child-1", WorkflowDispatchStatus.Completed, outputs, metadata);
        metadata["trace"] = "mutated";

        Assert.Equal("child-1", result.ChildWorkflowExecutionId);
        Assert.Equal(WorkflowDispatchStatus.Completed, result.Status);
        Assert.Equal("abc", result.DiagnosticMetadata["trace"]);
        Assert.Equal(42, Assert.Single(result.Outputs).Value?.GetInt32());
    }

    [Theory]
    [InlineData(WorkflowDispatchStatus.Faulted)]
    [InlineData(WorkflowDispatchStatus.Cancelled)]
    public void NonSuccessResult_RejectsOutputsAndCallerSuppliedDiagnostics(WorkflowDispatchStatus status)
    {
        var output = new DispatchWorkflowOutput(
            "partial",
            System.Text.Json.JsonSerializer.SerializeToElement("secret-output"),
            "System.String",
            false);

        Assert.Throws<ArgumentException>(() => new DispatchWorkflowResult("child-1", status, [output]));
        Assert.Throws<ArgumentException>(() => new DispatchWorkflowResult(
            "child-1",
            status,
            diagnosticMetadata: new Dictionary<string, string>
            {
                ["exceptionType"] = "SecretException",
                ["stackTrace"] = "secret stack",
                ["message"] = "secret message"
            }));
    }

    [Theory]
    [InlineData(WorkflowDispatchStatus.Completed)]
    [InlineData(WorkflowDispatchStatus.Faulted)]
    [InlineData(WorkflowDispatchStatus.Cancelled)]
    [InlineData(WorkflowDispatchStatus.DispatchFailed)]
    public void ParentResumePayload_AcceptsExactlySupportedChildTerminalStatuses(WorkflowDispatchStatus status)
    {
        var identity = new WorkflowDispatchIdentity("parent-1", "activity-1");
        var result = status switch
        {
            WorkflowDispatchStatus.Faulted => new DispatchWorkflowResult(
                identity.ChildWorkflowExecutionId,
                status,
                diagnosticMetadata: new Dictionary<string, string>
                {
                    [DispatchWorkflowDiagnostics.CodeKey] = DispatchWorkflowDiagnostics.FaultedCode,
                    [DispatchWorkflowDiagnostics.CategoryKey] = DispatchWorkflowDiagnostics.Category,
                    [DispatchWorkflowDiagnostics.SummaryKey] = DispatchWorkflowDiagnostics.FaultedSummary,
                    [DispatchWorkflowDiagnostics.IncidentCountKey] = "1",
                    [DispatchWorkflowDiagnostics.IncidentIdsTruncatedKey] = "false",
                    ["incidentId.000"] = "incident-a"
                }),
            WorkflowDispatchStatus.Cancelled => new DispatchWorkflowResult(
                identity.ChildWorkflowExecutionId,
                status,
                diagnosticMetadata: new Dictionary<string, string>
                {
                    [DispatchWorkflowDiagnostics.CodeKey] = DispatchWorkflowDiagnostics.CancelledCode,
                    [DispatchWorkflowDiagnostics.CategoryKey] = DispatchWorkflowDiagnostics.Category,
                    [DispatchWorkflowDiagnostics.SummaryKey] = DispatchWorkflowDiagnostics.CancelledSummary
                }),
            WorkflowDispatchStatus.DispatchFailed => new DispatchWorkflowResult(
                identity.ChildWorkflowExecutionId,
                status,
                diagnosticMetadata: new Dictionary<string, string>
                {
                    [DispatchWorkflowDiagnostics.CodeKey] = DispatchWorkflowDiagnostics.DispatchFailedCode,
                    [DispatchWorkflowDiagnostics.CategoryKey] = DispatchWorkflowDiagnostics.DeliveryCategory,
                    [DispatchWorkflowDiagnostics.SummaryKey] = DispatchWorkflowDiagnostics.DispatchFailedSummary,
                    [DispatchWorkflowDiagnostics.DeliveryIncidentIdKey] = identity.DeliveryIncidentId(0)
                }),
            _ => new DispatchWorkflowResult(identity.ChildWorkflowExecutionId, status)
        };

        var payload = new WorkflowDispatchParentResumePayload(
            identity.DispatchId,
            "parent-1",
            "activity-1",
            identity.ChildWorkflowExecutionId,
            identity.WaitBookmarkId,
            DispatchWorkflowConstants.WaitStimulusType,
            identity.WaitStimulusHash,
            result);

        Assert.Equal(status, payload.Result.Status);
    }

    [Fact]
    public void DispatchFailedResult_RejectsOutputsAndNoncanonicalOrRawDiagnostics()
    {
        var output = new DispatchWorkflowOutput(
            "partial",
            System.Text.Json.JsonSerializer.SerializeToElement("secret"),
            "System.String",
            false);
        Assert.Throws<ArgumentException>(() => new DispatchWorkflowResult(
            "child-1",
            WorkflowDispatchStatus.DispatchFailed,
            [output],
            DispatchFailedDiagnostics("incident-1")));

        var raw = DispatchFailedDiagnostics("incident-1")
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        raw["exception"] = "secret provider reason";
        Assert.Throws<ArgumentException>(() => new DispatchWorkflowResult(
            "child-1",
            WorkflowDispatchStatus.DispatchFailed,
            diagnosticMetadata: raw));
    }

    private static IReadOnlyDictionary<string, string> DispatchFailedDiagnostics(string incidentId) =>
        new Dictionary<string, string>
        {
            [DispatchWorkflowDiagnostics.CodeKey] = DispatchWorkflowDiagnostics.DispatchFailedCode,
            [DispatchWorkflowDiagnostics.CategoryKey] = DispatchWorkflowDiagnostics.DeliveryCategory,
            [DispatchWorkflowDiagnostics.SummaryKey] = DispatchWorkflowDiagnostics.DispatchFailedSummary,
            [DispatchWorkflowDiagnostics.DeliveryIncidentIdKey] = incidentId
        };

    private static void AssertFeatureCanBeSpecialized(Type featureType)
    {
        Assert.False(featureType.IsSealed);
        var configureServices = Assert.Single(
            featureType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly),
            method => method.Name == "ConfigureServices");
        Assert.True(configureServices.IsVirtual);
        Assert.False(configureServices.IsFinal);
    }

    private static int InputOrder(string name) => name switch
    {
        nameof(Activity.WorkflowDefinitionId) => 0,
        nameof(Activity.Inputs) => 1,
        nameof(Activity.WaitForCompletion) => 2,
        nameof(Activity.CancelChildOnParentCancellation) => 3,
        nameof(Activity.CorrelationId) => 4,
        _ => int.MaxValue
    };
}
