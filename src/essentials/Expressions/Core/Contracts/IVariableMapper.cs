using Elsa.Expressions.Core.Models;

namespace Elsa.Expressions.Core.Contracts;

public interface IVariableMapper
{
    /// <summary>
    /// Maps a <see cref="VariableDefinition"/> to a <see cref="IVariable"/>.
    /// </summary>
    IVariable Map(VariableDefinition source);

    /// <summary>
    /// Maps a <see cref="IVariable"/> to a <see cref="VariableDefinition"/>.
    /// </summary>
    VariableDefinition Map(IVariable source);
}