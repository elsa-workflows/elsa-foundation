using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Jint.Configurators;
using Elsa.Expressions.JavaScript.Jint.Contracts;
using Elsa.Expressions.JavaScript.Jint.Converters;
using Elsa.Expressions.JavaScript.Jint.Options;
using Elsa.Expressions.JavaScript.Jint.Services;
using Jint.Runtime.Interop;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Expressions.JavaScript.Jint;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Expressions")]
[ManifestFeatureCategory("JavaScript")]
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
    [ManifestSetting(DisplayName = "Allow CLR access", Description = "Allows scripts to access .NET classes. Enable only for trusted workflows.", Category = "Security", RestartRequired = true, Advanced = true)]
    public bool AllowClrAccess { get; set; }

    /// <summary>
    /// The timeout for script caching.
    /// </summary>
    /// <remarks>
    /// The <c>ScriptCacheTimeout</c> property specifies the duration for which the scripts are cached in the Jint JavaScript engine. When a script is executed, it is compiled and cached for future use. This caching improves performance by avoiding repetitive compilation of the same script.
    /// If the value of <c>ScriptCacheTimeout</c> is <c>null</c>, the scripts are cached indefinitely. If a time value is specified, the scripts will be purged from the cache after they've been unused for the specified duration and recompiled on next use.
    /// </remarks>
    [ManifestSetting(DisplayName = "Script cache timeout", Description = "Duration that compiled scripts remain cached; empty keeps scripts cached indefinitely.", Category = "Caching", UIHint = "duration")]
    public TimeSpan? ScriptCacheTimeout { get; set; } = TimeSpan.FromDays(1);

    /// <summary>
    /// Sandbox limit (DS-9): wall-clock execution timeout for a single script evaluation. Empty disables it.
    /// </summary>
    [ManifestSetting(DisplayName = "Execution timeout", Description = "Maximum wall-clock time a single script evaluation may run before it is aborted; empty disables the timeout. Guards against infinite loops.", Category = "Security", UIHint = "duration")]
    public TimeSpan? ExecutionTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Sandbox limit (DS-9): maximum statements a single evaluation may execute. Empty/zero disables it.
    /// </summary>
    [ManifestSetting(DisplayName = "Max statements", Description = "Maximum number of statements a single script evaluation may execute before it is aborted; empty or zero disables the limit.", Category = "Security", Advanced = true)]
    public int? MaxStatements { get; set; } = 10_000_000;

    /// <summary>
    /// Sandbox limit (DS-9): maximum call-stack recursion depth for a single evaluation. Empty/zero disables it.
    /// </summary>
    [ManifestSetting(DisplayName = "Max recursion depth", Description = "Maximum call-stack recursion depth for a single script evaluation; empty or zero disables the limit.", Category = "Security", Advanced = true)]
    public int? MaxRecursionDepth { get; set; } = 300;

    public void ConfigureServices(IServiceCollection services)
    {
        services.Configure<FeatureOptions>(options =>
        {
            options.AllowClrAccess = AllowClrAccess;
            options.ScriptCacheTimeout = ScriptCacheTimeout;
            options.ExecutionTimeout = ExecutionTimeout;
            options.MaxStatements = MaxStatements;
            options.MaxRecursionDepth = MaxRecursionDepth;
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