using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Distributed.Models;
using Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.Stores;

namespace Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore;

[EfSchemaUpcaster("1.0.0", "2.0.0")]
internal sealed class ExecutionCommandTransportV1Upcaster : IEfSchemaUpcaster
{
    public ExecutionCommandTransportV1Upcaster() { }
    public EfSchemaRowContent Upcast(EfSchemaRowContent row)
    {
        if (row.Entity != typeof(ExecutionCommandTransportItemEntity)) return row;
        var content = row[nameof(ExecutionCommandTransportItemEntity.PayloadJson)]
            ?? throw new InvalidDataException("Command transport predecessor content is missing.");
        var item = JsonSerializer.Deserialize<ExecutionCommandTransportItem>(content, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException("Command transport predecessor content is missing.");
        if (ExecutionCommandTransportWriteVersion.Requires(item.Envelope))
            throw new InvalidDataException("The predecessor command-transport format cannot carry selected test entry or context.");
        return row;
    }
}
