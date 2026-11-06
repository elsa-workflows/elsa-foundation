using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Jobs.Core;
using Elsa.Jobs.Workflows.Runtime.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Jobs.Workflows.Runtime.Services;

/// <summary>Finite Jobs-owned wire contracts; no model routing, persistence context or retry authority.</summary>
internal static class JobsNativeBoundary
{
    internal const int Version = 1;
    internal const int MaximumEnvelopeBytes = 4 * JobPayloadLimits.MaximumBytes;
    internal const string ActivityType = "Elsa.Jobs.Workflows.Runtime.Activities.RunJob";
    internal const string PinMetadataKey = "jobs.handler.v1";
    internal const string AdmissionMetadataKey = "jobs.native.admission.v1";
    internal const string DeliveryMetadataKey = "jobs.native.delivery.v1";
    internal const string ResumeTargetKey = "job-completed";
    internal const string StimulusType = "elsa.jobs.completed.v1";
    internal const string ProviderId = "elsa.jobs.native.v1";
    internal const string StartKind = "Elsa.Jobs.Start.v1";
    internal const string CancelKind = "Elsa.Jobs.Cancel.v1";
    internal const string SettlementKind = "Elsa.Jobs.Settlement.v1";
    internal const string ContinuationSchema = "elsa.jobs.native-address";
    internal const string PreAdmissionFaultCode = "jobs.native.admission-invalid";
    internal const string OwnerCancellationMetadataKey = "jobs.native.owner-cancel.v1";
    internal static readonly ValueTypeDescriptor CompletionType = new(typeof(RunJobCompletion).FullName!, schemaVersion: Version);
    private static readonly JsonSerializerOptions Json = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 64
    };

    internal static JobException Invalid(Exception? cause = null) => cause is null
        ? new("jobs.native.invalid", "The retained Jobs native boundary is invalid, mismatched or unsupported.")
        : new("jobs.native.invalid", "The retained Jobs native boundary is invalid, mismatched or unsupported.", cause);

    internal static T Protect<T>(Func<T> action)
    {
        try { return action(); }
        catch (Exception error) when (error is JsonException or NotSupportedException or ArgumentException or
            InvalidDataException or FormatException or OverflowException)
        { throw Invalid(error); }
    }

    internal static JsonElement Write<T>(T value) => Protect(() =>
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Json);
        if (bytes.Length > MaximumEnvelopeBytes) throw Invalid();
        using var document = JsonDocument.Parse(bytes);
        return document.RootElement.Clone();
    });

    internal static T Read<T>(JsonElement value) => Protect(() =>
    {
        if (value.ValueKind == JsonValueKind.Undefined ||
            System.Runtime.InteropServices.JsonMarshal.GetRawUtf8Value(value).Length > MaximumEnvelopeBytes) throw Invalid();
        return value.Deserialize<T>(Json) ?? throw Invalid();
    });

    internal static T ReadString<T>(string value, int maximum = MaximumEnvelopeBytes) => Protect(() =>
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length > maximum || Encoding.UTF8.GetByteCount(value) > maximum) throw Invalid();
        using var document = JsonDocument.Parse(value);
        return Read<T>(document.RootElement);
    });

    internal static bool Equivalent<T>(T left, T right) => JsonElement.DeepEquals(Write(left), Write(right));
    // The immutable source can be reserialized with another object property order. Reuse the
    // Core canonical frame (including raw numeric lexemes), retaining this boundary's byte bound.
    internal static string Hash<T>(T value) => JobPayloadDigest.Compute(new(new("elsa.jobs.native-identity", Version), Write(value)));

    internal static void Name(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 256 || value.Any(char.IsControl)) throw Invalid();
    }

    internal static void Digest(string value)
    {
        if (value is null || value.Length != 64 || value.Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))) throw Invalid();
    }

    internal static void Scope(JobScope value)
    {
        if (value is null) throw Invalid();
        Name(value.PersistencePartition); if (value.TenantId is not null) Name(value.TenantId);
    }

    internal static void Schema(JobSchema value)
    {
        if (value is null || value.Version < 1) throw Invalid(); Name(value.Alias);
    }

    private static void Pin(WorkflowExecutableIdentity value)
    {
        if (value is null) throw Invalid();
        Name(value.ArtifactId); Name(value.DefinitionId); Name(value.DefinitionVersionId); Name(value.ArtifactVersion); Digest(value.ArtifactHash);
    }

    internal static void ValidatePin(JobWorkflowHandlerPin pin)
    {
        if (pin is null || pin.FormatVersion != Version || pin.Handler is null || pin.Policy is null || pin.Policy.Retry is not { } retry ||
            !Enum.IsDefined(pin.AssociationMode)) throw Invalid();
        Name(pin.Handler.JobType); Name(pin.Handler.Version); Name(pin.Queue); Schema(pin.InputSchema); Schema(pin.ResultSchema);
        if (retry.MaximumAttempts is < 1 or > 1000 || retry.InitialBackoff < TimeSpan.Zero ||
            retry.MaximumBackoff < retry.InitialBackoff || retry.MaximumBackoff > TimeSpan.FromDays(365) ||
            !double.IsFinite(retry.BackoffMultiplier) || retry.BackoffMultiplier < 1 ||
            !double.IsFinite(retry.JitterFraction) || retry.JitterFraction is < 0 or > 1 ||
            pin.Policy.AttemptTimeout <= TimeSpan.Zero || pin.Policy.AttemptTimeout > TimeSpan.FromDays(365) ||
            pin.Policy.MinimumKeyRecognition <= TimeSpan.Zero || pin.Policy.MinimumKeyRecognition > TimeSpan.FromDays(3650)) throw Invalid();
    }

    internal static void ValidateInvocation(RunJobInvocation value)
    {
        if (value is null || value.FormatVersion != Version || value.Contract is null || value.Owner is null || value.HandlerPin is null ||
            value.Input is null || value.Input.Schema != value.HandlerPin.InputSchema ||
            value.HandlerPin.AssociationMode != JobAssociationMode.Awaited) throw Invalid();
        Scope(value.Scope); ValidatePin(value.HandlerPin);
        Pin(value.Executable);
        foreach (var field in new[] { value.WorkflowExecutionId, value.InvocationId, value.OperationKey, value.InitialAttemptId,
                     value.NodeId, value.ResumeTargetId, value.Contract.ContractVersion, value.Contract.SchemaFingerprint }) Name(field);
        if (value.Contract.ActivityTypeKey != ActivityType || value.Owner.FormatVersion != Version || value.Owner.Scope != value.Scope) throw Invalid();
        foreach (var field in new[] { value.Owner.ModelKind, value.Owner.DefinitionId, value.Owner.DefinitionVersion,
                     value.Owner.ExecutionId, value.Owner.InvocationId, value.Owner.MaterialId }) Name(field);
        Digest(value.Owner.MaterialSha256);
        if (JobPayloadLimits.GetByteCount(value.Input) > JobPayloadLimits.MaximumBytes) throw Invalid();
        if (value.Owner.Origin is { } origin)
        {
            if (origin.Scope.Value != value.Scope.PersistencePartition || origin.WorkerExecutionId != value.WorkflowExecutionId ||
                origin.ExecutionId != value.Owner.ExecutionId || origin.InvocationId != value.Owner.InvocationId ||
                !WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(origin.WorkerExecutable, value.Executable) ||
                origin.Executable.ArtifactId != value.Owner.MaterialId || origin.Executable.ArtifactHash != value.Owner.MaterialSha256 ||
                value.Owner.ModelAddress is not { } model || model.ValueKind != JsonValueKind.Object ||
                System.Runtime.InteropServices.JsonMarshal.GetRawUtf8Value(model).Length > 32_768) throw Invalid();
        }
        else if (value.Owner.ModelKind != "elsa.native" || value.Owner.ExecutionId != value.WorkflowExecutionId ||
            value.Owner.InvocationId != value.InvocationId || value.Owner.DefinitionId != value.Executable.DefinitionId ||
            value.Owner.DefinitionVersion != value.Executable.DefinitionVersionId || value.Owner.MaterialId != value.Executable.ArtifactId ||
            value.Owner.MaterialSha256 != value.Executable.ArtifactHash || value.Owner.ModelAddress is not null) throw Invalid();
    }

    internal static RunJobInvocation ReadState(RunJobState state)
    {
        if (state is null || state.FormatVersion != Version) throw Invalid();
        var invocation = Read<RunJobInvocation>(state.Invocation); ValidateInvocation(invocation); return invocation;
    }

    internal static RunJobInvocation ReadState(ActivityExecutionState activity)
    {
        if (activity.Execution.ActivityType != ActivityType || activity.PrivateState is not { StateVersion: Version } state ||
            state.InvocationId != activity.InvocationId || state.Value.Type != new ValueTypeDescriptor(typeof(RunJobState).FullName!, schemaVersion: Version) ||
            state.Value.InlineValue is not { } json) throw Invalid();
        var invocation = ReadState(Read<RunJobState>(json));
        if (invocation.WorkflowExecutionId != activity.Execution.WorkflowExecutionId || invocation.InvocationId != activity.InvocationId ||
            invocation.NodeId != activity.Execution.ExecutableNodeId || invocation.OperationKey != activity.OperationKey ||
            !Equivalent(invocation.Contract, activity.ContractIdentity)) throw Invalid();
        return invocation;
    }

    internal static string StimulusHash(RunJobInvocation invocation) => Hash(new
    {
        Format = "jobs.native.wait.v1", invocation.Scope, invocation.WorkflowExecutionId,
        invocation.InvocationId, invocation.OperationKey, invocation.Executable, invocation.NodeId, invocation.Contract
    });

    internal static JobNativeAddress ReadAddress(JobSnapshot snapshot)
    {
        if (snapshot.Request is not { Association: { Mode: JobAssociationMode.Awaited, Continuation: { } continuation } association } request ||
            snapshot.UnavailableDiagnostic is not null || snapshot.RequestSha256 != JobRequestDigest.Compute(request) ||
            continuation.Schema != new JobSchema(ContinuationSchema, Version)) throw Invalid();
        var address = Read<JobNativeAddress>(continuation.Value);
        if (address is null) throw Invalid();
        var pin = new JobWorkflowHandlerPin(Version, request.Handler, request.Input.Schema, request.ResultSchema, request.Queue, request.Policy, association.Mode);
        // Validate the captured address; no bookmark/registration is manufactured from invocation fields.
        var invocation = new RunJobInvocation(Version, address.Scope, address.Executable, address.WorkflowExecutionId,
            address.InvocationId, address.OperationKey, address.InitialAttemptId, address.NodeId, address.Contract,
            address.ResumeTargetId, pin, request.Input, request.NotBefore, address.Owner);
        ValidateInvocation(invocation); EnsureAddress(invocation, address);
        var owner = address.Owner;
        if (address.Scope != snapshot.Scope || association.ModelKind != owner.ModelKind || association.DefinitionId != owner.DefinitionId ||
            association.DefinitionVersion != owner.DefinitionVersion || association.ExecutionId != owner.ExecutionId ||
            association.InvocationId != owner.InvocationId || association.MaterialId != owner.MaterialId || association.MaterialSha256 != owner.MaterialSha256)
            throw Invalid();
        return address;
    }

    internal static JobResult SelectedResult(JobSnapshot snapshot)
    {
        if (snapshot.State != JobState.Terminal || snapshot.Request is not { } request || snapshot.UnavailableDiagnostic is not null ||
            snapshot.Result is not { } result || result.JobId != snapshot.JobId || !Enum.IsDefined(result.Outcome) ||
            result.PayloadSha256 != JobPayloadDigest.Compute(result.Payload) || result.Outcome == JobOutcome.Succeeded &&
            (result.Payload is null || result.Payload.Schema != request.ResultSchema)) throw Invalid();
        Name(result.ResultId); Digest(result.PayloadSha256); if (result.AttemptId is not null) Name(result.AttemptId);
        if (result.Payload is { } payload)
        {
            Schema(payload.Schema);
            if (JobPayloadLimits.GetByteCount(payload) > JobPayloadLimits.MaximumBytes) throw Invalid();
        }
        if (result.Diagnostic is { } diagnostic)
        {
            Name(diagnostic.Code);
            if (diagnostic.Summary is null || diagnostic.Summary.Length > 2048) throw Invalid();
        }
        return result;
    }

    internal static void EnsureAddress(RunJobInvocation invocation, JobNativeAddress address)
    {
        if (address is null || address.FormatVersion != Version || address.Scope != invocation.Scope ||
            !WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(address.Executable, invocation.Executable) ||
            address.WorkflowExecutionId != invocation.WorkflowExecutionId || address.InvocationId != invocation.InvocationId ||
            address.OperationKey != invocation.OperationKey || address.InitialAttemptId != invocation.InitialAttemptId ||
            address.NodeId != invocation.NodeId || !Equivalent(address.Contract, invocation.Contract) ||
            address.ResumeTargetId != invocation.ResumeTargetId || address.StimulusType != StimulusType ||
            address.StimulusHash != StimulusHash(invocation) || address.ProviderId != ProviderId || address.PayloadType != CompletionType ||
            address.DeduplicationPolicy != ActivityTriggerDeduplicationPolicy.Once || !Equivalent(address.Owner, invocation.Owner)) throw Invalid();
        Name(address.BookmarkId); Name(address.RegistrationId);
    }

    internal static JobNativeAddress CaptureAddress(ActivityExecutionState activity, BookmarkState bookmark)
    {
        var invocation = ReadState(activity);
        if (activity.TriggerRegistrations is not { Count: 1 }) throw Invalid();
        var registration = activity.TriggerRegistrations.Single();
        if (registration.InvocationId != invocation.InvocationId || registration.ResumeTargetKey != invocation.ResumeTargetId ||
            registration.StimulusType != StimulusType || registration.StimulusHash != StimulusHash(invocation) ||
            registration.PayloadType != CompletionType || registration.DeduplicationPolicy != ActivityTriggerDeduplicationPolicy.Once ||
            !activity.BookmarkIds.Contains(bookmark.BookmarkId, StringComparer.Ordinal)) throw Invalid();
        var address = new JobNativeAddress(Version, invocation.Scope, invocation.Executable, invocation.WorkflowExecutionId,
            invocation.InvocationId, invocation.OperationKey, invocation.InitialAttemptId, invocation.NodeId, invocation.Contract,
            bookmark.BookmarkId, registration.RegistrationId, invocation.ResumeTargetId, StimulusType, StimulusHash(invocation),
            ProviderId, CompletionType, ActivityTriggerDeduplicationPolicy.Once, invocation.Owner);
        EnsureAddress(invocation, address); EnsureBookmark(address, bookmark); return address;
    }

    internal static void EnsureBookmark(JobNativeAddress address, BookmarkState bookmark)
    {
        if (bookmark.BookmarkId != address.BookmarkId || bookmark.WorkflowExecutionId != address.WorkflowExecutionId ||
            bookmark.ActivityExecutionId != address.InvocationId || bookmark.ExecutableNodeId != address.NodeId ||
            bookmark.ResumeTargetId != address.ResumeTargetId || bookmark.StimulusType != address.StimulusType ||
            bookmark.StimulusHash != address.StimulusHash) throw Invalid();
    }

    internal static JobNativeAdmission Admission(RunJobInvocation invocation, JobNativeAddress address, DateTimeOffset committedAt)
    {
        ValidateInvocation(invocation); EnsureAddress(invocation, address);
        var owner = invocation.Owner;
        var continuation = new JobPayload(new(ContinuationSchema, Version), Write(address));
        if (JobPayloadLimits.GetByteCount(continuation) > JobPayloadLimits.MaximumBytes) throw Invalid();
        var association = new JobAssociation(owner.ModelKind, owner.DefinitionId, owner.DefinitionVersion, owner.ExecutionId,
            owner.InvocationId, owner.MaterialId, owner.MaterialSha256, invocation.HandlerPin.AssociationMode, continuation);
        var request = new JobRequest(invocation.HandlerPin.Handler, invocation.Input, invocation.HandlerPin.ResultSchema,
            invocation.HandlerPin.Queue, invocation.HandlerPin.Policy, invocation.NotBefore, association);
        var digest = JobRequestDigest.Compute(request);
        var originId = Hash(new { Format = "jobs.native.origin.v1", invocation.Scope, owner.ModelKind, owner.ExecutionId,
            invocation.WorkflowExecutionId, invocation.OperationKey });
        return new(Version, invocation, address, request, digest, new(ProviderId, owner.ExecutionId, originId, digest),
            "job-" + originId, "jobs-native:" + originId, committedAt);
    }

    internal static JobNativeAdmission ReadAdmission(ActivityExecutionState activity)
    {
        if (!activity.Metadata.TryGetValue(AdmissionMetadataKey, out var json)) throw Invalid();
        var retained = ReadString<JobNativeAdmission>(json);
        var invocation = activity.PrivateState is null ? retained.Invocation : ReadState(activity);
        ValidateInvocation(invocation);
        if (retained.FormatVersion != Version || !Equivalent(retained.Invocation, invocation) ||
            invocation.WorkflowExecutionId != activity.Execution.WorkflowExecutionId || invocation.InvocationId != activity.InvocationId ||
            invocation.NodeId != activity.Execution.ExecutableNodeId || activity.Execution.ActivityType != ActivityType ||
            invocation.OperationKey != activity.OperationKey || !Equivalent(invocation.Contract, activity.ContractIdentity) ||
            activity.PrivateState is { } state && state.CommittedAt != retained.CommittedAt ||
            !Equivalent(retained, Admission(invocation, retained.Address, retained.CommittedAt))) throw Invalid();
        return retained;
    }

    internal static RuntimePostCommitIntent Intent(JobNativeAdmission admission, bool cancel)
    {
        var id = "jobs-" + (cancel ? "cancel:" : "start:") + admission.Origin.OriginId;
        return new(id, admission.Invocation.WorkflowExecutionId, cancel ? CancelKind : StartKind, admission.CommittedAt,
            admission.Invocation.InvocationId, id, Write(new JobNativeIntent(Version, admission)));
    }

    internal static RuntimePostCommitIntent SettlementIntent(JobNativeAdmission admission)
    {
        if (admission.Address.Owner.Origin is not null) throw Invalid();
        var id = "jobs-settlement:" + admission.Origin.OriginId;
        return new(id, admission.Invocation.WorkflowExecutionId, SettlementKind, admission.CommittedAt,
            admission.Invocation.InvocationId, id, Write(new JobNativeIntent(Version, admission)));
    }

    internal static void EnsureOrigin(JobNativeAdmission admission, JobOriginSnapshot origin)
    {
        if (!Equivalent(origin.Origin, admission.Origin) || origin.RequestSha256 != admission.RequestSha256 ||
            origin.Acceptance is null && !origin.Cancelled && !origin.Retired) throw Invalid();
        if (origin.Acceptance is { } acceptance) Name(acceptance.JobId);
    }

    internal static string ModelInputId(JobNativeAddress address, JobResult result) => address.Owner.Origin?.ResultInputId
        ?? "jobs-input-" + Hash(new { Format = "jobs.native.input.v1", address.Scope, result.JobId, result.ResultId, Address = address });

    internal static string ContinuationId(JobNativeAddress address, JobResult result, JobAssociation association) =>
        "jobs-cont-" + Hash(new { Format = "jobs.native.continuation.v1", address.Scope, result.JobId, result.ResultId,
            Association = association, Input = ModelInputId(address, result) });

    internal static string DeliveryId(JobNativeAddress address, RunJobCompletion completion) =>
        "jobs-delivery-" + Hash(new { Format = "jobs.native.delivery.v1", address.Scope, completion.JobId,
            completion.ResultId, completion.ContinuationId, completion.ModelInputId, Address = address });

    internal static string DeduplicationKey(JobNativeAddress address, string deliveryId) =>
        $"{address.WorkflowExecutionId}:bookmark-resume:{address.BookmarkId}:{address.StimulusType}:{address.StimulusHash}:{deliveryId}";

    internal static string TerminalMarker(RunJobCompletion completion) => "jobs.terminal.v1:" + Hash(completion);

    internal static string PreAdmissionMarker(WorkflowExecutionState parent, ActivityExecutionState activity, string attemptId) =>
        "jobs.native.pre-admission.v1:" + Hash(new { Format = "jobs.native.pre-admission.v1", Scope = new JobScope(
            parent.Partition?.Value ?? throw Invalid(), parent.TenantId), parent.PinnedExecutable, parent.WorkflowExecutionId,
            activity.InvocationId, activity.Execution.ExecutableNodeId, activity.ContractIdentity, AttemptId = attemptId });

    internal static string OwnerCancellationMarker(JobNativeAdmission admission) => "jobs.native.owner-cancel.v1:" + Hash(new
    {
        Format = "jobs.native.owner-cancel.v1", admission.Address, admission.Origin, admission.RequestSha256, admission.CommittedAt
    });

    internal static RunJobResult Result(RunJobCompletion completion, JobResult result) => new(Version,
        result.JobId, result.ResultId, completion.ContinuationId, completion.ModelInputId, result.Outcome,
        result.Payload, result.PayloadSha256, result.Diagnostic);
}
