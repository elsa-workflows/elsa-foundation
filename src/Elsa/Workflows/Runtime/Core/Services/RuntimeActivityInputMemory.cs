using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public static class RuntimeActivityInputMemory
{
    public static void Seed(SimpleActivityExecutionContext context, IReadOnlyList<RuntimeMaterializedActivityInput> inputs)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(inputs);

        foreach (var input in inputs)
            context.Set(input.Argument.MemoryBlockReference(), input.Value);
    }
}
