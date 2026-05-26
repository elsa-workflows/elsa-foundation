namespace Elsa.Expressions.Core.Contracts
{
    public interface IVariable : IMemoryBlockReference
    {
        /// <summary>
        /// The name of the variable.
        /// </summary>
        string Name { get; set; }

        /// <summary>
        /// A default value for the variable.
        /// </summary>
        object? DefaultValue { get; set; }

        /// <summary>
        /// The storage driver type to use for persistence.
        /// If no driver is specified, the referenced memory block will remain in memory for as long as the expression execution context exists.
        /// </summary>
        Type? StorageDriverType { get; set; }

        Type GetVariableType()
        {
            var variableType = GetType();
            return variableType.GenericTypeArguments.Length != 0
                ? variableType.GetGenericArguments().First()
                : typeof(object);
        }
    }
}
