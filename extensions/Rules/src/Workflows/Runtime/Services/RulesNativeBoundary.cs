using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Primitives.Models;
using Elsa.Rules.Core;
using Elsa.Rules.Persistence.Core;
using Elsa.Rules.Workflows.Runtime.Activities;
using Elsa.Rules.Workflows.Runtime.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Rules.Workflows.Runtime.Services;

internal static class RulesNativeBoundary
{
    public const int FormatVersion = 1;
    public const string ResumeTargetKey = "rules-completed";
    public const string StimulusType = "elsa.rules.completed.v1";
    public const string AddressMetadataKey = "rules.destination.v1";
    public const string StartKind = "Elsa.Rules.Start.v1";
    public const string CancelKind = "Elsa.Rules.Cancel.v1";
    public const string ProviderId = "elsa.rules.native.v1";
    public const string InvalidRoundLimit = "rules.invalid-round-limit";
    public static readonly ValueTypeDescriptor CompletionType = new(typeof(RunRulesCompletion).FullName!, schemaVersion: FormatVersion);
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 32
    };

    public static JsonElement Write<T>(T value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Options);
        if (bytes.Length > RuleLimits.MaxEnvelopeBytes) throw Invalid();
        using var document = JsonDocument.Parse(bytes);
        return document.RootElement.Clone();
    }

    public static T Read<T>(JsonElement json)
    {
        if (System.Runtime.InteropServices.JsonMarshal.GetRawUtf8Value(json).Length > RuleLimits.MaxEnvelopeBytes)
            throw Invalid();
        return json.Deserialize<T>(Options) ?? throw Invalid();
    }

    public static string RunId(RuleScope scope, string workflowId, string activityId) =>
        "rules-" + RuleRecordCodec.Hash(JsonSerializer.SerializeToUtf8Bytes(
            new { Format = "rules-native-run-v1", Scope = scope, WorkflowId = workflowId, ActivityId = activityId }, Options));

    public static string StimulusHash(RunRulesRequest request) =>
        RuleRecordCodec.Hash(RuleRecordCodec.ScopeBytes(new(request.Scope, request.RunId)));

    public static RunRulesRequest ReadState(RunRulesState state)
    {
        if (state.FormatVersion != FormatVersion) throw Invalid();
        var request = Read<RunRulesRequest>(state.Invocation);
        if (request.RunId != RunId(request.Scope, request.WorkflowExecutionId, request.ActivityExecutionId) ||
            string.IsNullOrEmpty(request.WorkflowExecutionId) || string.IsNullOrEmpty(request.ActivityExecutionId) ||
            string.IsNullOrEmpty(request.NodeId) || string.IsNullOrEmpty(request.ResumeTargetId) ||
            string.IsNullOrEmpty(request.ExecutableId))
            throw Invalid();
        RuleValueValidation.Identifier(request.ExecutableHash, nameof(request.ExecutableHash));
        _ = RuleRecordCodec.CanonicalFacts(request.InitialFacts);
        return request;
    }

    public static void EnsureAddress(RunRulesRequest request, RuleNativeResultAddress address, string registrationId)
    {
        if (address.FormatVersion != FormatVersion || address.WorkflowExecutionId != request.WorkflowExecutionId ||
            address.ActivityExecutionId != request.ActivityExecutionId || address.ExecutableId != request.ExecutableId ||
            address.ExecutableHash != request.ExecutableHash || address.NodeId != request.NodeId ||
            address.ResumeTargetId != request.ResumeTargetId || address.RegistrationId != registrationId ||
            address.StimulusType != StimulusType || address.StimulusHash != StimulusHash(request) ||
            address.ResultContractId != CompletionType.Alias || address.ResultSchemaVersion != FormatVersion ||
            address.DeduplicationPolicy != "once" || address.ProviderId != ProviderId)
            throw Invalid();
    }

    public static RuleInvocationEnvelope Invocation(RunRulesRequest request, RuleNativeResultAddress address)
    {
        EnsureAddress(request, address, address.RegistrationId);
        var invocation = new RuleInvocationEnvelope(new(request.Scope, request.RunId), request.Program.Artifact,
            request.InitialFacts, request.MaxRounds, request.Program.Implementation, 1, address);
        _ = RuleRecordCodec.InvocationBytes(invocation);
        return invocation;
    }

    public static RunRulesRequest ReadState(ActivityExecutionState activity)
    {
        var state = activity.PrivateState;
        if (activity.Execution.ActivityType != RunRules.ActivityType || state?.StateVersion != 1 ||
            state.InvocationId != activity.InvocationId || state.Value.Type != new ValueTypeDescriptor(typeof(RunRulesState).FullName!, schemaVersion: 1) ||
            state.Value.InlineValue is not { } json)
            throw Invalid();
        var request = ReadState(json.Deserialize<RunRulesState>() ?? throw Invalid());
        if (request.WorkflowExecutionId != activity.Execution.WorkflowExecutionId || request.ActivityExecutionId != activity.InvocationId ||
            request.NodeId != activity.Execution.ExecutableNodeId)
            throw Invalid();
        return request;
    }

    public static RuleNativeResultAddress CaptureAddress(ActivityExecutionState activity, BookmarkState bookmark)
    {
        var request = ReadState(activity);
        if (activity.TriggerRegistrations is not { Count: 1 }) throw Invalid();
        var registration = activity.TriggerRegistrations.Single();
        if (registration.InvocationId != request.ActivityExecutionId || registration.ResumeTargetKey != request.ResumeTargetId ||
            registration.StimulusType != StimulusType || registration.StimulusHash != StimulusHash(request) ||
            registration.PayloadType != CompletionType ||
            registration.DeduplicationPolicy != ActivityTriggerDeduplicationPolicy.Once ||
            !activity.BookmarkIds.Contains(bookmark.BookmarkId, StringComparer.Ordinal))
            throw Invalid();
        var address = new RuleNativeResultAddress(1, request.WorkflowExecutionId, request.ActivityExecutionId,
            request.ExecutableId, request.ExecutableHash, request.NodeId, bookmark.BookmarkId, request.ResumeTargetId,
            registration.RegistrationId, StimulusType, StimulusHash(request), CompletionType.Alias, 1, "once", ProviderId);
        EnsureBookmark(address, bookmark);
        return address;
    }

    public static RuleNativeResultAddress ReadAddress(ActivityExecutionState activity)
    {
        if (!activity.Metadata.TryGetValue(AddressMetadataKey, out var value)) throw Invalid();
        if (value.Length > RuleLimits.MaxStateMetadataBytes) throw Invalid();
        using var json = JsonDocument.Parse(value);
        var address = Read<RuleNativeResultAddress>(json.RootElement);
        EnsureAddress(ReadState(activity), address, address.RegistrationId);
        return address;
    }

    public static void EnsureBookmark(RuleNativeResultAddress address, BookmarkState bookmark)
    {
        if (bookmark.BookmarkId != address.BookmarkId || bookmark.WorkflowExecutionId != address.WorkflowExecutionId ||
            bookmark.ActivityExecutionId != address.ActivityExecutionId || bookmark.ExecutableNodeId != address.NodeId ||
            bookmark.ResumeTargetId != address.ResumeTargetId || bookmark.StimulusType != address.StimulusType ||
            bookmark.StimulusHash != address.StimulusHash)
            throw Invalid();
    }

    public static RuntimePostCommitIntent Intent(RunRulesRequest request, RuleNativeResultAddress address, bool cancel, DateTimeOffset at)
    {
        EnsureAddress(request, address, address.RegistrationId);
        var id = $"rules-{(cancel ? "cancel" : "start")}:{request.RunId}";
        return new(id, request.WorkflowExecutionId, cancel ? CancelKind : StartKind, at, request.ActivityExecutionId, id,
            Write(new RulesNativeIntent(1, request, address)));
    }

    public static bool HasInvalidRoundLimit(RunRulesRequest request) => request.MaxRounds is <= 0 or > RuleLimits.MaxRounds;

    public static string InvalidDeliveryId(RunRulesRequest request, RuleNativeResultAddress address) =>
        "rules-invalid-" + RuleRecordCodec.Hash(JsonSerializer.SerializeToUtf8Bytes(
            new { Request = request, Destination = address, Code = InvalidRoundLimit }, Options));

    public static string DeliveryDeduplicationKey(RuleNativeResultAddress address, string deliveryId) =>
        $"{address.WorkflowExecutionId}:bookmark-resume:{address.BookmarkId}:{address.StimulusType}:{address.StimulusHash}:{deliveryId}";

    public static string TerminalMarker(RunRulesCompletion completion, RuleTerminalResult result) =>
        "rules.terminal.v1:" + RuleRecordCodec.Hash(RuleRecordCodec.DeliveryBytes(completion.DeliveryId, completion.Destination, result));

    public static InvalidDataException Invalid() => new("The frozen Rules native invocation or completion is invalid or unsupported.");
}
