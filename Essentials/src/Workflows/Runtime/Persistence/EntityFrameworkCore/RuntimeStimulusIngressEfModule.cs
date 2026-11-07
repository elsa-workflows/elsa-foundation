using Elsa.Persistence.EntityFramework;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

internal static class RuntimeStimulusIngressEfModule
{
    public const string SchemaFamily = "RuntimeStimulusIngress";
    public const string SchemaVersion = "1.0.0";
    public const string SourceTableName = "elsa_runtime_stimulus_ingress";
    public static readonly EfSchemaChain Chain = EfSchemaChain.Of(typeof(RuntimeStimulusIngressEfModule).Assembly, SchemaFamily);
}
