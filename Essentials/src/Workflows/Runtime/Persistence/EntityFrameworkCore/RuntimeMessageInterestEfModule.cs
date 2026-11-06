using Elsa.Persistence.EntityFramework;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

internal static class RuntimeMessageInterestEfModule
{
    internal const string SchemaFamily = "RuntimeMessageInterest";
    internal const string SchemaVersion = "1.0.0";
    internal const string RootTable = "elsa_runtime_message_publication_interest";
    internal const string ControlTable = "elsa_runtime_message_interest_scope";
    internal const string ActiveTable = "elsa_runtime_message_active_interest";
    internal const string ActiveOwnerTable = "elsa_runtime_message_active_interest_owner";
    internal static readonly EfSchemaChain Chain = EfSchemaChain.Of(typeof(RuntimeMessageInterestEfModule).Assembly, SchemaFamily);
}
