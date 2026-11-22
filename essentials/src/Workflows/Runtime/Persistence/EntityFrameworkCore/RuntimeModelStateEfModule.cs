using Elsa.Persistence.EntityFramework;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

internal static class RuntimeModelStateEfModule
{
    public const string StateTable = "elsa_runtime_model_execution";
    public const string InputTable = "elsa_runtime_model_input";
    public const string SchemaFamily = "RuntimeModelState";
    public const string SchemaVersion = "1.0.0";
    public const string IngressSchemaVersion = "1.1.0";
    public static readonly EfSchemaChain Chain = EfSchemaChain.Of(typeof(RuntimeModelStateEfModule).Assembly, SchemaFamily);
}
