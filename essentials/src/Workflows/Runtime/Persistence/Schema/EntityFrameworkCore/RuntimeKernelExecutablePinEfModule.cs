using Elsa.Persistence.EntityFramework;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

internal static class RuntimeKernelExecutablePinEfModule
{
    internal const string SchemaFamily = "RuntimeKernelExecutablePin";
    internal const string TableName = "RuntimeKernelExecutablePins";
    internal const string SchemaVersion = "1.0.0";

    internal static readonly EfSchemaChain Chain = EfSchemaChain.Of(typeof(RuntimeKernelExecutablePinEfModule).Assembly, SchemaFamily);
}
