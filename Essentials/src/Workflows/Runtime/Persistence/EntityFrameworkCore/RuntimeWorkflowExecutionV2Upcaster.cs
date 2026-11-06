using Elsa.Persistence.EntityFramework;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

/// <summary>The payload is unchanged; v3 readers additionally honor canonical ingress execution retention.</summary>
[EfSchemaUpcaster("2.0.0", "3.0.0")]
internal sealed class RuntimeWorkflowExecutionV2Upcaster : IEfSchemaUpcaster
{
    public RuntimeWorkflowExecutionV2Upcaster() { }
    public EfSchemaRowContent Upcast(EfSchemaRowContent row) => row;
}
