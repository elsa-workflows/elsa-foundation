namespace Elsa.Workflows.Design.Core
{
    public interface IWorkflowOptions
    {
        /// <summary>
        /// The type of <see cref="IWorkflowActivationStrategy"/> to apply when new instances are requested to be created.
        /// </summary>
        Type? ActivationStrategyType { get; set; }

        /// <summary>
        /// Used to decide if the workflow can be used as an activity.
        /// </summary>
        bool? UsableAsActivity { get; set; }

        /// <summary>
        /// Used to decide if the consuming workflows should be updated automatically to use the last published version of the workflow when it is published.
        /// </summary>
        bool AutoUpdateConsumingWorkflows { get; set; }

        /// <summary>
        /// The category to use when the workflow is used as an activity.
        /// </summary>
        string? ActivityCategory { get; set; }

        /// <summary>
        /// The type of <see cref="IIncidentStrategy"/> to use when a fault occurs in the workflow.
        /// </summary>
        Type? IncidentStrategyType { get; set; }

        /// <summary>
        /// The options for committing workflow state.
        /// </summary>
        string? CommitStrategyName { get; set; }
    }
}
