using Elsa.Persistence.EntityFramework;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

internal static class RuntimeKernelScheduleControlEfModule
{
    internal const string SchemaFamily = "RuntimeKernelScheduleControl";
    internal const string InitialSchemaVersion = "1.0.0";
    internal const string SchemaVersion = "1.1.0";
    internal const string TableName = "elsa_runtime_kernel_schedule_control";

    internal static readonly EfSchemaChain Chain = EfSchemaChain.Of(typeof(RuntimeKernelScheduleControlEfModule).Assembly, SchemaFamily);
}

[EfSchemaUpcaster(RuntimeKernelScheduleControlEfModule.InitialSchemaVersion, RuntimeKernelScheduleControlEfModule.SchemaVersion)]
internal sealed class RuntimeKernelScheduleControlV1Upcaster : IEfSchemaUpcaster
{
    public RuntimeKernelScheduleControlV1Upcaster() { }
    public EfSchemaRowContent Upcast(EfSchemaRowContent row) => row;
}
