namespace Elsa.Workflows.Design.Core.Models
{
    public class WorkflowOutputDefinition : WorkflowArgumentDefinition
    {
        public WorkflowOutputDefinition(string id, string name, string fullyQualifiedType) : base(id, name, fullyQualifiedType)
        {
        }

        public WorkflowOutputDefinition(string id, string name, Type type) : base(id, name, type)
        {
        }
    }

    public sealed class WorkflowInWorkflowOutputDefinitionputDefinition<TValueType>(string id, string name) : WorkflowOutputDefinition(id, name, typeof(TValueType))
    {
    }
}
