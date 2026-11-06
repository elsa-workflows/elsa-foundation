using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Jobs.Core;

namespace Elsa.Jobs.Persistence.EntityFrameworkCore;

internal static class JobStoreCodec
{
    internal static string Json<T>(T value) => JsonSerializer.Serialize(value);
    internal static T Preflight<T>(Func<T> read, string code)
    {
        try { return read(); }
        catch (Exception error) when (error is JsonException or NotSupportedException)
        { throw new JobException(code, "The Jobs caller data cannot be represented by the supported durable JSON format.", error); }
    }
    internal static bool SerializationFailure(Exception error) => error is JobException
    {
        Code: "jobs.payload.invalid" or "jobs.request.invalid" or "jobs.progress.invalid",
        InnerException: JsonException or NotSupportedException
    };
    internal static T Read<T>(string value) => JsonSerializer.Deserialize<T>(value)
        ?? throw new InvalidDataException("A Jobs persisted record is null.");
    internal static T ReadRetained<T>(string value, Action<T>? validate = null)
    {
        try
        {
            var result = Read<T>(value);
            validate?.Invoke(result);
            return result;
        }
        catch (Exception error) when (error is ArgumentException || SerializationFailure(error))
        { throw new InvalidDataException("The retained Jobs record has invalid fields.", error); }
    }
    internal static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    internal static string Identity(params object?[] fields) => Json(fields);
    internal static string Scope(JobScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        Name(scope.PersistencePartition);
        if (scope.TenantId is not null) Name(scope.TenantId);
        return Hash(Identity(scope.PersistencePartition, scope.TenantId));
    }
    internal static string Address(JobScope scope, string kind, params object?[] values) => Identity(scope.PersistencePartition, scope.TenantId, kind, values);
    internal static T New<T>(JobScope scope, string kind, params object?[] values) where T : IJobRecord, new()
    {
        var identity = Address(scope, kind, values);
        return new() { Key = Hash(identity), Identity = identity, ScopeKey = Scope(scope) };
    }
    internal static void Exact(IJobRecord row, JobScope scope, string kind, params object?[] values)
    {
        ExactIdentity(row, scope, kind, values);
        if (row.SchemaVersion != JobsEfModule.Version) throw new NotSupportedException("The Jobs record schema is unavailable.");
    }
    internal static void ExactIdentity(IJobRecord row, JobScope scope, string kind, params object?[] values)
    {
        if (row.ScopeKey != Scope(scope) || row.Identity != Address(scope, kind, values))
            throw new InvalidDataException("The Jobs indexed address does not match its exact identity.");
    }
    internal static DateTimeOffset At(long ticks) => new(ticks, TimeSpan.Zero);
    internal static string Support(JobRequest request) => Hash(Identity(request.Handler, request.Input.Schema, request.ResultSchema));
    internal static string Support(JobHandlerSupport support) => Hash(Identity(support.Handler, support.InputSchema, support.ResultSchema));
    internal static string Pin(string support, string implementation) => Hash(Identity(support, implementation));
    internal static string Key(JobScope scope, string kind, params object?[] values) => Hash(Address(scope, kind, values));
    internal static void Name(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 256) throw new ArgumentException("A Jobs identity must contain 1 through 256 nonblank characters.");
    }
    internal static void Digest(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length != 64 || value.Any(x => x is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new ArgumentException("A Jobs digest must be lowercase SHA-256 hexadecimal.");
    }
    internal static void Schema(JobSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        Name(schema.Alias);
        if (schema.Version < 1) throw new ArgumentOutOfRangeException(nameof(schema));
    }
    internal static void Payload(JobPayload payload)
    {
        Schema(payload.Schema);
        if (payload.Value.ValueKind == JsonValueKind.Undefined || JobPayloadLimits.GetByteCount(payload) > JobPayloadLimits.MaximumBytes)
            throw new ArgumentException("The Jobs payload is undefined or exceeds its durable envelope bound.");
    }
    internal static void Request(JobRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Handler); ArgumentNullException.ThrowIfNull(request.Input);
        ArgumentNullException.ThrowIfNull(request.Policy); ArgumentNullException.ThrowIfNull(request.Policy.Retry);
        Name(request.Handler.JobType); Name(request.Handler.Version); Name(request.Queue);
        Payload(request.Input); Schema(request.ResultSchema);
        if (Encoding.UTF8.GetByteCount(Json(request)) > 3 * JobPayloadLimits.MaximumBytes)
            throw new ArgumentException("The Jobs input/request exceeds its bounded durable size.");
        Policy(request.Policy);
        Association(request.Association);
    }
    internal static void Policy(JobExecutionPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy); ArgumentNullException.ThrowIfNull(policy.Retry);
        var retry = policy.Retry;
        if (retry.MaximumAttempts is < 1 or > 1000 || retry.InitialBackoff < TimeSpan.Zero || retry.MaximumBackoff < retry.InitialBackoff || retry.MaximumBackoff > TimeSpan.FromDays(365)
            || !double.IsFinite(retry.BackoffMultiplier) || retry.BackoffMultiplier < 1 || !double.IsFinite(retry.JitterFraction) || retry.JitterFraction is < 0 or > 1
            || policy.AttemptTimeout <= TimeSpan.Zero || policy.AttemptTimeout > TimeSpan.FromDays(365)
            || policy.MinimumKeyRecognition <= TimeSpan.Zero || policy.MinimumKeyRecognition > TimeSpan.FromDays(3650))
            throw new ArgumentException("The Jobs retry, timeout or recognition policy is invalid.");
    }
    internal static void Association(JobAssociation? association)
    {
        if (association is { } a)
        {
            Name(a.ModelKind); Name(a.DefinitionId); Name(a.DefinitionVersion); Name(a.ExecutionId); Name(a.InvocationId); Name(a.MaterialId); Digest(a.MaterialSha256);
            if (!Enum.IsDefined(a.Mode) || a.Mode == JobAssociationMode.Awaited && a.Continuation is null)
                throw new ArgumentException("An awaited Jobs association must contain its model continuation payload.");
            if (a.Continuation is { } c)
            {
                Payload(c);
            }
        }
    }
    internal static void RequestMetadata(JobRequestMetadata value)
    {
        ArgumentNullException.ThrowIfNull(value); ArgumentNullException.ThrowIfNull(value.Handler);
        Name(value.Handler.JobType); Name(value.Handler.Version); Name(value.Queue);
        Schema(value.InputSchema); Schema(value.ResultSchema); Digest(value.InputSha256);
        Policy(value.Policy); Association(value.Association);
    }
    internal static JobGateKey[] Gates(JobRequest request, bool workflowOnly = false)
    {
        var keys = new List<JobGateKey>();
        if (!workflowOnly) { keys.Add(new(JobGateKind.Queue, request.Queue)); keys.Add(new(JobGateKind.JobType, request.Handler.JobType)); }
        if (request.Association is { } association)
        {
            // Model-local definition/instance names are qualified by their owning model.
            keys.Add(new(JobGateKind.WorkflowDefinition, association.DefinitionId, association.ModelKind));
            keys.Add(new(JobGateKind.WorkflowInstance, association.ExecutionId, association.ModelKind));
        }
        return keys.OrderBy(x => x.Kind).ThenBy(x => x.Name, StringComparer.Ordinal).ToArray();
    }
    internal static void Receipt(JobPhysicalReceipt receipt)
    {
        Name(receipt.ReceiptId); Name(receipt.AttemptId); Name(receipt.Owner); Digest(receipt.PayloadSha256);
        if (receipt.Outcome == JobAttemptOutcome.Succeeded && receipt.Payload is null)
            throw new ArgumentException("A successful Jobs receipt requires a schema-bearing payload envelope; JSON null remains a supported value.");
        if (receipt.Payload is { } payload) Payload(payload);
        if (receipt.Fence < 1 || !Enum.IsDefined(receipt.Outcome) || receipt.PayloadSha256 != JobPayloadDigest.Compute(receipt.Payload))
            throw new ArgumentException("The Jobs physical receipt identity or payload digest is invalid.");
        if (Encoding.UTF8.GetByteCount(Json(receipt)) > 2 * JobPayloadLimits.MaximumBytes) throw new ArgumentException("The Jobs receipt exceeds its bounded size.");
        Diagnostic(receipt.Diagnostic);
    }
    internal static void Diagnostic(JobDiagnostic? diagnostic)
    {
        if (diagnostic is null) return;
        Name(diagnostic.Code);
        ArgumentNullException.ThrowIfNull(diagnostic.Summary);
        if (diagnostic.Summary.Length > 2048) throw new ArgumentException("The Jobs diagnostic exceeds its bounded size.");
    }
    internal static void Gate(JobGateKey gate)
    {
        Name(gate.Name);
        if (!Enum.IsDefined(gate.Kind)) throw new ArgumentOutOfRangeException(nameof(gate));
        if (gate.Kind is JobGateKind.WorkflowDefinition or JobGateKind.WorkflowInstance) Name(gate.ModelKind!);
        else if (gate.ModelKind is not null) throw new ArgumentException("Queue and job-type gates do not have a model namespace.");
    }
    internal static void Operation(JobOperation operation) { Name(operation.OperationId); Name(operation.Actor); if (string.IsNullOrWhiteSpace(operation.Reason) || operation.Reason.Length > 2048) throw new ArgumentException("A bounded Jobs operation reason is required."); }
    internal static void LeaseDuration(TimeSpan duration) { if (duration < TimeSpan.FromSeconds(1) || duration > TimeSpan.FromDays(1)) throw new ArgumentOutOfRangeException(nameof(duration)); }
}
