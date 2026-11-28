using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

[EfSchemaUpcaster("3.0.0", "4.0.0")]
internal sealed class RuntimeSchedulerIngressV3Upcaster : IEfSchemaUpcaster
{
    public RuntimeSchedulerIngressV3Upcaster() { }
    public EfSchemaRowContent Upcast(EfSchemaRowContent row)
    {
        var item = RuntimeArtifactJson.Deserialize<RuntimeSchedulerWorkItem>(row[nameof(SchedulerWorkItemEntity.ContentJson)]
            ?? throw new InvalidDataException("Scheduler work has no content."));
        if (RuntimeSchedulerWorkWriteVersion.IsIngress(item))
            throw new InvalidDataException("A predecessor scheduler format cannot carry accepted ingress authority.");
        return row;
    }
}

[EfSchemaUpcaster("2.0.0", "2.1.0")]
internal sealed class RuntimeWorkflowIngressV2Upcaster : IEfSchemaUpcaster
{
    public RuntimeWorkflowIngressV2Upcaster() { }
    public EfSchemaRowContent Upcast(EfSchemaRowContent row)
    {
        using var document = JsonDocument.Parse(row[nameof(WorkflowExecutionStateEntity.ContentJson)]
            ?? throw new InvalidDataException("Workflow state has no content."));
        if (document.RootElement.EnumerateObject().Any(property => (property.Name.Equals("ingressOrigin", StringComparison.OrdinalIgnoreCase) || property.Name.Equals("stimulusDeliveryOrigin", StringComparison.OrdinalIgnoreCase))) ||
            document.RootElement.EnumerateObject().Where(property => property.Name.Equals("modelWorkOrigin", StringComparison.OrdinalIgnoreCase))
                .Any(property => property.Value.ValueKind == JsonValueKind.Object && property.Value.EnumerateObject().Any(nested => nested.Name.Equals("ingressOrigin", StringComparison.OrdinalIgnoreCase))))
            throw new InvalidDataException("A predecessor workflow format cannot carry accepted ingress authority.");
        return row;
    }
}
