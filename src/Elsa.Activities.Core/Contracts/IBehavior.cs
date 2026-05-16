using System;
using System.Collections.Generic;
using System.Text;

namespace Elsa.Activities.Core.Contracts
{
    /// <summary>
    /// Represents a behavior that can be attached to an activity.
    /// </summary>
    public interface IBehavior : ISignalHandler
    {
        /// <summary>
        /// The owner of this behavior.
        /// </summary>
        IActivity Owner { get; }

        /// <summary>
        /// Invoked when the activity executes.
        /// </summary>
        ValueTask ExecuteAsync(IActivityExecutionContext context);
    }
}
