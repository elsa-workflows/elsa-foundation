using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Models;
using System.Runtime.CompilerServices;

namespace Elsa.Expressions.JavaScript.Services
{
    internal sealed class JavaScriptDeclarationsDocumentFactory(
        IEnumerable<IJavaScriptTypeDeclarationProvider> typeDeclarationProviders,
        IEnumerable<IJavaScriptFunctionDeclarationProvider> functionDefinitionProviders,
        IEnumerable<IJavaScriptVariableDeclarationProvider> variableDefinitionProviders
    )
        : IJavaScriptDeclarationsDocumentFactory
    {
        public async ValueTask<JavaScriptDeclarationsDocument> Create(CancellationToken cancellationToken = default)
        {
            var typeDefinitions = await GetTypeDeclarations(cancellationToken)
                .ToListAsync(cancellationToken);
            var functionDefinitions = await GetFunctionDeclarations(cancellationToken)
                .ToListAsync(cancellationToken);
            var variableDefinitions = await GetVariableDeclarations(cancellationToken)
                .ToListAsync(cancellationToken);

            return new JavaScriptDeclarationsDocument
            {
                Variables = variableDefinitions,
                Functions = functionDefinitions,
                Types = typeDefinitions
            };
        }

        private async IAsyncEnumerable<JavaScriptFunctionDeclaration> GetFunctionDeclarations([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var provider in functionDefinitionProviders)
            {
                var definitions = await provider.GetDeclarations(cancellationToken);

                foreach (var definition in definitions)
                    yield return definition;
            }
        }

        private async IAsyncEnumerable<JavaScriptTypeDeclaration> GetTypeDeclarations([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var provider in typeDeclarationProviders)
            {
                var definitions = await provider.GetDeclarations(cancellationToken);

                foreach (var definition in definitions)
                    yield return definition;
            }          
        }

        private async IAsyncEnumerable<JavaScriptVariableDeclaration> GetVariableDeclarations([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var provider in variableDefinitionProviders)
            {
                var definitions = await provider.GetDeclarations(cancellationToken);

                foreach (var definition in definitions)
                    yield return definition;
            }
        }
    }
}
