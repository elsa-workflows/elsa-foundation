namespace Kernel.Foundation.Contracts;

public enum ActorStatus { Running, Finished, Faulted, Cancelled }

public enum History { Full, State, Minimal }

public sealed record ActorHeader(
    ActorRef Ref,
    string Type,
    string Tenant,
    ActorStatus Status,
    string? Fault,
    ActorRef? Parent,
    long? EndedAt,
    string TypeData,
    IReadOnlyDictionary<string, string> Labels);

public sealed record StartSpec(
    string Type,
    string TypeData,
    string? Input = null,
    bool Ephemeral = false,
    History History = History.Full);

/// <summary>A routing hint. The foundation chooses the address.</summary>
public enum Placement { WithParent, Spread }

/// <summary>The inbox entry as seen by the actor. Times and classifications used by delivery stay internal.</summary>
public sealed record Message(string Id, string Kind, string? Payload = null, ActorRef? Sender = null, EventDelivered? Event = null);

public sealed record EventDelivered(string Key, string Id, string Wait);

public sealed record JobFinished(string Key, string? Result, string? Error);

/// <summary>A notice with Final set to false reports a Park and leaves the parent waiting.</summary>
public sealed record ChildFinished(ActorRef Child, ActorStatus Status, string? Output, bool Final);

public sealed record TimerSpec(string Id, string Kind, string? Payload, long Due);

public sealed record JobSpec(string Key, string? Arguments)
{
    /// <summary>The original absolute deadline on the actor's clock, when the job has a timeout.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public long? Deadline { get; init; }
}

public sealed record SubscriptionSpec(string Key, string Wait, bool Keep);

/// <summary>A supplied field is outside the foundation's representable metadata and message budget.</summary>
public sealed class MetadataLimitExceeded(string? parameter, int limitBytes)
    : ArgumentException($"The field must be representable in its text encoding and fit in {limitBytes:N0} bytes.", parameter)
{
    public int LimitBytes { get; } = limitBytes;
}

public sealed record RestorePlan(
    IReadOnlyList<TimerSpec> Timers,
    IReadOnlyList<JobSpec> Jobs,
    IReadOnlyList<SubscriptionSpec> Subscriptions);

public static class Kinds
{
    public const string Start = "start";
    public const string JobFinished = "job-finished";
    public const string Event = "event";
    public const string ChildFinished = "child-finished";
    public const string Suspend = "kernel.suspend";
    public const string Resume = "kernel.resume";
    public const string Terminate = "kernel.terminate";
    public const string Delete = "kernel.delete";
    public const string Recover = "kernel.recover";
    public const string Discard = "kernel.discard";
    public const string Migrate = "kernel.migrate";

    public static bool IsCommand(string kind) =>
        kind is Suspend or Resume or Terminate or Delete or Recover or Discard or Migrate;
}
