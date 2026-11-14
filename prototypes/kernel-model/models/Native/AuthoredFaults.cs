using System.Reflection;
using System.Text.Json.Nodes;

namespace Kernel.Native;

internal sealed partial class Burst
{
    private static Exception AuthoredCause(Exception error) => error is TargetInvocationException { InnerException: { } inner } ? inner : error;
    private bool HandlesAuthoredFault(Frame frame, Exception error) => NativeInterventions.Policy(_s) >= 5 && frame.RecoveryInputs is not null &&
        !(AuthoredCause(error) is OperationCanceledException && cancellationToken.IsCancellationRequested);

    private static Transition AuthoredFault(Exception error)
    {
        var cause = AuthoredCause(error);
        var message = cause.GetType().FullName + ": " + cause.Message;
        return new(TransitionKind.Fault, Fault: new ActivityFault("exception", message[..Math.Min(message.Length, 2048)]));
    }

    private ActivityBase? CreateActivity(Frame frame, Node node, JsonObject? pinned, IScope scope)
    {
        // Binding evaluation and material/scope validation are intentionally outside the authored catch.
        if (pinned is null) return Hydrator.Create(node, b => Evaluate(b, scope));
        try { return Hydrator.Create(node, pinned); }
        catch (Exception error) when (HandlesAuthoredFault(frame, error))
        { Apply(frame, AuthoredFault(error)); return null; }
    }

    private async ValueTask<Transition> InvokeAuthored(Frame frame, Func<ValueTask<Transition>> invoke)
    {
        try { return await invoke(); }
        catch (Exception error) when (HandlesAuthoredFault(frame, error)) { return AuthoredFault(error); }
    }
}
