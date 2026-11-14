using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

[EfSchemaUpcaster("1.0.0", "1.1.0")]
internal sealed class RuntimeModelIngressV1Upcaster : IEfSchemaUpcaster
{
    public RuntimeModelIngressV1Upcaster() { }
    public EfSchemaRowContent Upcast(EfSchemaRowContent row)
    {
        if (row.Entity != typeof(RuntimeModelExecutionEntity)) return row;
        using var document = JsonDocument.Parse(row[nameof(RuntimeModelExecutionEntity.ContentJson)]
            ?? throw new InvalidDataException("A model state has no content."));
        if (document.RootElement.EnumerateObject().Any(property => property.Name.Equals("ingressOrigin", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("A predecessor model state cannot carry accepted ingress authority.");
        return row;
    }
}
