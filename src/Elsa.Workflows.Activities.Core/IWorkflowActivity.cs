using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Workflows.Design.Core;

namespace Elsa.Workflows.Activities.Core
{
    public interface IWorkflowActivity : IActivity
    {
        /// <summary>
        /// Gets or sets input definitions.
        /// </summary>
        ICollection<IInputDefinition> Inputs { get; }

        /// <summary>
        /// Gets or sets output definitions.
        /// </summary>
        ICollection<IOutputDefinition> Outputs { get; }

        /// <summary>
        /// Gets or sets possible outcomes for this workflow.
        /// </summary>
        ICollection<string> Outcomes { get; }

        /// <summary>
        /// Gets or sets options for the workflow.
        /// </summary>
        IWorkflowOptions Options { get; }

        /// <summary>
        /// Make workflow definition readonly.
        /// </summary>
        bool IsReadonly { get; }

        /// <summary>
        /// Gets or sets a value indicating whether the workflow is a system workflow.
        /// </summary>
        bool IsSystem { get; }
    }
}
