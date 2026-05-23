using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts
{
    /// <summary>
    /// Contract for custom activities that return a result.
    /// </summary>
    /// <typeparam name="T">The type of the result.</typeparam>
    public interface IActivityWithResult<T> : IActivityWithResult
    {
        /// <summary>
        /// The result of the activity.
        /// </summary>
        new ActivityOutput<T>? Result { get; set; }
    }

    /// <summary>
    /// Contract for custom activities that return a result.
    /// </summary>
    public interface IActivityWithResult
    {
        /// <summary>
        /// The result of the activity.
        /// </summary>
        OutputArgument? Result { get; set; }
    }
}
