using CShells.Features;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Jint.Configurators;
using Elsa.Expressions.JavaScript.Jint.Contracts;
using Elsa.Expressions.JavaScript.Jint.Converters;
using Elsa.Expressions.JavaScript.Jint.Options;
using Elsa.Expressions.JavaScript.Jint.Services;
using Jint.Runtime.Interop;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Expressions.JavaScript.Jint;

[ShellFeature(
    "JavaScriptJintEngine",
    DisplayName = "JavaScript Jint engine",
    Description = "Provides JavaScript evaluation using the Jint engine."
)]
public class JintFeature : IShellFeature
{
    /// <summary>
    /// Enables access to any .NET class. Do not enable if you are executing workflows from untrusted sources (e.g. user defined workflows).        
    /// See Jint docs for more: https://github.com/sebastienros/jint#accessing-net-assemblies-and-classes
    /// </summary>
    public bool AllowClrAccess { get; set; }

    /// <summary>
    /// The timeout for script caching.
    /// </summary>
    /// <remarks>
    /// The <c>ScriptCacheTimeout</c> property specifies the duration for which the scripts are cached in the Jint JavaScript engine. When a script is executed, it is compiled and cached for future use. This caching improves performance by avoiding repetitive compilation of the same script.
    /// If the value of <c>ScriptCacheTimeout</c> is <c>null</c>, the scripts are cached indefinitely. If a time value is specified, the scripts will be purged from the cache after they've been unused for the specified duration and recompiled on next use.
    /// </remarks>
    public TimeSpan? ScriptCacheTimeout { get; set; } = TimeSpan.FromDays(1);

    public void ConfigureServices(IServiceCollection services)
    {
        services.Configure<FeatureOptions>(options =>
        {
            options.AllowClrAccess = AllowClrAccess;
            options.ScriptCacheTimeout = ScriptCacheTimeout;
        });

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
            .AddScoped<IPreparedScriptFactory, PreparedScriptFactory>()
            .AddScoped<IJintEngineFactory, JintEngineFactory>()
            ;
    }
}