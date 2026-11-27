using Elsa.Persistence.EntityFramework;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

internal static class RuntimeKernelScheduleOccurrenceEfModule
{
    internal const string SchemaFamily = "RuntimeKernelScheduleOccurrence";
    internal const string SchemaVersion = "1.0.0";
    internal const string TableName = "elsa_runtime_kernel_schedule_occurrence";

    internal static readonly EfSchemaChain Chain = EfSchemaChain.Of(typeof(RuntimeKernelScheduleOccurrenceEfModule).Assembly, SchemaFamily);
}
