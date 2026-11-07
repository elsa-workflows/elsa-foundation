using Elsa.Persistence.EntityFramework;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

/// <summary>New-family canonical headers and shared retained work; existing canonical sources stay with their owners.</summary>
/// <remarks>Physical schema admission does not recognize a source family or enable its checked provider adapter.</remarks>
internal static class RuntimeDeliverySourceEfModule
{
    internal const string SchemaFamily = "RuntimeDeliverySources";
    internal const string SchemaVersion = "1.0.0";
    internal const string SourceTableName = "elsa_runtime_ingress_source";
    internal const string WorkTableName = "elsa_runtime_delivery_work";
    internal static readonly EfSchemaChain Chain = EfSchemaChain.Of(typeof(RuntimeDeliverySourceEfModule).Assembly, SchemaFamily);
}
