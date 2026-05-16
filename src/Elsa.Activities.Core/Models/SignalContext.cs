using Elsa.Activities.Core.Contracts;

namespace Elsa.Activities.Core.Models
{

    /// <summary>
    /// Provides contextual information about a signal.
    /// </summary>
    /// <remarks>
    /// Initializes a new instance of the <see cref="SignalContext"/> class.
    /// </remarks>
    public sealed class SignalContext(
        IActivityExecutionContext receiverActivityExecutionContext, 
        IActivityExecutionContext senderActivityExecutionContext, 
        CancellationToken cancellationToken
    )
    {

        /// <summary>
        /// The <see cref="ActivityExecutionContext"/> receiving the signal.
        /// </summary>
        public IActivityExecutionContext ReceiverActivityExecutionContext { get; init; } = receiverActivityExecutionContext;

        /// <summary>
        /// The <see cref="ActivityExecutionContext"/> sending the signal.
        /// </summary>
        public IActivityExecutionContext SenderActivityExecutionContext { get; init; } = senderActivityExecutionContext;

        /// <summary>
        /// Returns true if the receiver is the same as the sender.
        /// </summary>
        public bool IsSelf => SenderActivityExecutionContext.Activity == ReceiverActivityExecutionContext.Activity;

        /// <summary>
        /// The cancellation token.
        /// </summary>
        public CancellationToken CancellationToken { get; init; } = cancellationToken;

        internal bool StopPropagationRequested { get; private set; }

        /// <summary>
        /// Stops the signal from propagating further up the activity execution context hierarchy.
        /// </summary>
        public void StopPropagation() => StopPropagationRequested = true;
    }
}
