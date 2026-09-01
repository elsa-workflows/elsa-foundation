namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Continuation retained by the resumption pump between bounded sweep passes.
/// </summary>
/// <remarks>
/// The scan time is retained with the provider token. A recovery page binds its ordering predicates to that time;
/// advancing the clock while reusing a cursor could skip rows that newly enter an earlier route frontier.
/// </remarks>
public sealed record RuntimeRecoverySweepCursor(
    DateTimeOffset ScanNow,
    TimeSpan LeaseTimeout,
    TimeSpan HeartbeatTimeout,
    int Limit,
    string ContinuationToken);
