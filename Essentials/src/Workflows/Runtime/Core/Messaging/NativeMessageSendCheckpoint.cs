using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Messaging;

/// <summary>Detached authored send proposal. Scope/context are supplied by the runtime, never a client grant.</summary>
/// <remarks>No control incarnation, accepted receipt, source outcome or callback capability is created here.</remarks>
public sealed class NativeMessageSendRequest
{
    public NativeMessageSendRequest(PersistenceScope scope, string workflowExecutionId, string invocationId,
        string attemptId, string executableNodeId, string operationKey, string? targetOperationKey,
        DateTimeOffset originatedAt, MessageSubmission submission)
    {
        ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(submission);
        foreach (var value in new[] { workflowExecutionId, invocationId, attemptId, executableNodeId, operationKey })
            MessageIdentityValidation.RequireOpaqueText(value, "context");
        if (targetOperationKey is not null) MessageIdentityValidation.RequireOpaqueText(targetOperationKey, nameof(targetOperationKey));
        if (originatedAt == default) throw new ArgumentException("The originating invoke time is unavailable.");
        Scope = scope; WorkflowExecutionId = workflowExecutionId; InvocationId = invocationId; AttemptId = attemptId;
        ExecutableNodeId = executableNodeId; OperationKey = operationKey; TargetOperationKey = targetOperationKey;
        OriginatedAt = originatedAt.ToUniversalTime();
        Submission = new(submission.Name, submission.CorrelationKey, submission.Payload, submission.ProducerMessageId, submission.LifetimeSeconds);
    }
    public PersistenceScope Scope { get; }
    public string WorkflowExecutionId { get; }
    public string InvocationId { get; }
    public string AttemptId { get; }
    public string ExecutableNodeId { get; }
    public string OperationKey { get; }
    public string? TargetOperationKey { get; }
    public DateTimeOffset OriginatedAt { get; }
    public MessageSubmission Submission { get; }
    public override string ToString() => "NativeMessageSendRequest { [redacted] }";
}

/// <summary>Private typed suspension data; stable IDs alone authorize no receipt delivery.</summary>
public sealed class NativeMessageSendWaitState
{
    public NativeMessageSendWaitState(string intentId, string bookmarkId, string stimulusHash)
    {
        MessageIdentityValidation.RequireOpaqueText(intentId, nameof(intentId));
        MessageIdentityValidation.RequireOpaqueText(bookmarkId, nameof(bookmarkId));
        MessageIdentityValidation.RequireOpaqueText(stimulusHash, nameof(stimulusHash));
        IntentId = intentId; BookmarkId = bookmarkId; StimulusHash = stimulusHash;
    }
    public string IntentId { get; }
    public string BookmarkId { get; }
    public string StimulusHash { get; }
    public override string ToString() => "NativeMessageSendWaitState { [redacted] }";
}

/// <summary>Typed receipt/rejection data only. The target must reread the actual immutable SourceOutcome.</summary>
public sealed class NativeMessageSendResult
{
    public NativeMessageSendResult(MessageSubmissionResult? acceptance, MessageSubmissionFailureKind? rejection)
    {
        if ((acceptance is null) == (rejection is null) || rejection is { } kind && (!Enum.IsDefined(kind) || kind == MessageSubmissionFailureKind.Uncertain))
            throw new ArgumentException("A native send result requires one known acceptance or rejection.");
        Acceptance = acceptance; Rejection = rejection;
    }
    public MessageSubmissionResult? Acceptance { get; }
    public MessageSubmissionFailureKind? Rejection { get; }
    public override string ToString() => "NativeMessageSendResult { [redacted] }";
}

/// <summary>Activity-facing typed handoff. Staging performs no publication, persistence or acceptance.</summary>
public interface INativeMessageSendStager
{
    NativeMessageSendWaitState Stage(NativeMessageSendRequest request);
}

/// <summary>Engine-owned scope/invocation reset and destructive drain of transient proposals.</summary>
public interface INativeMessageSendStagingAccessor
{
    void Reset(PersistenceScope scope, string workflowExecutionId, string invocationId);
    NativeMessageSendRequest? Take(PersistenceScope scope, string workflowExecutionId, string invocationId);
}

/// <summary>Closed v1 physical-checkpoint proposal, never a committed source/control/acceptance proof.</summary>
/// <remarks>
/// Providers must reread actual sender/fence/material/source and provision the opaque sender control and retained
/// outcome slot in their own transaction. They must refuse until T10/T11, guarded callback and Q4 cancellation are
/// qualified. Native source outcomes remain immutable beyond producer dedup/expiry, including no producer ID.
/// This document uses an owned lossless string codec; old null checkpoint fields keep their historical wire.
/// </remarks>
[JsonConverter(typeof(NativeMessageSendCheckpointPlanConverter))]
public sealed class NativeMessageSendCheckpointPlan
{
    public const int CurrentVersion = 1;
    public const string ActivityType = "Elsa.SendMessage";
    public const string IntentKind = "Elsa.Message.NativeSend.v1";
    public const string ResumeTargetKey = "resume-target:message-send-receipt";
    public const string WaitStimulusType = "Elsa.Message.PrivateSendReceipt.v1";

    public NativeMessageSendCheckpointPlan(int version, NativeMessageSendRequest request, WorkflowExecutableIdentity pinnedExecutable,
        RuntimePostCommitIntent intent, string bookmarkId, string stimulusHash, string resumeTargetId,
        string outboxItemId, string authoredFingerprint, string intentFingerprint)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(pinnedExecutable); ArgumentNullException.ThrowIfNull(intent);
        foreach (var value in new[] { bookmarkId, stimulusHash, resumeTargetId, outboxItemId, authoredFingerprint, intentFingerprint })
            MessageIdentityValidation.RequireOpaqueText(value, "checkpoint");
        if (version != CurrentVersion || intent.Kind != IntentKind || intent.WorkflowExecutionId != request.WorkflowExecutionId ||
            intent.ActivityExecutionId != request.InvocationId || intent.RecordedAt != request.OriginatedAt ||
            intent.DependsOnWaitRegistrationId != bookmarkId || intent.WaitFailurePolicy != RuntimeWaitDependentIntentFailurePolicy.KeepWaitingManualIntervention ||
            intent.MaterializedSchedulerWorkItem is not null || intent.NotBefore is not null)
            throw new ArgumentException("The native send checkpoint proposal is incoherent or unsupported.");
        Version = version; Request = request; PinnedExecutable = pinnedExecutable; Intent = intent; BookmarkId = bookmarkId;
        StimulusHash = stimulusHash; ResumeTargetId = resumeTargetId; OutboxItemId = outboxItemId;
        AuthoredFingerprint = authoredFingerprint; IntentFingerprint = intentFingerprint;
    }
    public int Version { get; }
    public NativeMessageSendRequest Request { get; }
    public WorkflowExecutableIdentity PinnedExecutable { get; }
    public RuntimePostCommitIntent Intent { get; }
    public string BookmarkId { get; }
    public string StimulusHash { get; }
    public string ResumeTargetId { get; }
    public string OutboxItemId { get; }
    public string AuthoredFingerprint { get; }
    public string IntentFingerprint { get; }
    public override string ToString() => "NativeMessageSendCheckpointPlan { [redacted] }";
}

// Owned optional wire only, never a universal serializer change. Opaque server IDs retain every UTF-16 code unit.
internal sealed class NativeMessageSendCheckpointPlanConverter : JsonConverter<NativeMessageSendCheckpointPlan>
{
    private static readonly JsonSerializerOptions Wire = Options();
    public NativeMessageSendCheckpointPlanConverter() { }
    public override NativeMessageSendCheckpointPlan Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        try
        {
            using var json = JsonDocument.ParseValue(ref reader); NoDuplicates(json.RootElement);
            var value = json.RootElement.Deserialize<Document>(Wire) ?? throw new JsonException();
            return new(value.Version, value.Request, value.PinnedExecutable, value.Intent, value.BookmarkId,
                value.StimulusHash, value.ResumeTargetId, value.OutboxItemId, value.AuthoredFingerprint, value.IntentFingerprint);
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException or FormatException or MessageSubmissionException)
        { throw new JsonException("The native send checkpoint document is unsupported."); }
    }
    public override void Write(Utf8JsonWriter writer, NativeMessageSendCheckpointPlan value, JsonSerializerOptions options) =>
        JsonSerializer.SerializeToElement(new Document(value.Version, value.Request, value.PinnedExecutable, value.Intent,
            value.BookmarkId, value.StimulusHash, value.ResumeTargetId, value.OutboxItemId, value.AuthoredFingerprint, value.IntentFingerprint), Wire).WriteTo(writer);
    private sealed record Document(int Version, NativeMessageSendRequest Request, WorkflowExecutableIdentity PinnedExecutable,
        RuntimePostCommitIntent Intent, string BookmarkId, string StimulusHash, string ResumeTargetId, string OutboxItemId,
        string AuthoredFingerprint, string IntentFingerprint);
    private static JsonSerializerOptions Options()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        { PropertyNameCaseInsensitive = false, NumberHandling = JsonNumberHandling.Strict, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 128 };
        options.Converters.Add(new Text()); return options;
    }
    private static void NoDuplicates(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in value.EnumerateObject()) { if (!names.Add(item.Name)) throw new JsonException(); NoDuplicates(item.Value); }
        }
        else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) NoDuplicates(item);
    }
    private sealed class Text : JsonConverter<string>
    {
        public override string Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) => Decode(reader.GetString());
        public override string ReadAsPropertyName(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) => Decode(reader.GetString());
        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) => writer.WriteStringValue(Encode(value));
        public override void WriteAsPropertyName(Utf8JsonWriter writer, string value, JsonSerializerOptions options) => writer.WritePropertyName(Encode(value));
        private static string Encode(string value)
        {
            var bytes = new byte[checked(value.Length * 2)];
            for (var index = 0; index < value.Length; index++) BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(index * 2, 2), value[index]);
            return "u16:" + Convert.ToBase64String(bytes);
        }
        private static string Decode(string? value)
        {
            if (value is null || !value.StartsWith("u16:", StringComparison.Ordinal)) throw new JsonException();
            var bytes = Convert.FromBase64String(value[4..]); if (bytes.Length % 2 != 0) throw new JsonException();
            var chars = new char[bytes.Length / 2];
            for (var index = 0; index < chars.Length; index++) chars[index] = (char)BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(index * 2, 2));
            var text = new string(chars); if (Encode(text) != value) throw new JsonException(); return text;
        }
    }
}
