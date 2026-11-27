using Elsa.Persistence.EntityFramework;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

internal static class RuntimeKernelEventPublicationEfModule
{
    internal const string SchemaFamily = "RuntimeKernelEventPublication";
    internal const string SchemaVersion = "1.0.0";
    internal const string TableName = "elsa_runtime_kernel_event_publication";

    internal static readonly EfSchemaChain Chain = EfSchemaChain.Of(typeof(RuntimeKernelEventPublicationEfModule).Assembly, SchemaFamily);
}
