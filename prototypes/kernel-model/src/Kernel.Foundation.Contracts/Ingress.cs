namespace Kernel.Foundation.Contracts;

/// <summary>The tenant selected by the trusted host for an ingress operation.</summary>
public sealed class BoundScope
{
    internal BoundScope(string tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        if (tenant.Length is < 1 or > 64 || tenant.Any(c => c is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '-')))
            throw new ArgumentException("A tenant id must contain 1 to 64 lowercase ASCII letters, digits or hyphens.", nameof(tenant));

        Tenant = tenant;
    }

    public string Tenant { get; }
}

/// <summary>What the bound ingress operation addresses.</summary>
public abstract class Target
{
    private Target() { }

    public sealed class Ref(ActorRef actor) : Target
    {
        public ActorRef Actor { get; } = actor;
    }

    public sealed class Key(string callerKey, StartSpec? spec = null) : Target
    {
        public string CallerKey { get; } = callerKey ?? throw new ArgumentNullException(nameof(callerKey));
        public StartSpec? Spec { get; } = spec;
    }

    public sealed class Event(string correlationKey) : Target
    {
        public string CorrelationKey { get; } = correlationKey ?? throw new ArgumentNullException(nameof(correlationKey));
    }

    public sealed class Start(StartSpec spec) : Target
    {
        public StartSpec Spec { get; } = spec ?? throw new ArgumentNullException(nameof(spec));
    }
}

public sealed record AddOptions(long? DueAt = null, TimeSpan? KeepFor = null);

public sealed record RequestOptions(object? Ambient, long Deadline, bool ReleaseBeforeCommit = false, bool Inline = false);

public enum AdmissionOutcome { Accepted, Duplicate, Refused }

public enum RefusalReason { RateLimited, NodeBusy, InboxFull, InvalidEventId, Repeat, StaleSelection }

public enum PublicationStatus { Pending, Matched, DeadLettered }
/// <summary>Acceptance and matching status. Each wait receives its broadcasts in match commit order.</summary>
public sealed record PublicationReceipt(string CorrelationKey, string EventId, Guid Incarnation, PublicationStatus Status);

public sealed record Admission(
    AdmissionOutcome Outcome,
    ActorRef? Actor = null,
    RefusalReason? Reason = null,
    TimeSpan? RetryAfter = null)
{
    public PublicationReceipt? Publication { get; init; }
}

public sealed record Reply(string? Channel, object? Item, ActorStatus? Status = null, string? Error = null);

public enum RecoverMode { Resume, Retry, RetryAll }

/// <summary>An operation on the actor life and mark identified by an opaque selection from the read model.</summary>
public sealed record OperatorCommand(
    string Selection,
    RecoverMode Mode = RecoverMode.Resume,
    IReadOnlyList<string>? Handles = null,
    string? Reason = null,
    bool ThenDelete = false);

public interface IIngress
{
    ValueTask<Admission> SendAsync(BoundScope scope, Target target, string id, string kind, string? payload = null,
        AddOptions? options = null, CancellationToken cancellationToken = default);

    ValueTask<Reply> RequestAsync(BoundScope scope, Target target, string id, string kind, string? payload,
        AddOptions? add, RequestOptions request, CancellationToken cancellationToken = default);

    ValueTask SetStartAsync(BoundScope scope, string key, string wait, StartSpec? spec, CancellationToken cancellationToken = default);
}
