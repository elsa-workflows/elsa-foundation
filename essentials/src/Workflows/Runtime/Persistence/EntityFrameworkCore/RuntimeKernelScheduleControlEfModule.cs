using Elsa.Persistence.EntityFramework;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

internal static class RuntimeKernelScheduleControlEfModule
{
    internal const string SchemaFamily = "RuntimeKernelScheduleControl";
    internal const string SchemaVersion = "1.0.0";
    internal const string TableName = "elsa_runtime_kernel_schedule_control";

    internal static readonly EfSchemaChain Chain = EfSchemaChain.Of(typeof(RuntimeKernelScheduleControlEfModule).Assembly, SchemaFamily);
}
