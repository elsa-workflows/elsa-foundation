using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

/// <summary>Retains native content exactly; a v1 stamp never authorizes model input.</summary>
[EfSchemaUpcaster("1.0.0", "2.0.0")]
internal sealed class RuntimeSchedulerWorkV1Upcaster : IEfSchemaUpcaster
{
    public RuntimeSchedulerWorkV1Upcaster() { }

    public EfSchemaRowContent Upcast(EfSchemaRowContent row)
    {
        var item = RuntimeArtifactJson.Deserialize<RuntimeSchedulerWorkItem>(
            row[nameof(SchedulerWorkItemEntity.ContentJson)] ?? throw new InvalidDataException("Scheduler work content is missing."));
        if (item.CommandKind == WorkflowExecutionCommandKind.ExecuteModel || !Enum.IsDefined(item.CommandKind) ||
            RuntimeSchedulerWorkWriteVersion.IsModelWorker(item))
            throw new InvalidDataException("The native scheduler-work format cannot carry model or unknown commands.");
        return row;
    }
}
