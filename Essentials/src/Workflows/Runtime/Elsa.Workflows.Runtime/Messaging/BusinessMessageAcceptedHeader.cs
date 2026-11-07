using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Messaging;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Messaging;

/// <summary>Immutable BusinessMessage content commitment. Construction/decoding is never acceptance authority.</summary>
/// <remarks>The selected provider constructs/revalidates this value with actual payload, complete coherent interest
/// capture, dedup/capacity and operation proof in its acceptance transaction. Payload/progress stay in the same
/// canonical source row outside this header. Current target, sender control and delivery status have other owners.</remarks>
internal sealed class BusinessMessageAcceptedHeader
{
    internal const string Family = "BusinessMessage";
    internal const int Version = 1;
    internal const int MaximumDefinitions = 256;

    private BusinessMessageAcceptedHeader(string messageId, string receiptId, RuntimeDeliveryDiscoveryPartition context,
        string producerNamespace, string configurationRevision, string name, string correlationKey,
        string? producerMessageId, int? lifetimeSeconds, string submittedFingerprint, string payloadCommitment,
        MessageSubmissionPolicy policy, DateTimeOffset acceptedAt, long interestEpoch,
        IReadOnlyCollection<MessageDefinitionIdentity> definitions)
    {
        ArgumentNullException.ThrowIfNull(context); ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId); ArgumentException.ThrowIfNullOrWhiteSpace(receiptId);
        ArgumentException.ThrowIfNullOrWhiteSpace(producerNamespace);
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationRevision);
        if (interestEpoch <= 0) throw new ArgumentOutOfRangeException(nameof(interestEpoch));
        BusinessMessageAcceptedHeaderCodec.RequireFingerprint(submittedFingerprint, MessageSubmissionRules.FingerprintVersion);
        BusinessMessageAcceptedHeaderCodec.RequireFingerprint(payloadCommitment, BusinessMessageAcceptedHeaderCodec.PayloadVersion);
        // Structural envelope checks and fixed clock/policy replay; actual payload is checked by Create/BindPayload.
        var shape = MessageSubmissionRules.CreateIntent(name, correlationKey, JsonSerializer.SerializeToElement<object?>(null),
            producerMessageId, lifetimeSeconds);
        var times = MessageSubmissionRules.AtAcceptance(shape, acceptedAt, policy);
        if (context.TestScope?.IsExpired(times.AcceptedAt) == true)
            throw new ArgumentException("The accepted context was already expired.", nameof(context));
        Context = context;
        Scope = new(context.Scope, context.TenantId, context.TestScope?.ScopeId);
        var distinct = new HashSet<MessageDefinitionIdentity>();
        var snapshot = new List<MessageDefinitionIdentity>();
        foreach (var definition in definitions)
        {
            if (definition is null || definition.Scope != Scope || snapshot.Count == MaximumDefinitions || !distinct.Add(definition))
                throw new ArgumentException("The complete cohort is foreign, duplicate or excessive.", nameof(definitions));
            snapshot.Add(definition);
        }
        Definitions = Array.AsReadOnly(snapshot.OrderBy(item => item.DefinitionNamespace, StringComparer.Ordinal)
            .ThenBy(item => item.DefinitionId, StringComparer.Ordinal).ToArray());
        Address = new(Family, context.Scope, messageId);
        Receipt = new(receiptId, times.AcceptedAt, times.BufferLifetimeSeconds == 0 ? MessageDeliveryMode.ImmediateOnly : MessageDeliveryMode.Buffered,
            times.BufferUntil, times.DeduplicateUntil);
        ProducerNamespace = producerNamespace; ConfigurationRevision = configurationRevision;
        Name = name; CorrelationKey = correlationKey; ProducerMessageId = producerMessageId; LifetimeSeconds = lifetimeSeconds;
        SubmittedFingerprint = submittedFingerprint; PayloadCommitment = payloadCommitment; Policy = policy;
        InterestEpoch = interestEpoch;
        CohortDigest = BusinessMessageAcceptedHeaderCodec.ComputeCohortDigest(Scope, Definitions);
    }

    internal static BusinessMessageAcceptedHeader Create(string messageId, string receiptId,
        RuntimeDeliveryDiscoveryPartition context, string producerNamespace, string configurationRevision,
        MessageSubmission submission, string originalSubmittedFingerprint, MessageSubmissionPolicy policy,
        DateTimeOffset acceptedAt, long interestEpoch, IReadOnlyCollection<MessageDefinitionIdentity> definitions)
    {
        ArgumentNullException.ThrowIfNull(submission);
        var intent = MessageSubmissionRules.CreateIntent(submission.Name, submission.CorrelationKey, submission.Payload,
            submission.ProducerMessageId, submission.LifetimeSeconds);
        if (!StringComparer.Ordinal.Equals(intent.Fingerprint, originalSubmittedFingerprint))
            throw new ArgumentException("The original submitted content differs.", nameof(originalSubmittedFingerprint));
        return new(messageId, receiptId, context, producerNamespace, configurationRevision, intent.Name, intent.CorrelationKey,
            intent.MessageId, intent.LifetimeSeconds, intent.Fingerprint,
            BusinessMessageAcceptedHeaderCodec.ComputePayloadCommitment(intent.Payload), policy, acceptedAt, interestEpoch, definitions);
    }

    // Metadata read after payload retirement validates the immutable commitment, never invents deleted payload.
    internal static BusinessMessageAcceptedHeader Restore(string messageId, string receiptId,
        RuntimeDeliveryDiscoveryPartition context, string producerNamespace, string configurationRevision,
        string name, string key, string? producerId, int? lifetime, string submittedFingerprint, string payloadCommitment,
        MessageSubmissionPolicy policy, DateTimeOffset acceptedAt, long epoch, IReadOnlyCollection<MessageDefinitionIdentity> definitions) =>
        new(messageId, receiptId, context, producerNamespace, configurationRevision, name, key, producerId, lifetime,
            submittedFingerprint, payloadCommitment, policy, acceptedAt, epoch, definitions);

    internal MessageSubmissionIntent BindPayload(JsonElement payload)
    {
        var intent = MessageSubmissionRules.CreateIntent(Name, CorrelationKey, payload, ProducerMessageId, LifetimeSeconds);
        if (intent.Fingerprint != SubmittedFingerprint ||
            BusinessMessageAcceptedHeaderCodec.ComputePayloadCommitment(intent.Payload) != PayloadCommitment)
            throw new InvalidDataException("The canonical payload differs from its accepted commitment.");
        return intent;
    }

    internal RuntimeDeliverySourceAddress Address { get; }
    internal RuntimeDeliveryDiscoveryPartition Context { get; }
    internal MessageScope Scope { get; }
    internal MessageReceipt Receipt { get; }
    internal string ProducerNamespace { get; }
    internal string ConfigurationRevision { get; }
    internal string Name { get; }
    internal string CorrelationKey { get; }
    internal string? ProducerMessageId { get; }
    internal int? LifetimeSeconds { get; }
    internal string SubmittedFingerprint { get; }
    internal string PayloadCommitment { get; }
    internal MessageSubmissionPolicy Policy { get; }
    internal long InterestEpoch { get; }
    internal IReadOnlyList<MessageDefinitionIdentity> Definitions { get; }
    internal string CohortDigest { get; }
    public override string ToString() => nameof(BusinessMessageAcceptedHeader);
}

internal enum BusinessMessagePayloadState { Present = 0, Retired = 1 }

/// <summary>Data in the SAME canonical source row; no independent store or retirement authority.</summary>
internal sealed class BusinessMessagePayloadSlot
{
    private readonly JsonElement payload;
    private BusinessMessagePayloadSlot(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Undefined) throw new ArgumentException("A payload must be defined.", nameof(value));
        payload = value.Clone(); State = BusinessMessagePayloadState.Present;
    }
    private BusinessMessagePayloadSlot(string operationId, string operationFingerprint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId); ArgumentException.ThrowIfNullOrWhiteSpace(operationFingerprint);
        State = BusinessMessagePayloadState.Retired; RetirementOperationId = operationId; RetirementOperationFingerprint = operationFingerprint;
    }
    internal static BusinessMessagePayloadSlot Present(JsonElement value) => new(value);
    // The real provider must prove eligible retirement atomically before writing this data variant.
    internal static BusinessMessagePayloadSlot Retired(string operationId, string operationFingerprint) => new(operationId, operationFingerprint);
    internal BusinessMessagePayloadState State { get; }
    internal string? RetirementOperationId { get; }
    internal string? RetirementOperationFingerprint { get; }
    internal JsonElement Payload => State == BusinessMessagePayloadState.Present ? payload :
        throw new InvalidOperationException("The canonical payload was retired.");
    internal MessageSubmissionIntent BindForPlanning(BusinessMessageAcceptedHeader header)
    {
        ArgumentNullException.ThrowIfNull(header);
        if (State != BusinessMessagePayloadState.Present) throw new InvalidDataException("Owed planning requires the canonical payload.");
        return header.BindPayload(payload);
    }
    public override string ToString() => nameof(BusinessMessagePayloadSlot);
}
