using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>One authored notification, interpreted only by its declared control consumer.</summary>
public sealed class RuntimeChildControlNotification
{
    public const int MaximumPayloadBytes = 16 * 1024;

    public RuntimeChildControlNotification(string code, JsonElement? payload = null)
    {
        RuntimeChildControlValidation.Text(code, nameof(code), 1024);
        if (payload is { ValueKind: JsonValueKind.Undefined })
            throw new ArgumentException("A control notification payload must be valid JSON.", nameof(payload));
        if (payload is { } value && Encoding.UTF8.GetByteCount(value.GetRawText()) > MaximumPayloadBytes)
            throw new ArgumentException("A control notification payload exceeds its byte limit.", nameof(payload));

        Code = code;
        Payload = payload?.Clone();
    }

    public string Code { get; }
    public JsonElement? Payload { get; }
}

/// <summary>
/// One indivisible authored-control emission from a workflow root. This contains no runtime fault or incident.
/// Dispatch authority and immutable source evidence are validated separately from this payload.
/// </summary>
[JsonConverter(typeof(RuntimeChildControlBatchJsonConverter))]
public sealed class RuntimeChildControlBatch
{
    public const int CurrentVersion = 1;
    public const int MaximumNotifications = 32;
    public const int MaximumContentBytes = 64 * 1024;

    public RuntimeChildControlBatch(
        int version,
        string consumerKey,
        int consumerVersion,
        string childWorkflowExecutionId,
        string rootActivityExecutionId,
        string sourceWorkItemId,
        long ordinal,
        bool isTerminal,
        IReadOnlyList<RuntimeChildControlNotification>? notifications = null,
        string? authoredFailureCode = null)
    {
        if (version != CurrentVersion)
            throw new ArgumentOutOfRangeException(nameof(version), version, "Unsupported child-control batch version.");
        RuntimeChildControlValidation.Text(consumerKey, nameof(consumerKey), 128);
        if (consumerVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(consumerVersion));
        RuntimeChildControlValidation.Text(childWorkflowExecutionId, nameof(childWorkflowExecutionId));
        RuntimeChildControlValidation.Text(rootActivityExecutionId, nameof(rootActivityExecutionId));
        RuntimeChildControlValidation.Text(sourceWorkItemId, nameof(sourceWorkItemId));
        if (ordinal < 0)
            throw new ArgumentOutOfRangeException(nameof(ordinal));
        if (authoredFailureCode is not null)
        {
            RuntimeChildControlValidation.Text(authoredFailureCode, nameof(authoredFailureCode), 1024);
            if (!isTerminal)
                throw new ArgumentException("Authored failure classification requires a terminal control batch.", nameof(authoredFailureCode));
        }
        if (notifications?.Count > MaximumNotifications)
            throw new ArgumentException("A control batch exceeds its notification limit.", nameof(notifications));

        var snapshot = notifications?.ToArray() ?? [];
        if (snapshot.Any(notification => notification is null))
            throw new ArgumentException("Control notifications cannot contain null entries.", nameof(notifications));
        var contentBytes = Encoding.UTF8.GetByteCount(consumerKey) + Encoding.UTF8.GetByteCount(childWorkflowExecutionId) +
                           Encoding.UTF8.GetByteCount(rootActivityExecutionId) + Encoding.UTF8.GetByteCount(sourceWorkItemId) +
                           Encoding.UTF8.GetByteCount(authoredFailureCode ?? "");
        foreach (var notification in snapshot)
            contentBytes += Encoding.UTF8.GetByteCount(notification.Code) +
                            (notification.Payload is { } payload ? Encoding.UTF8.GetByteCount(payload.GetRawText()) : 0);
        if (contentBytes > MaximumContentBytes)
            throw new ArgumentException("A control batch exceeds its content byte limit.", nameof(notifications));
        if (!isTerminal && snapshot.Length == 0)
            throw new ArgumentException("A nonterminal control batch must contain a notification.", nameof(notifications));

        Version = version;
        ConsumerKey = consumerKey;
        ConsumerVersion = consumerVersion;
        ChildWorkflowExecutionId = childWorkflowExecutionId;
        RootActivityExecutionId = rootActivityExecutionId;
        SourceWorkItemId = sourceWorkItemId;
        Ordinal = ordinal;
        IsTerminal = isTerminal;
        Notifications = Array.AsReadOnly(snapshot);
        AuthoredFailureCode = authoredFailureCode;
    }

    public int Version { get; }
    public string ConsumerKey { get; }
    public int ConsumerVersion { get; }
    public string ChildWorkflowExecutionId { get; }
    public string RootActivityExecutionId { get; }
    public string SourceWorkItemId { get; }
    public long Ordinal { get; }
    public bool IsTerminal { get; }
    public IReadOnlyList<RuntimeChildControlNotification> Notifications { get; }
    public string? AuthoredFailureCode { get; }
}

/// <summary>
/// Exact dispatch target and source-outbox lookup for a control batch. Shape validation grants no delivery authority;
/// the receiver must compare this envelope against the retained dispatch, executable and source outbox payload.
/// </summary>
public sealed class RuntimeChildControlTransport
{
    public RuntimeChildControlTransport(
        string dispatchId,
        string parentWorkflowExecutionId,
        string callingActivityExecutionId,
        string consumerActivityExecutionId,
        WorkflowExecutableIdentity parentExecutable,
        string sourceCommitId,
        string sourceIntentId,
        string sourceOutboxItemId,
        RuntimeChildControlBatch batch)
    {
        RuntimeChildControlValidation.Text(dispatchId, nameof(dispatchId));
        RuntimeChildControlValidation.Text(parentWorkflowExecutionId, nameof(parentWorkflowExecutionId));
        RuntimeChildControlValidation.Text(callingActivityExecutionId, nameof(callingActivityExecutionId));
        RuntimeChildControlValidation.Text(consumerActivityExecutionId, nameof(consumerActivityExecutionId));
        RuntimeChildControlValidation.Pin(parentExecutable);
        RuntimeChildControlValidation.Text(sourceCommitId, nameof(sourceCommitId));
        RuntimeChildControlValidation.Text(sourceIntentId, nameof(sourceIntentId));
        RuntimeChildControlValidation.Text(sourceOutboxItemId, nameof(sourceOutboxItemId), 8193);
        ArgumentNullException.ThrowIfNull(batch);
        var identity = new WorkflowDispatchIdentity(parentWorkflowExecutionId, callingActivityExecutionId);
        if (!StringComparer.Ordinal.Equals(identity.DispatchId, dispatchId) ||
            !StringComparer.Ordinal.Equals(identity.ChildWorkflowExecutionId, batch.ChildWorkflowExecutionId))
            throw new ArgumentException("Child-control transport does not match its deterministic dispatch identity.", nameof(dispatchId));
        if (StringComparer.Ordinal.Equals(callingActivityExecutionId, consumerActivityExecutionId))
            throw new ArgumentException("A dispatch invocation cannot consume its own parent control.", nameof(consumerActivityExecutionId));
        if (!StringComparer.Ordinal.Equals(sourceOutboxItemId, RuntimePostCommitOutboxIdentity.CreateLogicalValue(sourceCommitId, sourceIntentId)))
            throw new ArgumentException("Child-control source outbox identity does not match its commit and intent.", nameof(sourceOutboxItemId));

        DispatchId = dispatchId;
        ParentWorkflowExecutionId = parentWorkflowExecutionId;
        CallingActivityExecutionId = callingActivityExecutionId;
        ConsumerActivityExecutionId = consumerActivityExecutionId;
        ParentExecutable = parentExecutable;
        SourceCommitId = sourceCommitId;
        SourceIntentId = sourceIntentId;
        SourceOutboxItemId = sourceOutboxItemId;
        Batch = batch;
    }

    public string DispatchId { get; }
    public string ParentWorkflowExecutionId { get; }
    public string CallingActivityExecutionId { get; }
    public string ConsumerActivityExecutionId { get; }
    public WorkflowExecutableIdentity ParentExecutable { get; }
    public string SourceCommitId { get; }
    public string SourceIntentId { get; }
    public string SourceOutboxItemId { get; }
    public RuntimeChildControlBatch Batch { get; }
}

/// <summary>Persisted control must declare its complete authored content before constructor defaults apply.</summary>
internal sealed class RuntimeChildControlBatchJsonConverter : JsonConverter<RuntimeChildControlBatch>
{
    public RuntimeChildControlBatchJsonConverter() { }

    public override RuntimeChildControlBatch Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var members = ReadMembers(document.RootElement, options);
        var notifications = Required(members, nameof(WireBatch.Notifications), options);
        if (notifications.ValueKind != JsonValueKind.Array)
            throw new JsonException("A persisted child-control batch must declare its notification array.");
        _ = Required(members, nameof(WireBatch.AuthoredFailureCode), options);
        foreach (var notification in notifications.EnumerateArray())
            _ = Required(ReadMembers(notification, options), nameof(RuntimeChildControlNotification.Payload), options);
        var wire = document.RootElement.Deserialize<WireBatch>(options)
            ?? throw new JsonException("A child-control batch must be an object.");
        if (wire.Ordinal is null || wire.IsTerminal is null)
            throw new JsonException("A child-control batch must declare its ordinal and terminal status.");
        return new RuntimeChildControlBatch(wire.Version, wire.ConsumerKey, wire.ConsumerVersion,
            wire.ChildWorkflowExecutionId, wire.RootActivityExecutionId, wire.SourceWorkItemId,
            wire.Ordinal.Value, wire.IsTerminal.Value, wire.Notifications, wire.AuthoredFailureCode);
    }

    private static Dictionary<string, JsonElement> ReadMembers(JsonElement value, JsonSerializerOptions options)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw new JsonException("A child-control record must be an object.");
        var members = new Dictionary<string, JsonElement>(options.PropertyNameCaseInsensitive
            ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
            if (!members.TryAdd(property.Name, property.Value))
                throw new JsonException("A child-control record cannot shadow a persisted member.");
        return members;
    }

    private static JsonElement Required(Dictionary<string, JsonElement> members, string name, JsonSerializerOptions options)
    {
        var wireName = options.PropertyNamingPolicy?.ConvertName(name) ?? name;
        return members.TryGetValue(wireName, out var value) ? value
            : throw new JsonException($"A child-control record must declare '{wireName}', including explicit null values.");
    }

    public override void Write(Utf8JsonWriter writer, RuntimeChildControlBatch value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, new WireBatch(value.Version, value.ConsumerKey, value.ConsumerVersion,
            value.ChildWorkflowExecutionId, value.RootActivityExecutionId, value.SourceWorkItemId,
            value.Ordinal, value.IsTerminal, value.Notifications, value.AuthoredFailureCode), options);

    private sealed record WireBatch(int Version, string ConsumerKey, int ConsumerVersion,
        string ChildWorkflowExecutionId, string RootActivityExecutionId, string SourceWorkItemId,
        long? Ordinal, bool? IsTerminal, IReadOnlyList<RuntimeChildControlNotification>? Notifications,
        string? AuthoredFailureCode);
}

internal static class RuntimeChildControlValidation
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static void Pin(WorkflowExecutableIdentity parentExecutable)
    {
        ArgumentNullException.ThrowIfNull(parentExecutable);
        Text(parentExecutable.ArtifactId, "parentExecutable.ArtifactId");
        Text(parentExecutable.DefinitionId, "parentExecutable.DefinitionId");
        Text(parentExecutable.DefinitionVersionId, "parentExecutable.DefinitionVersionId");
        Text(parentExecutable.ArtifactVersion, "parentExecutable.ArtifactVersion", 128);
        Text(parentExecutable.ArtifactHash, "parentExecutable.ArtifactHash", 1024);
    }

    public static void Text(string value, string parameterName, int maximumBytes = 4096)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > maximumBytes)
            throw new ArgumentException("Child-control text exceeds its byte limit.", parameterName);
        try
        {
            if (StrictUtf8.GetByteCount(value) > maximumBytes)
                throw new ArgumentException("Child-control text exceeds its byte limit.", parameterName);
        }
        catch (EncoderFallbackException exception)
        {
            throw new ArgumentException("Child-control text must be well-formed Unicode.", parameterName, exception);
        }
    }
}
