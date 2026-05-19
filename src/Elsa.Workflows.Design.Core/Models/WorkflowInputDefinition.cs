namespace Elsa.Workflows.Design.Core.Models
{
    public class WorkflowInputDefinition : WorkflowArgumentDefinition
    {
        public WorkflowInputDefinition(string id, string name, string fullyQualifiedType) : base(id, name, fullyQualifiedType)
        {
        }

        public WorkflowInputDefinition(string id, string name, Type type) : base(id, name, type)
        {
        }
    }

    public sealed class WorkflowInputDefinition<TValueType>(string id, string name) : WorkflowInputDefinition(id, name, typeof(TValueType))
    {
    }
}
