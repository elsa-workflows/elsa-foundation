using Elsa.Expressions.JavaScript.Rendering.Core.Models;

namespace Elsa.Expressions.JavaScript.Rendering.Core.Contracts
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
