using CShells.Features;
using Elsa.Expressions.JavaScript.Core.Models;
using Elsa.Expressions.JavaScript.Rendering.Constants;
using Elsa.Expressions.JavaScript.Rendering.Core.Events;
using Elsa.Expressions.JavaScript.Rendering.EventHandlers;
using Elsa.Mediator.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Expressions.JavaScript.Rendering
{
    public class JavaScriptRenderingFeature : IShellFeature
    {
        public IEnumerable<JavaScriptFunctionDeclaration> FunctionDeclarations { get; set; } = DefaultFunctionDeclarations.Get();

        public IEnumerable<JavaScriptTypeDeclaration> TypeDeclarations { get; set; } = [];

        public IEnumerable<JavaScriptVariableDeclaration> VariableDeclarations { get; set; } = [];

        public void ConfigureServices(IServiceCollection services)
        {
            services
                .AddScoped<IDomainEventHandler<OnDeclarationsDocumentGenerating>, AddDeclarations>();
        }
    }
}
