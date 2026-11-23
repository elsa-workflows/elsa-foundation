using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kernel.Foundation.Contracts;
using Durable = Kernel.Durable;

namespace Kernel.Foundation.InMemory;

internal sealed class OperatorIngress(Durable.InMemoryStore store, Func<BoundScope, string, bool>? authorize)
{
    private static readonly JsonSerializerOptions RecoverJson = new() { Converters = { new RetryHandlesConverter() } };

    public ValueTask<Admission> SendAsync(BoundScope scope, Target target, string id, string kind, string? payload,
        AddOptions? options, CancellationToken cancellationToken, string? replyTo)
    {
        ArgumentNullException.ThrowIfNull(scope);
        cancellationToken.ThrowIfCancellationRequested();
        var refused = new Admission(AdmissionOutcome.Refused);
        if (authorize?.Invoke(scope, kind) != true || !Durable.InMemoryStore.IsOutsideMessageId(id))
            return new(refused);

        // A migration dispatcher may skip an unavailable exact life. Admission bounds must refuse separately.
        if (kind == Kinds.Migrate && (!Durable.FoundationMetadataLimits.FitsName(id) ||
            !Durable.FoundationMetadataLimits.FitsContent(payload)))
            return new(refused);

        string? selection = null;
        string? deleteTarget = null;
        Durable.ParkRecoveryMode? recoverMode = null;
        IReadOnlyList<string>? recoverHandles = null;
        if (kind is Kinds.Terminate or Kinds.Delete or Kinds.Discard or Kinds.Recover)
        {
            OperatorCommand? command;
            if (!Durable.FoundationMetadataLimits.FitsContent(payload)) return new(refused);
            try { command = payload is null ? null : JsonSerializer.Deserialize<OperatorCommand>(payload, kind == Kinds.Recover ? RecoverJson : null); }
            catch (JsonException) { return new(refused); }
            if (command?.ThenDelete == true)
            {
                if (kind != Kinds.Terminate || authorize.Invoke(scope, Kinds.Delete) != true ||
                    command.ExpectedTarget is not { Id.Length: > 0 } expected)
                    return new(refused);
                deleteTarget = expected.Id;
            }
            if (command is not { Selection.Length: > 0 } &&
                (kind != Kinds.Terminate || command?.Selection is not null))
                return new(new Admission(AdmissionOutcome.Refused, Reason: RefusalReason.StaleSelection));
            if (kind == Kinds.Recover)
            {
                recoverMode = command switch
                {
                    { Mode: RecoverMode.Resume, Handles: null or { Count: 0 } } => Durable.ParkRecoveryMode.Resume,
                    { Mode: RecoverMode.RetryAll, Handles: null or { Count: 0 } } => Durable.ParkRecoveryMode.RetryAll,
                    { Mode: RecoverMode.Retry } when Durable.InMemoryStore.FitsFoundationRetryHandles(command.Handles) => Durable.ParkRecoveryMode.Retry,
                    _ => null
                };
                if (recoverMode is null) return new(refused);
                if (recoverMode == Durable.ParkRecoveryMode.Retry) recoverHandles = command?.Handles;
            }
            selection = command?.Selection;
        }

        var receipt = store.AdmitFoundationOperator(scope.Tenant, (target as Target.Ref)?.Actor.Id,
            (target as Target.Key)?.CallerKey, id, kind, payload, options?.DueAt, options?.KeepFor, selection, replyTo, recoverMode, recoverHandles, deleteTarget);
        return new(receipt.Outcome switch
        {
            Durable.InMemoryStore.OperatorAdmission.Accepted => new Admission(AdmissionOutcome.Accepted, new ActorRef(receipt.Reference!)),
            Durable.InMemoryStore.OperatorAdmission.Duplicate => new Admission(AdmissionOutcome.Duplicate, new ActorRef(receipt.Reference!)),
            Durable.InMemoryStore.OperatorAdmission.StaleSelection => new Admission(AdmissionOutcome.Refused, Reason: RefusalReason.StaleSelection),
            Durable.InMemoryStore.OperatorAdmission.Unavailable when kind == Kinds.Migrate && target is Target.Ref =>
                new Admission(AdmissionOutcome.Refused, Reason: RefusalReason.TargetMismatch),
            Durable.InMemoryStore.OperatorAdmission.TargetMismatch => new Admission(AdmissionOutcome.Refused, Reason: RefusalReason.TargetMismatch),
            _ => refused
        });
    }

    // Bound the list while reading the operator payload, before it reaches the store.
    private sealed class RetryHandlesConverter : JsonConverter<IReadOnlyList<string>>
    {
        public override IReadOnlyList<string> Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException();
            var handles = new List<string>();
            var bytes = 0;
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndArray) return handles.AsReadOnly();
                if (reader.TokenType != JsonTokenType.String || handles.Count == Durable.InMemoryStore.MaxRetryHandles ||
                    (reader.HasValueSequence ? reader.ValueSequence.Length : reader.ValueSpan.Length) > 6 * Durable.InMemoryStore.MaxRetryTokenChars)
                    throw new JsonException();
                var handle = reader.GetString()!;
                bytes += Encoding.UTF8.GetByteCount(handle);
                if (handle.Length is 0 or > Durable.InMemoryStore.MaxRetryTokenChars || bytes > Durable.InMemoryStore.MaxRetryHandleBytes)
                    throw new JsonException();
                handles.Add(handle);
            }
            throw new JsonException();
        }

        public override void Write(Utf8JsonWriter writer, IReadOnlyList<string> value, JsonSerializerOptions options) =>
            throw new NotSupportedException();
    }
}
