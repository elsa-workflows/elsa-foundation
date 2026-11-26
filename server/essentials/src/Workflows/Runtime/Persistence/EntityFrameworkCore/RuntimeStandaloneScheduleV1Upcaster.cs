using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

/// <summary>Preserves predecessor operational bytes and refuses resource schedule state mislabeled as the old format.</summary>
[EfSchemaUpcaster("1.0.0", "1.1.0")]
internal sealed class RuntimeStandaloneScheduleV1Upcaster : IEfSchemaUpcaster
{
    public RuntimeStandaloneScheduleV1Upcaster() { }
    public EfSchemaRowContent Upcast(EfSchemaRowContent row)
    {
        if (row.Entity != typeof(RecurringTriggerScheduleEntity)) return row;
        using var document = JsonDocument.Parse(row[nameof(RecurringTriggerScheduleEntity.ContentJson)]
            ?? throw new InvalidDataException("A recurring schedule has no content."));
        if (document.RootElement.EnumerateObject().Any(property => property.Name.Equals("resource", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("A predecessor schedule cannot carry standalone resource authority.");
        return row;
    }
}
