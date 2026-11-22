using Elsa.Persistence.EntityFramework;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

internal static class StandaloneTriggersEfModule
{
    public const string SchemaFamily = "StandaloneTriggers";
    public const string SchemaVersion = "1.0.0";
    public const string SemanticReservationsSchemaVersion = "1.1.0";
    public static readonly EfSchemaChain Chain = EfSchemaChain.Of(typeof(StandaloneTriggersEfModule).Assembly, SchemaFamily);
}
