using System.Text.Json;

namespace Elsa.Workflows.Runtime.Core.Messaging;

/// <summary>Immutable submitted business content, without client-supplied scope or producer authority.</summary>
/// <remarks>
/// Construction detaches the payload and validates structural shape only. The publisher validates deployment
/// limits, names, payload compatibility policy and trusted ambient producer/scope before acceptance.
/// A constructed submission is neither accepted work nor an authorization capability.
/// </remarks>
public sealed class MessageSubmission
{
    public MessageSubmission(string name, string correlationKey, JsonElement payload, string? producerMessageId = null,
        int? lifetimeSeconds = null)
    {
        try
        {
            MessageIdentityValidation.RequirePortableText(name, nameof(name));
            MessageIdentityValidation.RequirePortableText(correlationKey, nameof(correlationKey));
            if (producerMessageId is not null) MessageIdentityValidation.RequirePortableText(producerMessageId, nameof(producerMessageId));
            if (lifetimeSeconds < 0 || payload.ValueKind == JsonValueKind.Undefined)
                throw new MessageSubmissionException(MessageSubmissionFailureKind.Validation);
            Payload = payload.Clone();
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or JsonException)
        {
            throw new MessageSubmissionException(MessageSubmissionFailureKind.Validation);
        }
        Name = name; CorrelationKey = correlationKey; ProducerMessageId = producerMessageId; LifetimeSeconds = lifetimeSeconds;
    }

    public string Name { get; }
    public string CorrelationKey { get; }
    public JsonElement Payload { get; }
    public string? ProducerMessageId { get; }
    /// <summary>Null retains omitted intent; zero requests immediate-only delivery, subject to publisher policy.</summary>
    public int? LifetimeSeconds { get; }
    public override string ToString() => "MessageSubmission { [redacted] }";
}

/// <summary>Controls buffering, without implying recipient delivery or workflow completion.</summary>
public enum MessageDeliveryMode
{
    Buffered = 0,
    ImmediateOnly = 1
}

/// <summary>Server-issued durable acceptance identity and fixed independent retention deadlines.</summary>
/// <remarks>
/// This receipt reports acceptance, not recipient selection, delivery, workflow completion or exactly-once effects.
/// Providers must reread original authoritative acceptance; a constructed or deserialized receipt grants no authority.
/// An immediate-only receipt has no open buffer deadline, including when its confirmed result was unmatched.
/// ReceiptId is an opaque server identity; internal storage/codecs must preserve its exact UTF-16 code units.
/// </remarks>
public sealed class MessageReceipt
{
    public MessageReceipt(string receiptId, DateTimeOffset acceptedAt, MessageDeliveryMode mode,
        DateTimeOffset? bufferUntil = null, DateTimeOffset? deduplicateUntil = null)
    {
        try { MessageIdentityValidation.RequireOpaqueText(receiptId, nameof(receiptId)); }
        catch (ArgumentException) { throw new MessageSubmissionException(MessageSubmissionFailureKind.Validation); }
        if (!Enum.IsDefined(mode) || acceptedAt == default || acceptedAt == DateTimeOffset.MaxValue ||
            mode == MessageDeliveryMode.Buffered && bufferUntil is null ||
            mode == MessageDeliveryMode.ImmediateOnly && bufferUntil is not null ||
            bufferUntil is { } buffer && buffer <= acceptedAt ||
            deduplicateUntil is { } dedup && dedup <= acceptedAt)
            throw new MessageSubmissionException(MessageSubmissionFailureKind.Validation);
        ReceiptId = receiptId; AcceptedAt = acceptedAt.ToUniversalTime(); Mode = mode;
        BufferUntil = bufferUntil?.ToUniversalTime(); DeduplicateUntil = deduplicateUntil?.ToUniversalTime();
    }

    public string ReceiptId { get; }
    public DateTimeOffset AcceptedAt { get; }
    public MessageDeliveryMode Mode { get; }
    public DateTimeOffset? BufferUntil { get; }
    public DateTimeOffset? DeduplicateUntil { get; }
    public override string ToString() => "MessageReceipt { [redacted] }";
}

/// <summary>Acceptance response; a duplicate retains the original receipt and never implies completed delivery.</summary>
public sealed class MessageSubmissionResult
{
    public MessageSubmissionResult(MessageReceipt receipt, bool duplicate)
    {
        Receipt = receipt ?? throw new MessageSubmissionException(MessageSubmissionFailureKind.Validation);
        Duplicate = duplicate;
    }

    public MessageReceipt Receipt { get; }
    public bool Duplicate { get; }
    public override string ToString() => "MessageSubmissionResult { [redacted] }";
}

/// <summary>Declares the message publisher as one scoped replacement, never a collection of contributors.</summary>
[AttributeUsage(AttributeTargets.Interface, Inherited = false)]
public sealed class MessagePublisherReplacementContractAttribute : Attribute;

/// <summary>Accepts business messages under trusted ambient producer identity, scope and current authorization.</summary>
/// <remarks>
/// Replacement contract: exactly one publisher per runtime context when messaging is enabled. Composition must
/// reject duplicate registrations; this Core contract registers no implementation. API and authored activities
/// consume the same acceptance semantics, while authored sources retain their separate permanent replay binding.
/// Success means durable acceptance only, not delivery, recipient completion or exactly-once external effects.
/// A same-ID duplicate resolves original acceptance and preserves its outstanding per-definition deliveries.
/// </remarks>
[MessagePublisherReplacementContract]
public interface IMessagePublisher
{
    /// <summary>Returns only after durable acceptance or resolution of the original accepted duplicate.</summary>
    /// <exception cref="MessageSubmissionException">Safe refusal or explicitly uncertain acceptance.</exception>
    /// <exception cref="OperationCanceledException">
    /// Requested cancellation propagates. Cancellation/lost response after commit submission does not prove
    /// rejection; reconcile or retry the same producer ID and unchanged content within its recognition window.
    /// </exception>
    ValueTask<MessageSubmissionResult> SubmitAsync(MessageSubmission submission, CancellationToken cancellationToken = default);
}

/// <summary>Preacceptance refusal categories plus a distinct unresolved acceptance outcome.</summary>
public enum MessageSubmissionFailureKind
{
    Validation = 0,
    Conflict = 1,
    Quota = 2,
    Forbidden = 3,
    Unavailable = 4,
    Uncertain = 5
}

/// <summary>Safe submission failure with fixed messages/codes and no submitted content or provider exception text.</summary>
/// <remarks>
/// Unavailable means known not accepted. Uncertain means commit outcome is unresolved, so callers must reconcile
/// the original identity rather than assume rejection. Raw provider causes belong in protected diagnostics,
/// not this public exception. Recipient failures after acceptance are separately observable delivery outcomes.
/// </remarks>
public sealed class MessageSubmissionException : Exception
{
    public MessageSubmissionException(MessageSubmissionFailureKind kind) : base(SafeMessage(kind)) => Kind = kind;

    public MessageSubmissionFailureKind Kind { get; }
    public string ReasonCode => Kind switch
    {
        MessageSubmissionFailureKind.Validation => "message-validation",
        MessageSubmissionFailureKind.Conflict => "message-conflict",
        MessageSubmissionFailureKind.Quota => "message-quota",
        MessageSubmissionFailureKind.Forbidden => "message-forbidden",
        MessageSubmissionFailureKind.Unavailable => "message-unavailable",
        _ => "message-uncertain"
    };

    private static string SafeMessage(MessageSubmissionFailureKind kind) => kind switch
    {
        MessageSubmissionFailureKind.Validation => "The message submission is invalid.",
        MessageSubmissionFailureKind.Conflict => "The message identity conflicts with an existing submission.",
        MessageSubmissionFailureKind.Quota => "Message acceptance capacity is exhausted.",
        MessageSubmissionFailureKind.Forbidden => "Message submission is not permitted.",
        MessageSubmissionFailureKind.Unavailable => "The message was not accepted because the required service is unavailable.",
        MessageSubmissionFailureKind.Uncertain => "Message acceptance is uncertain. Reconcile the original submission identity.",
        _ => throw new ArgumentException("Unknown message submission failure kind.", nameof(kind))
    };
}
