using Elsa.Expressions.Core;
using Elsa.Expressions.Models;

namespace Elsa.Expressions.Services
{
    /// <inheritdoc />
    public sealed class VariableFactory : IVariableFactory
    {
        /// <inheritdoc />
        public IVariable Create(string name, object? value = null, string? id = null)
        {
            return new Variable(name, value, id);
        }

        /// <inheritdoc />
        public IVariable<T> Create<T>(string name, T value, string? id = null)
            where T : notnull
        {
            return new Variable<T>(name, value, id);
        }
    }
}
