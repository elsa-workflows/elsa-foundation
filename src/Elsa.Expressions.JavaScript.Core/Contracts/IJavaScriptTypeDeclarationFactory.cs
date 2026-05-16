using Elsa.Expressions.JavaScript.Core.Models;

namespace Elsa.Expressions.JavaScript.Core.Contracts
{
    /// <summary>
    /// Returns a <see cref="JavaScriptTypeDeclaration"/> from a given <see cref="Type"/>.
    /// </summary>
    public interface IJavaScriptTypeDeclarationFactory
    {
        /// <summary>
        /// Returns a <see cref="JavaScriptTypeDeclaration"/> from a given <see cref="Type"/>.
        /// </summary>
        JavaScriptTypeDeclaration Create(Type type);
    }
}
