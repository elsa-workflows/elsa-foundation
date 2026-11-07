using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

[EfSchemaUpcaster("3.0.0", "4.0.0")]
internal sealed class RuntimeSchedulerWorkV3Upcaster : IEfSchemaUpcaster
{
    public RuntimeSchedulerWorkV3Upcaster() { }
    public EfSchemaRowContent Upcast(EfSchemaRowContent row)
    {
        var item = RuntimeArtifactJson.Deserialize<RuntimeSchedulerWorkItem>(row[nameof(SchedulerWorkItemEntity.ContentJson)]
            ?? throw new InvalidDataException("Scheduler work content is missing."));
        if (RuntimeTestEntryWriteVersion.Requires(item))
            throw new InvalidDataException("The predecessor scheduler-work format cannot carry selected test entry.");
        return row;
    }
}

[EfSchemaUpcaster("2.0.0", "3.0.0")]
internal sealed class RuntimeWorkflowExecutionV2Upcaster : IEfSchemaUpcaster
{
    public RuntimeWorkflowExecutionV2Upcaster() { }
    public EfSchemaRowContent Upcast(EfSchemaRowContent row)
    {
        using var document = JsonDocument.Parse(row[nameof(WorkflowExecutionStateEntity.ContentJson)]
            ?? throw new InvalidDataException("Native execution content is missing."));
        if (RuntimeTestEntryWriteVersion.HasEntry(document.RootElement))
            throw new InvalidDataException("The predecessor execution format cannot carry selected test entry or context.");
        return row;
    }
}

[EfSchemaUpcaster("2.0.0", "3.0.0")]
internal sealed class RuntimePostCommitOutboxV2Upcaster : IEfSchemaUpcaster
{
    public RuntimePostCommitOutboxV2Upcaster() { }
    public EfSchemaRowContent Upcast(EfSchemaRowContent row)
    {
        var item = RuntimeArtifactJson.Deserialize<RuntimePostCommitOutboxItem>(row[nameof(RuntimePostCommitOutboxEntity.ContentJson)]
            ?? throw new InvalidDataException("Post-commit outbox content is missing."));
        if (RuntimeTestEntryWriteVersion.Requires(item))
            throw new InvalidDataException("The predecessor outbox format cannot carry selected test admission.");
        return row;
    }
}

[EfSchemaUpcaster("1.0.0", "2.0.0")]
internal sealed class RuntimeOperationalStateV1Upcaster : IEfSchemaUpcaster
{
    public RuntimeOperationalStateV1Upcaster() { }
    public EfSchemaRowContent Upcast(EfSchemaRowContent row)
    {
        if (row.Entity == typeof(DurableTimerEntity) && row.Columns.TryGetValue(nameof(DurableTimerEntity.ContentJson), out var content) && content is not null)
        {
            using var document = JsonDocument.Parse(content);
            if (document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.EnumerateObject().Any(x =>
                x.Name.Equals("businessTime", StringComparison.OrdinalIgnoreCase) && x.Value.ValueKind == JsonValueKind.Object &&
                x.Value.EnumerateObject().Any(stamp => stamp.Name.Equals("clockId", StringComparison.OrdinalIgnoreCase) && stamp.Value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))))
                throw new InvalidDataException("The predecessor timer format cannot carry selected test business time.");
        }
        return row;
    }
}

[EfSchemaUpcaster("1.0.0", "2.0.0")]
internal sealed class RuntimeBackgroundActivityWorkV1Upcaster : IEfSchemaUpcaster
{
    public RuntimeBackgroundActivityWorkV1Upcaster() { }
    public EfSchemaRowContent Upcast(EfSchemaRowContent row) => row;
}
