using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

/// <summary>Preparation adds metadata; accepted predecessor material/source documents retain their bytes.</summary>
[EfSchemaUpcaster("1.1.0", "1.2.0")]
internal sealed class RuntimePreparationV11Upcaster : IEfSchemaUpcaster
{
    public RuntimePreparationV11Upcaster() { }

    public EfSchemaRowContent Upcast(EfSchemaRowContent row)
    {
        if (row.Entity == typeof(WorkflowPreparationRetentionEntity))
            throw new InvalidDataException("The predecessor RuntimeArtifact format cannot carry preparation metadata.");
        return row;
    }
}
