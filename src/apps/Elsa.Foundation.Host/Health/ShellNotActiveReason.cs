using System.Text.Json.Serialization;
using CShells.Lifecycle;
using Elsa.Foundation.Host.Shells;

namespace Elsa.Foundation.Host.Health;

/// <summary>
/// Why a configured shell is not active, in what a public probe may say: a code, and for a failed activation its exception type
/// (never the message), how many attempts have failed and when the host tries next. An EF module's refusal also names the
/// module and its pending migrations, which are names and never settings. The command that resolves it is left to the host log
/// and the reload endpoint's answer, because it carries the host's directory.
/// </summary>
/// <param name="Code">
/// <c>activation-failed</c>: a fault, and the host retries. <c>activation-refused</c>: an EF module refused, an operator resolves
/// it and the host checks back. <c>shell-not-serving</c>: the shell exists and is between states. <c>not-activated</c>: no attempt
/// has failed, so the first is still to come or in flight (eager activation) or waits for the first request (lazy).
/// </param>
/// <param name="FailureType">The type of the exception that stopped the last attempt.</param>
/// <param name="Attempts">How many activations have failed in a row.</param>
/// <param name="NextAttemptAt">When the host tries again.</param>
/// <param name="Refusal">The EF module's refusal, when that is what stopped it.</param>
public sealed record ShellNotActiveReason(
    string Code,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? FailureType = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Attempts = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? NextAttemptAt = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ShellActivationRefusal? Refusal = null)
{
    public static ShellNotActiveReason For(IShell? active, ShellActivationFailure? failure) => (active, failure) switch
    {
        (not null, _) => new("shell-not-serving"),
        (_, null) => new("not-activated"),
        (_, { Refusal: not null }) => new("activation-refused", failure.FailureType, failure.Attempts, failure.NextAttemptAt, failure.Refusal),
        _ => new("activation-failed", failure.FailureType, failure.Attempts, failure.NextAttemptAt)
    };
}
