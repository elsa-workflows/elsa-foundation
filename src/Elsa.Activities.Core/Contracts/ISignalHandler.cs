using Elsa.Activities.Core.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace Elsa.Activities.Core.Contracts
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
