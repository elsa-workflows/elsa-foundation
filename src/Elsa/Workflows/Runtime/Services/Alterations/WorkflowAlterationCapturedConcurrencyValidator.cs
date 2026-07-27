using System.Security.Cryptography;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;

namespace Elsa.Workflows.Runtime.Services.Alterations;

/// <summary>Creates and validates the immutable optimistic facts used by activity-affecting alterations.</summary>
internal static class WorkflowAlterationCapturedConcurrencyValidator
{
    public static string WorkflowRevision(WorkflowExecutionState workflow) =>
        workflow.UpdatedAt?.UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;

    public static IReadOnlyDictionary<string, string> CaptureActivities(IReadOnlyCollection<ActivityExecutionState> activities) =>
        activities.ToDictionary(activity => activity.Execution.ActivityExecutionId, ActivityRevision, StringComparer.Ordinal);

    public static WorkflowAlterationSafeFailure? ValidateWorkflow(
        WorkflowAlterationJobState job,
        WorkflowExecutionState workflow)
    {
        var captured = job.CapturedConcurrency;
        if (captured is null)
            return null; // Plans admitted before captured-concurrency facts existed remain safely compatible.
        if (!StringComparer.Ordinal.Equals(captured.WorkflowStateRevision ?? string.Empty, WorkflowRevision(workflow)))
            return new WorkflowAlterationSafeFailure("WorkflowStateRevisionConflict", "The workflow changed after target capture.");
        if (captured.PinnedExecutable is not null && !Equals(captured.PinnedExecutable, workflow.PinnedExecutable))
            return new WorkflowAlterationSafeFailure("PinnedExecutableConflict", "The workflow executable changed after target capture.");
        return null;
    }

    public static WorkflowAlterationSafeFailure? ValidateActivity(
        WorkflowAlterationJobState job,
        ActivityExecutionState activity,
        string role)
    {
        var captured = job.CapturedConcurrency;
        if (captured is null)
            return null;
        if (captured.ActivityStateRevisions is null ||
            !captured.ActivityStateRevisions.TryGetValue(activity.Execution.ActivityExecutionId, out var revision) ||
            !StringComparer.Ordinal.Equals(revision, ActivityRevision(activity)))
        {
            return new WorkflowAlterationSafeFailure(
                "ActivityStateRevisionConflict",
                $"The {role} activity changed after target capture.");
        }

        return null;
    }

    private static string ActivityRevision(ActivityExecutionState activity)
    {
        using var document = JsonSerializer.SerializeToDocument(activity);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            WriteCanonical(document.RootElement, writer);
        return Convert.ToHexStringLower(SHA256.HashData(stream.GetBuffer().AsSpan(0, checked((int)stream.Length))));
    }

    private static void WriteCanonical(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(property.Value, writer);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                    WriteCanonical(item, writer);
                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }
}
