using CShells.Features;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.JavaScript.Core;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Models;
using Elsa.Expressions.JavaScript.Providers;
using Elsa.Expressions.JavaScript.Services;
using Elsa.Primitives.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Expressions.JavaScript
{
    [ShellFeature(
        name: "JavaScriptExpressions",
        DisplayName = "JavaScript Expressions",
        Description = "Provides functions to register and configure JavaScript Expressions"
    )]
    public class JavaScriptFeature : IShellFeature
    {
        public IEnumerable<JavaScriptVariableDeclaration> VariableDeclarations { get; set; } = [];

        public IEnumerable<JavaScriptTypeDescriptor> TypeDescriptors { get; set; } = [];

        public IEnumerable<JavaScriptTypeDeclaration> TypeDeclarations { get; set; } = [];

        public void ConfigureServices(IServiceCollection services)
        {
            services
                .AddScoped<IJavaScriptTypeDeclarationProvider>(sp=> new JavaScriptTypeDeclarationProvider(TypeDeclarations))
                .AddScoped<IJavaScriptVariableDeclarationProvider>(sp => new JavaScriptVariableDeclarationProvider(VariableDeclarations))
                .AddScoped<IJavaScriptTypeDescriptorProvider>(sp => new JavaScriptTypeDescriptorProvider(TypeDescriptors))

                .AddScoped<IExpressionHandler, JavaScriptExpressionHandler>()

                .AddScoped<IJavaScriptDeclarationsDocumentFactory, JavaScriptDeclarationsDocumentFactory>()
                .AddScoped<IJavaScriptTypeDeclarationFactory, JavaScriptTypeDeclarationFactory>()
                .AddScoped<IJavaScriptDeclarationsDocumentRenderer, JavaScriptTypeDefinitionDocumentRenderer>()
                .AddScoped<IJavaScriptTypeAliasRegistry, JavaScriptTypeAliasRegistry>()

                
                // Providers
                .AddScoped<IExpressionDescriptorProvider, JavaScriptExpressionDescriptorProvider>()
                .AddScoped<IJavaScriptObjectProvider, ArgsObjectProvider>()
                .AddAsInterfaces<CommonFunctionsProvider>()
                .AddAsInterfaces<ConfigurationAccessFunctionProvider>()
                .AddScoped<IJavaScriptFunctionProvider, GetArgumentFunctionsProvider>()
                .AddScoped<IJavaScriptTypeDeclarationProvider, TypeDescriptorsTypeDeclarationsProvider>()
                ;
        }
    }
}
