namespace Elsa.Expressions.Core
{
    public interface IVariableDefinition
    {
        /// <summary>
        /// Gets or sets the ID of the variable.
        /// </summary>
        string Id { get; }

        /// <summary>
        /// Gets or sets the name of the variable.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Gets or sets the type name of the variable.
        /// </summary>
        string TypeName { get; }

        /// <summary>
        /// Gets or sets the value of the variable.
        /// </summary>
        string? Value { get; }

        /// <summary>
        /// Gets or sets the storage driver type name of the variable.
        /// </summary>
        string? StorageDriverTypeName { get; }
    }
}
