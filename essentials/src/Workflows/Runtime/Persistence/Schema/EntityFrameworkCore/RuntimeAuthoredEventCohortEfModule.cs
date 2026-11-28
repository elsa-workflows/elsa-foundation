using Elsa.Persistence.EntityFramework;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

internal static class RuntimeAuthoredEventCohortEfModule
{
    internal const string SchemaFamily = "RuntimeAuthoredEventCohort";
    internal const string InitialSchemaVersion = "1.0.0";
    internal const string SchemaVersion = InitialSchemaVersion;

    internal static readonly EfSchemaChain Chain = EfSchemaChain.Of(typeof(RuntimeAuthoredEventCohortEfModule).Assembly, SchemaFamily);
}
