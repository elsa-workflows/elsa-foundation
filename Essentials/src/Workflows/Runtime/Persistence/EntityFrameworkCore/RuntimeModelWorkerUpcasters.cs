using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

[EfSchemaUpcaster("2.0.0", "3.0.0")]
internal sealed class RuntimeSchedulerWorkV2Upcaster : IEfSchemaUpcaster
{
    public RuntimeSchedulerWorkV2Upcaster() { }
    public EfSchemaRowContent Upcast(EfSchemaRowContent row)
    {
        var item = RuntimeArtifactJson.Deserialize<RuntimeSchedulerWorkItem>(row[nameof(SchedulerWorkItemEntity.ContentJson)]
            ?? throw new InvalidDataException("Scheduler work content is missing."));
        if (RuntimeSchedulerWorkWriteVersion.IsModelWorker(item))
            throw new InvalidDataException("The earlier scheduler-work format cannot carry model-owned native admission.");
        return row;
    }
}

[EfSchemaUpcaster("1.0.0", "2.0.0")]
internal sealed class RuntimeWorkflowExecutionV1Upcaster : IEfSchemaUpcaster
{
    public RuntimeWorkflowExecutionV1Upcaster() { }
    public EfSchemaRowContent Upcast(EfSchemaRowContent row)
    {
        using var document = JsonDocument.Parse(row[nameof(WorkflowExecutionStateEntity.ContentJson)]
            ?? throw new InvalidDataException("Native execution content is missing."));
        if (document.RootElement.EnumerateObject().Any(property => property.Name.Equals("modelWorkOrigin", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("The earlier native execution format cannot carry a model-work origin.");
        return row;
    }
}
