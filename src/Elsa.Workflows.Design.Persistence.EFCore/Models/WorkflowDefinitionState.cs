using Elsa.Expressions.Core.Contracts;
using Elsa.Workflows.Design.Core;
using Elsa.Workflows.Design.Core.Options;
using System.Text.Json.Serialization;

namespace Elsa.Workflows.Design.Persistence.EFCore.Models
{
    internal class WorkflowDefinitionState
    {
        [JsonConstructor]
        public WorkflowDefinitionState()
        {
        }

        public WorkflowDefinitionState(
            IWorkflowOptions options,
            ICollection<IVariable> variables,
            ICollection<IInputDefinition> inputs,
            ICollection<IOutputDefinition> outputs,
            ICollection<string> outcomes,
            IDictionary<string, object> customProperties
        )
        {
            Options = options;
            Variables = variables;
            Inputs = inputs;
            Outputs = outputs;
            Outcomes = outcomes;
            CustomProperties = customProperties;
        }

        public IWorkflowOptions Options { get; set; } = new WorkflowOptions();
        public ICollection<IVariable> Variables { get; set; } = [];
        public ICollection<IInputDefinition> Inputs { get; set; } = [];
        public ICollection<IOutputDefinition> Outputs { get; set; } = [];
        public ICollection<string> Outcomes { get; set; } = [];
        public IDictionary<string, object> CustomProperties { get; set; } = new Dictionary<string, object>();
    }
}
