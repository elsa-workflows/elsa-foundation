using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Activities.Runtime.Core.Contracts
{
    /// <summary>
    /// Handles signals.
    /// </summary>
    public interface ISignalHandler
    {
        /// <summary>
        /// Receives a signal.
        /// </summary>
        ValueTask ReceiveSignalAsync(object signal, SignalContext context);
    }
}
