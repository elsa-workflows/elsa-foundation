using CShells.Features;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Jint.Configurators;
using Elsa.Expressions.JavaScript.Jint.Contracts;
using Elsa.Expressions.JavaScript.Jint.Converters;
using Elsa.Expressions.JavaScript.Jint.Services;
using Jint.Runtime.Interop;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Expressions.JavaScript.Jint
{
    [ShellFeature(
        "Jint", 
        DisplayName = "Jint", 
        Description = "Provides JavaScript evaluation using the Jint engine."
    )]
    public class JintFeature : IShellFeature
    {
        public JintFeatureOptions Options { get; set; } = new();

        public void ConfigureServices(IServiceCollection services)
        {
            services.Configure<JintFeatureOptions>(options =>
            {
                options.AllowClrAccess = Options.AllowClrAccess;
                options.AllowConfigurationAccess = Options.AllowConfigurationAccess;
                options.ScriptCacheTimeout = Options.ScriptCacheTimeout;                
            });

            // JavaScript services.
            services
                // Configurators
                .AddScoped<IJintEngineOptionsConfigurator, ClrAccessEngineConfigurator>()
                .AddScoped<IJintEngineOptionsConfigurator, ObjectConvertersEngineConfigurator>()
                .AddScoped<IJintEngineOptionsConfigurator, ObjectWrapperEngineConfigurator>()

                // JINT object converters
                .AddScoped<IObjectConverter, ByteArrayConverter>()
                .AddScoped<IObjectConverter, EnumToStringConverter>()
                .AddScoped<IObjectConverter, JsonElementConverter>()

                // JINT Evaluation / Execution
                .AddScoped<IJavaScriptEvaluator, JintJavaScriptEvaluator>()
                .AddScoped<IJavaScriptExecutionContextFactory, JintExecutionContextFactory>()
                .AddScoped<IPreparedScriptFactory, PreparedScriptFactory>()
                ;             

            // Type definition services.
            //services            

            // Handlers.
            //services.AddNotificationHandlersFrom<JavaScriptFeature>();

            // Type Script definitions.
            //services.AddFunctionDefinitionProvider<InputFunctionsDefinitionProvider>();

            // UI property handlers.
            //services.AddScoped<IPropertyUIHandler, RunJavaScriptOptionsProvider>();

            // Hosted services.
            //services.AddHostedService<RegisterVariableTypesWithJavaScriptHostedService>();
        }
    }
}
