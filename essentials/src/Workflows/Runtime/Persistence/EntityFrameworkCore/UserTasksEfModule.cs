using Elsa.Persistence.EntityFramework;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

internal static class UserTasksEfModule
{
    public const string SchemaFamily = "UserTasks";
    public const string SchemaVersion = "1.0.0";
    public static readonly EfSchemaChain Chain = EfSchemaChain.Of(typeof(UserTasksEfModule).Assembly, SchemaFamily);
}
