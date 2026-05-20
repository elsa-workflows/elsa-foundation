using CShells.Features;
using Elsa.Expressions.JavaScript.Rendering.Constants;
using Elsa.Expressions.JavaScript.Rendering.Core.Contracts;
using Elsa.Expressions.JavaScript.Rendering.Core.Events;
using Elsa.Expressions.JavaScript.Rendering.Core.Models;
using Elsa.Expressions.JavaScript.Rendering.EventHandlers;
using Elsa.Expressions.JavaScript.Rendering.Services;
using Elsa.Mediator.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Expressions.JavaScript.Rendering
{
    [ShellFeature(
        name: "JavaScriptRendering",
        DisplayName = "JavaScript rendering"
    )]
    public class JavaScriptRenderingFeature : IShellFeature
    {
        public IEnumerable<JavaScriptFunctionDeclaration> FunctionDeclarations { get; set; } = DefaultFunctionDeclarations.Get();

        public IEnumerable<JavaScriptTypeDeclaration> TypeDeclarations { get; set; } = [];

        public IEnumerable<JavaScriptVariableDeclaration> VariableDeclarations { get; set; } = [];

        public void ConfigureServices(IServiceCollection services)
        {
            services                
                .AddDomainEventHandler<OnDeclarationsDocumentGenerating, AddDeclarations>()

                .AddScoped<IJavaScriptDeclarationsDocumentFactory, JavaScriptDeclarationsDocumentFactory>()
                .AddScoped<IJavaScriptDeclarationsDocumentRenderer, JavaScriptTypeDefinitionDocumentRenderer>()
                .AddScoped<IJavaScriptTypeDeclarationFactory, JavaScriptTypeDeclarationFactory>()
                ;
        }
    }
}
