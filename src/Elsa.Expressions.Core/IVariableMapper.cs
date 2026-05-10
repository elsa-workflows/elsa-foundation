namespace Elsa.Expressions.Core
{
    public interface IVariableMapper
    {
        /// <summary>
        /// Maps a <see cref="IVariableDefinition"/> to a <see cref="IVariable"/>.
        /// </summary>
        IVariable Map(IVariableDefinition source);

        /// <summary>
        /// Maps a <see cref="IVariable"/> to a <see cref="IVariableDefinition"/>.
        /// </summary>
        IVariableDefinition Map(IVariable source);
    }
}
