using Elsa.Persistence.EntityFramework;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

internal static class RuntimeKernelScheduleOccurrenceEfModule
{
    internal const string SchemaFamily = "RuntimeKernelScheduleOccurrence";
    internal const string RetirementSchemaVersion = "1.1.0";
    internal const string SchemaVersion = "1.2.0";
    internal const string TableName = "elsa_runtime_kernel_schedule_occurrence";

    internal static readonly EfSchemaChain Chain = EfSchemaChain.Of(typeof(RuntimeKernelScheduleOccurrenceEfModule).Assembly, SchemaFamily);
}

[EfSchemaUpcaster("1.0.0", RuntimeKernelScheduleOccurrenceEfModule.RetirementSchemaVersion)]
internal sealed class RuntimeKernelScheduleOccurrenceV1Upcaster : IEfSchemaUpcaster
{
    public RuntimeKernelScheduleOccurrenceV1Upcaster() { }
    public EfSchemaRowContent Upcast(EfSchemaRowContent row) => row;
}

[EfSchemaUpcaster(RuntimeKernelScheduleOccurrenceEfModule.RetirementSchemaVersion, RuntimeKernelScheduleOccurrenceEfModule.SchemaVersion)]
internal sealed class RuntimeKernelScheduleOccurrenceRetirementUpcaster : IEfSchemaUpcaster
{
    public RuntimeKernelScheduleOccurrenceRetirementUpcaster() { }
    public EfSchemaRowContent Upcast(EfSchemaRowContent row) => row;
}
