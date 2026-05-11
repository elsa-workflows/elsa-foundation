namespace Elsa.Expressions.Core
{
    /// <summary>
    /// Creates instances of type <see cref="IVariable"/> and <see cref="IVariable{T}"/>.
    /// </summary>
    public interface IVariableFactory
    {
        /// <summary>
        /// Creates a new variable with the specified name, value and id. The type of the variable is determined by the type of the value parameter. If the value parameter is null, the variable will be created with a type of object.
        /// </summary>
        /// <param name="name"></param>
        /// <param name="value"></param>
        /// <param name="id"></param>
        /// <returns></returns>
        IVariable Create(string name, object? value = null, string? id = null);

        /// <summary>
        /// Creates a new variable with the specified name, value and id. The type of the variable is determined by the type parameter T. If the value parameter is null, the variable will be created with a default value of type T.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="name"></param>
        /// <param name="value"></param>
        /// <param name="id"></param>
        /// <returns></returns>
        IVariable<T> Create<T>(string name, T value, string? id = null)
            where T : notnull;
    }
}
