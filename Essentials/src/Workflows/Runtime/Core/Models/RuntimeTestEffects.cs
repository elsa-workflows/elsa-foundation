using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Physical invocation facts, reloaded and validated by the trusted gate before activation or effects.</summary>
public sealed record RuntimeTestInvocation(string WorkflowExecutionId, string ActivityExecutionId,
    string AttemptId, string ExecutableNodeId);

/// <summary>Node qualification precedes input evaluation, before a physical activation attempt exists.</summary>
public sealed record RuntimeTestNodeRequest(string WorkflowExecutionId, string ActivityExecutionId,
    WorkflowExecutableIdentity Executable, ExecutableNode Node);

/// <summary>The supplied activation must agree with its actual pinned node before any constructor runs.</summary>
public sealed record RuntimeTestActivationRequest(string WorkflowExecutionId, string ActivityExecutionId,
    string AttemptId, ActivityContract Contract, RuntimeActivityDescriptor? Descriptor);

/// <summary>
/// OperationId is stable across physical retries. AttemptId fences the current caller; it must not create a fresh
/// receipt namespace and permit repeating an unresolved effect. A deliberate new test attempt has a new context.
/// Only the fingerprint, rather than request values or credentials, belongs in the interaction journal.
/// </summary>
public sealed record RuntimeTestEffectRequest(RuntimeTestInvocation Invocation, string EffectKey,
    string OperationId, string RequestFingerprint);

public enum RuntimeTestEffectMode { Ordinary, Substitute, TestSystem }
public enum RuntimeTestEffectDisposition { Completed, Refused, Unknown }

/// <summary>
/// Data returned by the trusted gate, not a caller-issued authority token. Every use and completion is revalidated.
/// Configuration is an exact module-owned fixture or authorized test-system mapping, never production fallback.
/// A prior performed or uncertain operation cannot execute again merely because a new activation asks for it.
/// </summary>
public sealed class RuntimeTestEffectAdmission
{
    public RuntimeTestEffectAdmission(RuntimeTestEffectRequest request, RuntimeTestEffectMode mode,
        string profile, JsonElement configuration, string? contextId, string? contextFingerprint,
        string? receiptId, long revision, bool canExecute)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(profile);
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (revision < 0) throw new ArgumentOutOfRangeException(nameof(revision));
        if (mode != RuntimeTestEffectMode.Ordinary &&
            (string.IsNullOrWhiteSpace(contextId) || string.IsNullOrWhiteSpace(contextFingerprint) || string.IsNullOrWhiteSpace(receiptId)))
            throw new ArgumentException("A test effect requires its exact context and retained receipt.");
        if (configuration.ValueKind == JsonValueKind.Undefined) throw new ArgumentException("Effect configuration must be explicit.", nameof(configuration));
        Request = request;
        Mode = mode;
        Profile = profile;
        Configuration = configuration.Clone();
        ContextId = contextId;
        ContextFingerprint = contextFingerprint;
        ReceiptId = receiptId;
        Revision = revision;
        CanExecute = canExecute;
    }

    public RuntimeTestEffectRequest Request { get; }
    public RuntimeTestEffectMode Mode { get; }
    public string Profile { get; }
    public JsonElement Configuration { get; }
    public string? ContextId { get; }
    public string? ContextFingerprint { get; }
    public string? ReceiptId { get; }
    public long Revision { get; }
    public bool CanExecute { get; }
}

/// <summary>Exact retained receipt transition; no raw response or credential value is journaled.</summary>
public sealed record RuntimeTestEffectCompletion(RuntimeTestEffectAdmission Admission,
    RuntimeTestEffectDisposition Disposition, string? ResultFingerprint = null);
