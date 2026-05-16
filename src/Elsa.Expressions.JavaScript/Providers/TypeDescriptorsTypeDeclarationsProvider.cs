using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Models;

namespace Elsa.Expressions.JavaScript.Providers
{
    internal sealed class TypeDescriptorsTypeDeclarationsProvider(IEnumerable<IJavaScriptTypeDescriptorProvider> providers, IJavaScriptTypeDeclarationFactory factory) : IJavaScriptTypeDeclarationProvider
    {
        public ValueTask<IEnumerable<JavaScriptTypeDeclaration>> GetDeclarations(CancellationToken cancellationToken = default)
        {
            var declarations = providers
                .SelectMany(x => x.GetDescriptors())
                .Where(x => x.RegisterTypeDeclaration)
                .Select(x => factory.Create(x.GetDescriptorType()));

            return new(declarations);
        }
    }
}
