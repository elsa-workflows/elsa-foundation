using System.Text.Json.Serialization;
using CShells.Lifecycle;
using Elsa.Foundation.Host.Shells;

namespace Elsa.Foundation.Host.Health;

/// <summary>The stable codes of <see cref="ShellNotActiveReason.Code"/>, which tooling and operators match on.</summary>
public static class ShellNotActiveReasonCodes
{
    /// <summary>The last activation failed with a fault, and the host retries.</summary>
    public const string ActivationFailed = "activation-failed";

    /// <summary>An EF module refused the activation; an operator resolves it and the host checks back.</summary>
    public const string ActivationRefused = "activation-refused";

    /// <summary>The shell exists and is between states, for example mid-reload.</summary>
    public const string ShellNotServing = "shell-not-serving";

    /// <summary>No attempt has failed, so the first is still to come or in flight (eager activation) or waits for the first request (lazy).</summary>
    public const string NotActivated = "not-activated";
}

/// <summary>
/// Why a configured shell is not active, in what a public, unauthenticated probe may say: a stable code, and for a failed
/// activation how many attempts have failed and when the host tries next. Nothing that names the failure: not its exception type,
/// not the EF module or its migrations. Those are in the host log and in the <c>shell-activation</c> Attention item, which is
/// behind a permission.
/// </summary>
/// <param name="Code">One of <see cref="ShellNotActiveReasonCodes"/>.</param>
/// <param name="Attempts">How many activations have failed in a row.</param>
/// <param name="NextAttemptAt">When the host tries again.</param>
public sealed record ShellNotActiveReason(
    string Code,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Attempts = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? NextAttemptAt = null)
{
    public static ShellNotActiveReason For(IShell? active, ShellActivationFailure? failure) => (active, failure) switch
    {
        (not null, _) => new(ShellNotActiveReasonCodes.ShellNotServing),
        (_, null) => new(ShellNotActiveReasonCodes.NotActivated),
        (_, { Refusal: not null }) => new(ShellNotActiveReasonCodes.ActivationRefused, failure.Attempts, failure.NextAttemptAt),
        _ => new(ShellNotActiveReasonCodes.ActivationFailed, failure.Attempts, failure.NextAttemptAt)
    };
}
