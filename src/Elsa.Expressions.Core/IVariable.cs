namespace Elsa.Expressions.Core
{
    public interface IVariable
    {
        /// <summary>
        /// The identifier of the variable.
        /// </summary>
        string Id { get; set; }

        /// <summary>
        /// The name of the variable.
        /// </summary>
        string Name { get; set; }

        /// <summary>
        /// A default value for the variable.
        /// </summary>
        object? Value { get; set; }

        /// <summary>
        /// The storage driver type to use for persistence.
        /// If no driver is specified, the referenced memory block will remain in memory for as long as the expression execution context exists.
        /// </summary>
        Type? StorageDriverType { get; set; }
    }
}
