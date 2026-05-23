using Elsa.Activities.Design.Core.Models;

namespace Elsa.Workflows.Design.Core.Contracts
{
    public interface IActivityState
    {
        /// <summary>
        /// 
        /// </summary>
        string ActivityDefinitionId { get; }

        /// <summary>
        /// 
        /// </summary>
        string ReferenceKey { get; }

        /// <summary>
        /// 
        /// </summary>
        IEnumerable<IArgumentState> Inputs { get; }

        /// <summary>
        /// 
        /// </summary>
        IEnumerable<IArgumentState> Outputs { get; }

        /// <summary>
        /// A value indicating whether this activity is a container of child activities.
        /// </summary>
        bool IsContainer { get; }

        /// <summary>
        /// Whether this activity type is a start activity.
        /// </summary>
        bool IsStart { get; }

        /// <summary>
        /// Whether this activity type is a terminal activity.
        /// </summary>
        bool IsTerminal { get; }

        /// <summary>
        /// Data used for displaying this activity in the correct location within the workflow.
        /// </summary>
        ActivityDisplayInfo DisplayInfo { get; } 
    }
}
