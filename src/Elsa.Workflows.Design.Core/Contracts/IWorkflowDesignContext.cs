using Elsa.Activities.Design.Core.Contracts;
using Elsa.Expressions.Core.Extensions;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Design.Core.Contracts
{
    public interface IWorkflowDesignContext
    {
        IWorkflowDefinitionState Draft { get; }

        IEnumerable<IActivityDefinitionVersion> Activities { get; }

        public IActivityDefinitionVersion? FindActivity(string uniqueName) => Activities.FirstOrDefault(x => x.Definition.UniqueName == uniqueName);

        public IEnumerable<IActivityDefinitionVersion> GetActivitiesWithOutput() => Activities
            .Where(x => x.Definition.UniqueName.IsValidVariableName());

        public IEnumerable<IInputDefinition> GetWorkflowInputs() => Draft.Inputs.Where(x => VariableNameValidator.IsValidVariableName(x.Name));

        public IEnumerable<VariableDefinition> GetVariableDefinitions() => Draft.Variables.Where(x => VariableNameValidator.IsValidVariableName(x.Name));
    }
}
