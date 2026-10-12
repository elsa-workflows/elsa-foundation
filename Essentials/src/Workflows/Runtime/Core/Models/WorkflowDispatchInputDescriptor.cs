namespace Elsa.Workflows.Runtime.Core.Models;

public sealed record WorkflowDispatchInputDescriptor
{
    public WorkflowDispatchInputDescriptor(string name, string valueType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(valueType);

        Name = name;
        ValueType = valueType;
    }

    public string Name { get; }
    public string ValueType { get; }
}
