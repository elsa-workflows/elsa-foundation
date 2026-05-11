using Elsa.Primitives.Entities;

namespace Elsa.Persistence.EFCore.Options
{
    public sealed class CommandsOptions
    {
        internal static readonly IEnumerable<CommandType> AllCommands = Enum.GetValues<CommandType>();

        public IEnumerable<EntityCommandsOptions> Entities { get; set; } = [];

        public IEnumerable<CommandType>? DefaultSupportedCommands { get; set; }

        public bool IsCommandSupported(Type entityType, CommandType commandType) => GetSupportedCommands(entityType).Contains(commandType);

        public bool IsCommandSupported<TEntity>(CommandType commandType) => GetSupportedCommands(typeof(TEntity)).Contains(commandType);

        public bool IsAnyCommandSupported<TEntity>(IEnumerable<CommandType> commandTypes) => GetSupportedCommands(typeof(TEntity)).Any(commandTypes.Contains);


        public IEnumerable<CommandType> GetSupportedCommands(Type entityType)
        {
            var entityOptions = Entities?.FirstOrDefault(x => x.EntityType == entityType.FullName || x.EntityType == entityType.Name);
            if(entityOptions is null)
            {
                return DefaultSupportedCommands ?? AllCommands;
            }
            
            return entityOptions.SupportedCommands ?? DefaultSupportedCommands ?? CommandsOptions.AllCommands;
        }
    }

    public sealed class EntityCommandsOptions
    {

        public string EntityType { get; set; } = string.Empty;

        public IEnumerable<CommandType>? SupportedCommands { get; set; }
    }

    public enum CommandType
    {
        Add,
        Save,
        Delete,
        Update,
        BulkInsert,
        BulkUpsert
    }
}
