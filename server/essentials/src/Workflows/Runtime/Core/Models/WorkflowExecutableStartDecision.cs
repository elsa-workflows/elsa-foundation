namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Immutable, input-free context supplied to the executable start replacement policy.</summary>
public sealed class WorkflowExecutableStartPolicyContext
{
    public WorkflowExecutableStartPolicyContext(
        WorkflowExecutableIdentity executable,
        WorkflowExecutableStartAuthorityKind authorityKind,
        string requestedBy,
        WorkflowExecutionAuthoritySnapshot authority,
        WorkflowRunKind runKind,
        string? tenantId = null,
        WorkflowExecutionPartition? partition = null,
        int dispatchNestingDepth = 0)
    {
        ArgumentNullException.ThrowIfNull(executable);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedBy);
        ArgumentNullException.ThrowIfNull(authority);
        if (!Enum.IsDefined(authorityKind))
            throw new ArgumentOutOfRangeException(nameof(authorityKind), authorityKind, "Unknown executable start authority kind.");
        if (tenantId is not null && string.IsNullOrWhiteSpace(tenantId))
            throw new ArgumentException("Tenant ID cannot be blank when provided.", nameof(tenantId));
        if (dispatchNestingDepth < 0)
            throw new ArgumentOutOfRangeException(nameof(dispatchNestingDepth), "Dispatch nesting depth cannot be negative.");
        if (!StringComparer.Ordinal.Equals(requestedBy, authority.SystemIdentity))
            throw new ArgumentException("RequestedBy must match the execution authority system identity.", nameof(authority));

        Executable = executable;
        AuthorityKind = authorityKind;
        RequestedBy = requestedBy;
        Authority = authority;
        RunKind = runKind;
        TenantId = tenantId;
        Partition = partition;
        DispatchNestingDepth = dispatchNestingDepth;
    }

    public WorkflowExecutableIdentity Executable { get; }
    public WorkflowExecutableStartAuthorityKind AuthorityKind { get; }
    public string RequestedBy { get; }
    public WorkflowExecutionAuthoritySnapshot Authority { get; }
    public WorkflowRunKind RunKind { get; }
    public string? TenantId { get; }
    public WorkflowExecutionPartition? Partition { get; }
    public int DispatchNestingDepth { get; }
}

/// <summary>A stable allow/deny result for one future executable start.</summary>
public sealed class WorkflowExecutableStartDecision
{
    public WorkflowExecutableStartDecision(
        WorkflowExecutableStartDecisionKind kind,
        string? reasonCode = null,
        string? message = null)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown executable start decision kind.");

        if (kind == WorkflowExecutableStartDecisionKind.Allow && (reasonCode is not null || message is not null))
            throw new ArgumentException("Allow decisions cannot carry a denial reason or message.");

        if (kind == WorkflowExecutableStartDecisionKind.Deny)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);
            ArgumentException.ThrowIfNullOrWhiteSpace(message);

            if (!StringComparer.Ordinal.Equals(reasonCode, reasonCode.Trim()))
                throw new ArgumentException("Start denial reason codes cannot have leading or trailing whitespace.", nameof(reasonCode));
            if (!StringComparer.Ordinal.Equals(message, message.Trim()))
                throw new ArgumentException("Start denial messages cannot have leading or trailing whitespace.", nameof(message));
        }

        Kind = kind;
        ReasonCode = reasonCode;
        Message = message;
    }

    public WorkflowExecutableStartDecisionKind Kind { get; }
    public string? ReasonCode { get; }
    public string? Message { get; }
    public bool IsAllowed => Kind == WorkflowExecutableStartDecisionKind.Allow;

    public static WorkflowExecutableStartDecision Allow() => new(WorkflowExecutableStartDecisionKind.Allow);

    public static WorkflowExecutableStartDecision Deny(string reasonCode, string message) =>
        new(WorkflowExecutableStartDecisionKind.Deny, reasonCode, message);
}

public enum WorkflowExecutableStartDecisionKind
{
    Allow = 0,
    Deny = 1
}
