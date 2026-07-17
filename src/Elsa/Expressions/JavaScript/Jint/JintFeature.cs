using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Jint.Options;
using Elsa.Expressions.JavaScript.Jint.Services;
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
            options.ExecutionTimeout = ExecutionTimeout;
            options.MaxStatements = MaxStatements;
            options.MaxRecursionDepth = MaxRecursionDepth;
        });

        services
            .AddScoped<IPortableJavaScriptEvaluator, JintPortableJavaScriptEvaluator>()
            .AddScoped<IJavaScriptScriptEvaluator, JintJavaScriptScriptEvaluator>();
    }
}
