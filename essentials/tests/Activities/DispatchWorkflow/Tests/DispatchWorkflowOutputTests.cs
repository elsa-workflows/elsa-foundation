using System.Text.Json;
using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Activities.DispatchWorkflow.Runtime.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.DispatchWorkflow.Tests;

/// <summary>
/// Spec 1245 FR-001 to FR-003 on the current core: the real <c>WorkflowOutputSource</c> with default diagnostics settings
/// carries a completed child's committed outputs into <c>Output</c>, unchanged, and leaves protected ones out with a
/// warning that names them.
/// </summary>
public sealed class DispatchWorkflowOutputTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Completed_child_output_arrives_by_name()
    {
        var (activity, result) = await RunWaitedAsync("decision", new() { ["Decision"] = Json("approved") });

        Assert.Equal(DispatchWorkflowOutcomes.Completed, result.Status);
        Assert.Equal("""{"Decision":"approved"}""", result.Output!.Value.GetRawText());
        Assert.Null(activity.Warnings);
    }

    [Fact]
    public async Task Long_string_and_secret_named_output_arrive_unchanged_under_default_diagnostics()
    {
        var summary = new string('s', 1000);

        var (_, result) = await RunWaitedAsync("unchanged", new() { ["Summary"] = Json(summary), ["token"] = Json("abc-123") });

        Assert.Equal(summary, result.Output!.Value.GetProperty("Summary").GetString());
        Assert.Equal("abc-123", result.Output.Value.GetProperty("token").GetString());
    }

    [Fact]
    public async Task Only_the_outputs_the_child_set_appear()
    {
        // The child declares nothing it does not set; a second, unset name never appears.
        var (_, result) = await RunWaitedAsync("one-of-two", new() { ["Set"] = Json(1) });

        Assert.Equal(["Set"], result.Output!.Value.EnumerateObject().Select(property => property.Name));
    }

    [Fact]
    public async Task Child_without_outputs_gives_an_empty_object()
    {
        var (_, result) = await RunWaitedAsync("none", new());

        Assert.Equal("{}", result.Output!.Value.GetRawText());
    }

    [Theory]
    [InlineData(RuntimeMetadataKeys.IsSensitive, "true", "\"held\"")]
    [InlineData(RuntimeMetadataKeys.RequiresEncryption, "true", "null")]
    [InlineData(RuntimeMetadataKeys.RedactionMode, "mask", "\"held\"")]
    [InlineData(RuntimeMetadataKeys.RetentionPolicy, "short", "\"held\"")]
    public async Task Protected_child_output_is_left_out_with_one_warning_that_names_it(string key, string marker, string value)
    {
        var (activity, result) = await RunWaitedAsync(
            "protected-" + key.Replace('.', '-'),
            new() { ["Plain"] = Json("visible") },
            seedChildRows: childId => [ProtectedRow(childId, "Zeta", key, marker, value), ProtectedRow(childId, "Alpha", key, marker, value)]);

        Assert.Equal("""{"Plain":"visible"}""", result.Output!.Value.GetRawText());
        var warning = Assert.Single(activity.Warnings!);
        Assert.Equal(DispatchWorkflowDiagnostics.ChildOutputWithheldCode, warning.Code);
        Assert.Equal(Elsa.Expressions.Core.Models.ExecutionWarningSeverity.Warning, warning.Severity);
        Assert.Equal("Alpha, Zeta", warning.Details![DispatchWorkflowDiagnostics.WithheldOutputsKey]);
        Assert.DoesNotContain("held", warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("held", JsonSerializer.Serialize(warning.Details), StringComparison.Ordinal);
        Assert.DoesNotContain("\"held\"", activity.Completion!.Result.InlineValue!.Value.GetRawText(), StringComparison.Ordinal);
    }

    private static async Task<(ActivityExecutionState Activity, DispatchWorkflowActivityResult Result)> RunWaitedAsync(
        string caseId,
        Dictionary<string, JsonElement> childOutputs,
        Func<string, DurableValueState[]>? seedChildRows = null)
    {
        await using var fixture = await DispatchWorkflowRuntimeTestFixture.CreateAsync(childOutputs: childOutputs);
        var run = await fixture.StartParentAsync(
            caseId: "output-" + caseId,
            parentWorkflowExecutionId: "parent-output-" + caseId,
            parentCorrelationId: "correlation-parent",
            waitForCompletion: true);
        Assert.Equal(ActivityExecutionStatus.Suspended, run.Activity.Status);

        if (seedChildRows is not null)
        {
            var store = fixture.Services.GetRequiredService<IDurableValueStateStore>();
            foreach (var row in seedChildRows(run.Identity.ChildWorkflowExecutionId))
                await store.SaveAsync(row);
        }

        await fixture.SweepAsync();
        Assert.Equal(WorkflowExecutionStatus.Completed, (await fixture.FindWorkflowAsync(run.Identity.ChildWorkflowExecutionId))?.Status);
        await fixture.SweepAsync();
        Assert.Equal(WorkflowExecutionStatus.Completed, (await fixture.FindWorkflowAsync(run.Start.WorkflowExecutionId))?.Status);

        var activity = Assert.Single(await fixture.ListActivitiesAsync(run.Start.WorkflowExecutionId));
        var inline = activity.Completion?.Result.InlineValue;
        Assert.NotNull(inline);
        return (activity, Assert.IsType<DispatchWorkflowActivityResult>(inline.Value.Deserialize<DispatchWorkflowActivityResult>(SerializerOptions)));
    }

    // A child output row carrying a protection policy, as Set Output writes one from a protected source.
    private static DurableValueState ProtectedRow(string childId, string name, string key, string marker, string value) =>
        new(
            durableValueId: $"durable-seeded-output:{name}",
            workflowExecutionId: childId,
            valueId: "output:" + name,
            type: new RuntimeValueTypeDescriptor("clr", "System.String", null),
            lifecycle: DurableValueLifecycle.Instance,
            storage: DurableValueStorage.Inline,
            inlineValue: JsonDocument.Parse(value).RootElement,
            externalReference: null,
            sourceActivityExecutionId: null,
            capturedAt: DispatchWorkflowRuntimeTestFixture.Now,
            metadata: new Dictionary<string, string>(StringComparer.Ordinal) { [RuntimeMetadataKeys.OutputName] = name, [key] = marker });

    private static JsonElement Json<T>(T value) => JsonSerializer.SerializeToElement(value);
}
