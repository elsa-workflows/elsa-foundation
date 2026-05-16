using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Models;
using Elsa.Http.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Elsa.Http.JavaScript
{
    internal sealed class HttpTypesProvider(IOptions<JavaScriptHttpFeatureOptions> options)
        : IJavaScriptTypeDescriptorProvider, IJavaScriptTypeDeclarationProvider, IJavaScriptVariableDeclarationProvider
    {
        private static readonly IEnumerable<JavaScriptTypeDescriptor> defaultTypes =
        [
            new(typeof(HttpRouteData)),
            new(typeof(HttpRequest)),
            new(typeof(HttpResponse)),
            new(typeof(HttpResponseMessage)),
            new(typeof(HttpHeaders)),
            new(typeof(IFormFile)),
            new(typeof(HttpFile)),
            new(typeof(Downloadable)),
            new (typeof(IFormFile[]))
            { 
                Alias = $"{nameof(IFormFile)}[]",
                RegisterTypeDeclaration = false 
            },
            new (typeof(HttpFile[]))
            { 
                Alias = $"{nameof(HttpFile)}[]", 
                RegisterTypeDeclaration = false 
            },
            new (typeof(Downloadable[]))
            { 
                Alias = $"{nameof(Downloadable)}[]", 
                RegisterTypeDeclaration = false 
            }
        ];

        public IEnumerable<JavaScriptTypeDescriptor> GetDescriptors()
        {
            var additionalTypes = options.Value.TypeDescriptors ?? [];
            return defaultTypes.Concat(additionalTypes);
        }
        ValueTask<IEnumerable<JavaScriptTypeDeclaration>> IJavaScriptTypeDeclarationProvider.GetDeclarations(CancellationToken cancellationToken)
        {
            return new(options.Value.TypeDeclarations);
        }


        ValueTask<IEnumerable<JavaScriptVariableDeclaration>> IJavaScriptVariableDeclarationProvider.GetDeclarations(CancellationToken cancellationToken)
        {
            return new(options.Value.VariableDeclarations);
        }
    }
}
