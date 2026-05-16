using CShells.Features;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Models;
using Elsa.Primitives.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Http.JavaScript
{
    [ShellFeature(
        name: "JavaScriptHttp",
        DisplayName = "JavaScript HTTP"
    )]
    public class JavaScriptHttpFeature : IShellFeature
    {
        public IEnumerable<JavaScriptVariableDeclaration> VariableDeclarations { get; set; } = [];

        public IEnumerable<JavaScriptTypeDescriptor> TypeDescriptors { get; set; } = [];

        public IEnumerable<JavaScriptTypeDeclaration> TypeDeclarations { get; set; } = [];

        public void ConfigureServices(IServiceCollection services)
        {
            services
                .AddAsInterfaces<HttpTypesProvider>()
                .Configure<JavaScriptHttpFeatureOptions>(o =>
                {
                    o.VariableDeclarations = VariableDeclarations;
                    o.TypeDeclarations = TypeDeclarations;
                    o.TypeDescriptors = TypeDescriptors;
                })
                .AddScoped<IJavaScriptTypeDescriptorProvider, HttpTypesProvider>();
        }
    }
}
