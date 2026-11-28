using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

/// <summary>Preserves predecessor bytes while refusing history responsibility mislabeled as an older timer format.</summary>
[EfSchemaUpcaster("1.1.0", "1.2.0")]
internal sealed class RuntimeTimerHistoryV11Upcaster : IEfSchemaUpcaster
{
    public RuntimeTimerHistoryV11Upcaster() { }
    public EfSchemaRowContent Upcast(EfSchemaRowContent row)
    {
        if (row.Entity == typeof(ExecutionLivenessStateEntity) && row[nameof(ExecutionLivenessStateEntity.TimerHistoryOwnerJson)] is not null)
            throw new InvalidDataException("A predecessor ownership row cannot carry timer history authority.");
        if (row.Entity != typeof(DurableTimerEntity)) return row;
        using var document = JsonDocument.Parse(row[nameof(DurableTimerEntity.ContentJson)]
            ?? throw new InvalidDataException("A predecessor timer has no content."));
        if (document.RootElement.EnumerateObject().Any(property => property.Name.Equals("historyResponsibility", StringComparison.OrdinalIgnoreCase) &&
            property.Value.ValueKind != JsonValueKind.Null))
            throw new InvalidDataException("A predecessor timer cannot carry history responsibility.");
        return row;
    }
}
