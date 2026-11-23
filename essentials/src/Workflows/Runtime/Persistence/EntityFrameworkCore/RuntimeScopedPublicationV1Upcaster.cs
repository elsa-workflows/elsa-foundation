using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

[EfSchemaUpcaster("1.0.0", "1.1.0")]
internal sealed class RuntimeScopedPublicationV1Upcaster : IEfSchemaUpcaster
{
    public RuntimeScopedPublicationV1Upcaster() { }
    public EfSchemaRowContent Upcast(EfSchemaRowContent row)
    {
        if (row.Entity != typeof(WorkflowExecutableSourceReferenceEntity)) return row;
        using var document = JsonDocument.Parse(row[nameof(WorkflowExecutableSourceReferenceEntity.ContentJson)]
            ?? throw new InvalidDataException("A publication source has no content."));
        if (HasScope(document.RootElement) || document.RootElement.EnumerateObject()
            .Where(property => property.Name.Equals("reference", StringComparison.OrdinalIgnoreCase)).Any(property => HasScope(property.Value)))
            throw new InvalidDataException("A predecessor source format cannot carry explicit publication scope authority.");
        return row;
    }
    private static bool HasScope(JsonElement value) => value.ValueKind == JsonValueKind.Object &&
        value.EnumerateObject().Any(property => property.Name.Equals("publicationScope", StringComparison.OrdinalIgnoreCase));
}
