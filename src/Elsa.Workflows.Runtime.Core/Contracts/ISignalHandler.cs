using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts
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
