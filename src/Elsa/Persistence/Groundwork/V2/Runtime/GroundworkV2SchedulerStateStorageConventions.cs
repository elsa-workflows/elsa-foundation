using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Store;

namespace Elsa.Persistence.Groundwork.Runtime;

/// <summary>Owns the current scheduler-state row envelope and its collection projection.</summary>
internal static class GroundworkV2SchedulerStateStorageConventions
{
    private static readonly JsonSerializerOptions StateJsonOptions = CreateStateJsonOptions();

    public static StorageValues Values(SchedulerState state)
    {
        Validate(state);
        return GroundworkRuntimeRowStore.Values(
            state.WorkflowExecutionId,
            ElsaRuntimeV2StorageManifest.SchemaVersion,
            GroundworkV2RuntimeJson.Serialize(state),
            Projections(state));
    }

    public static IReadOnlyDictionary<string, object?> Projections(SchedulerState state)
    {
        Validate(state);
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [ElsaRuntimeV2StorageManifest.CollectionField] =
                ElsaRuntimeV2StorageManifest.SchedulerStateDocumentKind
        };
    }

    public static SchedulerState Deserialize(IReadOnlyDictionary<string, object?> values)
    {
        var schemaVersion = RequiredString(values, ElsaRuntimeV2StorageManifest.SchemaVersionField);
        if (!StringComparer.Ordinal.Equals(schemaVersion, ElsaRuntimeV2StorageManifest.SchemaVersion))
        {
            throw new InvalidDataException(
                $"Groundwork scheduler-state row returned unsupported schema version '{schemaVersion}'; " +
                $"this adapter requires '{ElsaRuntimeV2StorageManifest.SchemaVersion}'.");
        }

        var content = values.TryGetValue(ElsaRuntimeV2StorageManifest.ContentField, out var rawContent)
            ? rawContent switch
            {
                string text => text,
                JsonElement element => element.GetRawText(),
                JsonDocument document => document.RootElement.GetRawText(),
                _ => throw new InvalidDataException("Groundwork scheduler-state row content is not JSON.")
            }
            : throw new InvalidDataException("Groundwork scheduler-state row did not contain JSON content.");

        SchedulerState state;
        try
        {
            state = DeserializeCurrentState(content);
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException
                                          or NotSupportedException
                                          or ArgumentException
                                          or InvalidOperationException
                                          or KeyNotFoundException
                                          or FormatException
                                          or OverflowException)
        {
            throw new InvalidDataException(
                "Groundwork scheduler-state row content was not valid current JSON.",
                exception);
        }

        Validate(state);
        EnsureProjection(values, ElsaRuntimeV2StorageManifest.IdField, state.WorkflowExecutionId);
        foreach (var (field, expected) in Projections(state))
            EnsureProjection(values, field, (string)expected!);
        return state;
    }

    private static SchedulerState DeserializeCurrentState(string content)
    {
        using var document = JsonDocument.Parse(content);
        var root = document.RootElement;
        var workflowExecutionId = RequiredProperty(root, "workflowExecutionId").GetString();
        if (string.IsNullOrWhiteSpace(workflowExecutionId))
            throw new JsonException("Scheduler-state workflow execution ID is missing.");

        var version = RequiredProperty(root, "version").GetInt64();
        var pendingWork = ReadCollection<ScheduledActivityWorkItem>(root, "pendingWork");
        var pendingCompletionWork = ReadCollection<SchedulerCompletionWorkItem>(root, "pendingCompletionWork");
        var pendingContinuations = ReadCollection<SchedulerContinuationWorkItem>(root, "pendingContinuations");
        var volatileWaits = ReadCollection<VolatileWaitRegistration>(root, "volatileWaits");
        var activeGenerators = ReadCollection<GeneratorRegistration>(root, "activeGenerators");
        var pendingGeneratedEvents = ReadCollection<SchedulerGeneratedEventWorkItem>(root, "pendingGeneratedEvents");

        return new SchedulerState(
            workflowExecutionId,
            version,
            pendingWork,
            pendingContinuations,
            volatileWaits,
            pendingCompletionWork,
            activeGenerators,
            pendingGeneratedEvents);
    }

    private static IReadOnlyCollection<T> ReadCollection<T>(JsonElement root, string propertyName)
    {
        var value = RequiredProperty(root, propertyName);
        if (value.ValueKind != JsonValueKind.Array)
            throw new JsonException($"Scheduler-state property '{propertyName}' must be an array.");

        return JsonSerializer.Deserialize<T[]>(value.GetRawText(), StateJsonOptions)
               ?? throw new JsonException($"Scheduler-state property '{propertyName}' deserialized to null.");
    }

    private static JsonElement RequiredProperty(JsonElement root, string propertyName)
    {
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(propertyName, out var value))
            return value;

        throw new JsonException($"Scheduler-state content is missing property '{propertyName}'.");
    }

    private static JsonSerializerOptions CreateStateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    public static void Validate(SchedulerState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.WorkflowExecutionId);
    }

    private static void EnsureProjection(
        IReadOnlyDictionary<string, object?> values,
        string field,
        string expected)
    {
        if (!values.TryGetValue(field, out var actual) || !StringComparer.Ordinal.Equals(ReadString(actual), expected))
        {
            throw new InvalidDataException(
                $"Groundwork scheduler-state row projection '{field}' does not match its current content.");
        }
    }

    private static string? ReadString(object? value) => value switch
    {
        string text => text,
        JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
        _ => null
    };

    private static string RequiredString(IReadOnlyDictionary<string, object?> values, string field)
    {
        if (values.TryGetValue(field, out var value) && !string.IsNullOrWhiteSpace(ReadString(value)))
            return ReadString(value)!;

        throw new InvalidDataException(
            $"Groundwork scheduler-state row is missing required string field '{field}'.");
    }
}
