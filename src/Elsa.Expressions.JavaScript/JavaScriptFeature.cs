using CShells.Features;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.JavaScript.Constants;
using Elsa.Expressions.JavaScript.Core.Models;
using Elsa.Expressions.JavaScript.Core.Options;
using Elsa.Expressions.JavaScript.Options;
using Elsa.Expressions.JavaScript.Services;
using Elsa.Mediator.Core.Extensions;
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
        public IEnumerable<JavaScriptTypeDescriptor> TypeDescriptors { get; set; } = DefaultTypeDescriptors.Get();

        public ConfigurationAccessFunctionProviderOptions? GetConfigurationFunction { get; set; }

        public void ConfigureServices(IServiceCollection services)
        {            
            services
                .Configure<ConfigurationAccessFunctionProviderOptions>(o =>
                {
                    o.AllowConfigurationAccess = GetConfigurationFunction?.AllowConfigurationAccess ?? false;
                    o.DisallowedSections = GetConfigurationFunction?.DisallowedSections ?? [];
                })
                .Configure<JavaScriptOptions>(o =>
                {
                    TypeDescriptors.ToList().ForEach(o.TypeDescriptors.Add);
                })

                .AddDomainEventHandlers(typeof(JavaScriptFeature).Assembly)

                .AddScoped<IExpressionHandler, JavaScriptExpressionHandler>()
                .AddScoped<IExpressionDescriptorProvider, JavaScriptExpressionDescriptorProvider>();
        }
    }
}
