using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Expressions.Core.Extensions;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Design.Core.Contracts
{
    public interface IWorkflowDesignContext
    {
        WorkflowDefinitionState Draft { get; }

        IEnumerable<IActivityDefinitionVersion> Activities { get; }

        IActivityDefinitionVersion? FindActivity(string activityTypeKey) => Activities.FirstOrDefault(x => x.Definition.ActivityTypeKey == activityTypeKey);

        IEnumerable<IActivityDefinitionVersion> GetActivitiesWithOutput() => Activities
            .Where(x => x.Definition.ActivityTypeKey.IsValidVariableName());

        IEnumerable<InputDefinition> GetWorkflowInputs() => Draft.Inputs.Where(x => VariableNameValidator.IsValidVariableName(x.Name));

        IEnumerable<VariableDefinition> GetVariableDefinitions() => Draft.Variables.Where(x => VariableNameValidator.IsValidVariableName(x.Name));
    }
}
