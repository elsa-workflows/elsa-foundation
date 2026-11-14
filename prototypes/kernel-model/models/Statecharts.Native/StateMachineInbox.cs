using Kernel.Native;

namespace Kernel.Statecharts.Native;

internal sealed record MachineInboxSettings(int Capacity, int ReceiptCapacity, long RetentionMilliseconds);

internal sealed partial class StateMachine : IInvocationInputReceiver
{
    [Input] public MachineInboxSettings? InputInbox { get; set; }

    bool IInvocationInputReceiver.CanAcceptInput(CompositeContext context) => State(context) is { Inbox: not null, Pending: null };

    void IInvocationInputReceiver.AcceptInput(CompositeContext context, InvocationInput input)
    {
        // The owner has just selected this exact receipt. Validate that relationship before chart evaluation.
        var state = Restore(context.ReadState<MachineState>()!, Compile(context), context.FrameId);
        ValidateResources(context, state);
        ValidateScopeParents(context, state, Compile(context));
        if (state.Inbox is null || state.Pending is not null || state.SelectedInput is not null || input.Frame != context.FrameId)
            throw new InvalidOperationException("Statechart cannot select this admitted input.");
        ValidateInbox(context, state with { SelectedInput = input.Id });
        React(context, state with { SelectedInput = input.Id }, input.Id, input.Name, input.Value);
    }

    private static void ValidateInbox(CompositeContext context, MachineState state)
    {
        if (state.Inbox is { } settings)
            context.ValidateInputInbox(settings.Capacity, settings.ReceiptCapacity, settings.RetentionMilliseconds, state.SelectedInput);
    }

    private static void ValidateInboxState(MachineState state)
    {
        if (state.Version < 5 && (state.Inbox is not null || state.SelectedInput is not null) ||
            state.Version == 5 && (state.Inbox is not { Capacity: > 0, RetentionMilliseconds: > 0 } s || s.ReceiptCapacity < s.Capacity) ||
            state.SelectedInput is { } id && (string.IsNullOrEmpty(id) || id.Length > 128 || state.Inbox is null || state.Pending?.EventId != id) ||
            state.Inbox is not null && state.Pending?.EventId is not null && state.SelectedInput is null)
            throw new InvalidOperationException("Invalid statechart admitted-input checkpoint.");
    }
}
