using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Workflows.Runtime.Activities;
using Elsa.Etl.Workflows.Runtime.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;
using RuntimeRegistration = Elsa.Workflows.Runtime.Core.Models.ActivityTriggerRegistration;

namespace Elsa.Etl.Workflows.Runtime.Services;

internal static class EtlNativeBoundary
{
    public const int FormatVersion = 1;
    public const int MaximumRequestBytes = 512 * 1024;
    public const int MaximumResultBytes = 16 * 1024;
    public const string PinKey = "etl.pipeline.v1";
    public const string ResumeTargetKey = "etl-completed";
    public const string StimulusType = "elsa.etl.completed.v1";
    public const string StartKind = "Elsa.Etl.Start.v1";
    public const string CancelKind = "Elsa.Etl.Cancel.v1";
    private static readonly ValueTypeDescriptor StateType = new(typeof(RunEtlState).FullName!, schemaVersion: FormatVersion);
    public static readonly ValueTypeDescriptor CompletionType = new(typeof(RunEtlCompletion).FullName!, schemaVersion: FormatVersion);
    private static readonly JsonSerializerOptions PinOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };

    public static EtlArtifact ReadPin(string json, string selection)
    {
        if (json.Length > EtlArtifact.MaximumByteLength || Encoding.UTF8.GetByteCount(json) > EtlArtifact.MaximumByteLength)
            throw Invalid();
        var artifact = JsonSerializer.Deserialize<EtlArtifact>(json, PinOptions) ?? throw Invalid();
        var selected = EtlPublishedSelection.Parse(selection);
        if (artifact.FormatVersion != EtlArtifact.CurrentFormatVersion ||
            artifact.DefinitionId != selected.DefinitionId || artifact.DefinitionVersion != selected.DefinitionVersion ||
            artifact.ContentHash != selected.ContentHash || EtlArtifactJson.Hash(artifact.Pipeline) != selected.ContentHash ||
            artifact.ArtifactId != EtlArtifactJson.CreateArtifactId(artifact.FormatVersion, artifact.DefinitionId,
                artifact.DefinitionVersion, artifact.ContentHash))
            throw Invalid();
        return artifact;
    }

    public static string RunId(EtlScope scope, string workflowId, string activityId) =>
        "etl-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            EtlRuntimeJson.Write(new { Format = "etl-native-run-v1", Scope = scope, WorkflowId = workflowId, ActivityId = activityId })))).ToLowerInvariant();

    public static RunEtlRequest ReadState(RunEtlState state)
    {
        if (state.FormatVersion != FormatVersion)
            throw Invalid();
        var request = EtlRuntimeJson.Read<RunEtlRequest>(state.Invocation, MaximumRequestBytes);
        ValidateRequest(request);
        return request;
    }

    private static void ValidateRequest(RunEtlRequest request)
    {
        if (request.Scope is null || request.Artifact is null || string.IsNullOrWhiteSpace(request.WorkflowExecutionId) ||
            string.IsNullOrWhiteSpace(request.ActivityExecutionId) ||
            request.RunId != RunId(request.Scope, request.WorkflowExecutionId, request.ActivityExecutionId) ||
            string.IsNullOrWhiteSpace(request.ExecutableId) || string.IsNullOrWhiteSpace(request.ExecutableHash) ||
            string.IsNullOrWhiteSpace(request.NodeId))
            throw Invalid();
    }

    public static void EnsureInvocation(EtlInvocation invocation)
    {
        var address = invocation.ResultAddress;
        var request = new RunEtlRequest(invocation.Scope, invocation.RunId, invocation.Artifact, address.WorkflowExecutionId,
            address.ActivityExecutionId, address.ExecutableId, address.ExecutableHash, address.NodeId);
        ValidateRequest(request);
        if (EtlRuntimeIdentity.InvocationFingerprint(invocation) != EtlRuntimeIdentity.InvocationFingerprint(Invocation(request, address.RegistrationId)))
            throw Invalid();
    }

    public static RunEtlRequest ReadState(ActivityExecutionState activity)
    {
        var state = activity.PrivateState;
        if (activity.Execution.ActivityType != RunEtl.ActivityType || state?.StateVersion != FormatVersion ||
            state.InvocationId != activity.InvocationId || state.Value.Type != StateType || state.Value.InlineValue is not { } json ||
            System.Runtime.InteropServices.JsonMarshal.GetRawUtf8Value(json).Length > MaximumRequestBytes + 1024)
            throw Invalid();
        var request = ReadState(json.Deserialize<RunEtlState>() ?? throw Invalid());
        if (request.WorkflowExecutionId != activity.Execution.WorkflowExecutionId ||
            request.ActivityExecutionId != activity.InvocationId || request.NodeId != activity.Execution.ExecutableNodeId)
            throw Invalid();
        return request;
    }

    public static RuntimeRegistration Registration(ActivityExecutionState activity, RunEtlRequest request)
    {
        var registrations = activity.TriggerRegistrations;
        if (registrations is null || registrations.Count != 1)
            throw Invalid();
        var registration = registrations.Single();
        if (registration.InvocationId != request.ActivityExecutionId ||
            registration.ResumeTargetKey != WorkflowExecutableResumeTarget.ComposeScopedId(request.NodeId, ResumeTargetKey) ||
            registration.StimulusType != StimulusType || registration.StimulusHash != request.RunId ||
            registration.PayloadType != CompletionType ||
            registration.DeduplicationPolicy != ActivityTriggerDeduplicationPolicy.Once)
            throw Invalid();
        return registration;
    }

    public static EtlInvocation Invocation(ActivityExecutionState activity)
    {
        var request = ReadState(activity);
        if (activity.TriggerRegistrations is { Count: > 0 })
            return Invocation(request, Registration(activity, request).RegistrationId);

        // The native claim atomically consumes the bookmark and clears its registration before Resume runs.
        // Its durable typed receipt retains the real registration identity even after payload retirement.
        var receipts = activity.TriggerDeliveries?.Where(delivery => delivery.Status == ActivityTriggerDeliveryStatus.Consumed &&
            delivery.ProviderId == EtlNativeResultAddress.CompletionProviderId && delivery.PayloadType == CompletionType).ToArray();
        if (receipts is not { Length: 1 })
            throw Invalid();
        var invocation = Invocation(request, receipts[0].RegistrationId);
        if (receipts[0].Payload.InlineValue is { } json)
        {
            var completion = json.Deserialize<RunEtlCompletion>() ?? throw Invalid();
            if (completion.FormatVersion != FormatVersion ||
                EtlRuntimeIdentity.InvocationFingerprint(EtlRuntimeJson.Read<EtlInvocation>(completion.Invocation, MaximumRequestBytes)) !=
                EtlRuntimeIdentity.InvocationFingerprint(invocation))
                throw Invalid();
        }
        return invocation;
    }

    public static EtlInvocation Invocation(RunEtlRequest request, string registrationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registrationId);
        return new(request.Scope, request.RunId, request.Artifact, new(FormatVersion,
            request.WorkflowExecutionId, request.ActivityExecutionId, request.ExecutableId, request.ExecutableHash,
            request.NodeId, registrationId, registrationId, WorkflowExecutableResumeTarget.ComposeScopedId(request.NodeId, ResumeTargetKey),
            StimulusType, request.RunId, CompletionType.Alias, FormatVersion, "once", EtlNativeResultAddress.CompletionProviderId));
    }

    public static void EnsureBookmark(EtlNativeResultAddress address, BookmarkState bookmark)
    {
        if (bookmark.BookmarkId != address.BookmarkId || bookmark.WorkflowExecutionId != address.WorkflowExecutionId ||
            bookmark.ActivityExecutionId != address.ActivityExecutionId || bookmark.ExecutableNodeId != address.NodeId ||
            bookmark.ResumeTargetId != address.ResumeTargetId || bookmark.StimulusType != address.StimulusType ||
            bookmark.StimulusHash != address.StimulusHash)
            throw Invalid();
    }

    public static RuntimePostCommitIntent Intent(EtlInvocation invocation, bool cancel, DateTimeOffset recordedAt) =>
        new($"etl-{(cancel ? "cancel" : "start")}:{invocation.RunId}", invocation.ResultAddress.WorkflowExecutionId,
            cancel ? CancelKind : StartKind, recordedAt, invocation.ResultAddress.ActivityExecutionId,
            $"etl-{(cancel ? "cancel" : "start")}:{invocation.RunId}", EtlRuntimeJson.WriteElement(invocation));

    public static InvalidDataException Invalid() => new("The ETL native invocation or its frozen result address is invalid or unsupported.");
}
