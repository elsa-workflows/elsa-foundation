using CShells.Features;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Jint.Contracts;
using Elsa.Expressions.JavaScript.Jint.Options;
using Elsa.Expressions.JavaScript.Jint.Services;
using CShells.Lifecycle;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
    /// <summary>Requested evaluation-thread stack size, 16–64 MiB. Default 16 MiB.</summary>
    [ManifestIgnore] // Host configuration only; excluded from the generated Studio settings surface.
    public int EvaluationThreadStackBytes { get; set; } = 16 * 1024 * 1024;

    /// <summary>Evaluation stack budget, 64–512 KiB. Default 512 KiB.</summary>
    [ManifestIgnore]
    public long EvaluationStackBudgetBytes { get; set; } = 512 * 1024;

    /// <summary>
    /// Sandbox limit: wall-clock execution timeout for a single script evaluation. Empty disables it.
    /// </summary>
    [ManifestSetting(DisplayName = "Execution timeout", Description = "Maximum wall-clock time a single script evaluation may run before it is aborted; empty disables the timeout. Guards against infinite loops.", Category = "Security", UIHint = "duration")]
    public TimeSpan? ExecutionTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Sandbox limit: maximum statements a single evaluation may execute. Empty/zero disables it.
    /// </summary>
    [ManifestSetting(DisplayName = "Max statements", Description = "Maximum number of statements a single script evaluation may execute before it is aborted; empty or zero disables the limit.", Category = "Security", Advanced = true)]
    public int? MaxStatements { get; set; } = 10_000_000;

    /// <summary>
    /// Sandbox limit: maximum call-stack recursion depth for a single evaluation. Empty/zero disables it.
    /// </summary>
    [ManifestSetting(DisplayName = "Max recursion depth", Description = "Maximum call-stack recursion depth for a single script evaluation; empty or zero disables the limit.", Category = "Security", Advanced = true)]
    public int? MaxRecursionDepth { get; set; } = 300;

    /// <summary>
    /// Sandbox limit: how many bytes one evaluation may allocate. Low 256 MiB, Medium 512 MiB (default), Maximum 1 GiB.
    /// </summary>
    [ManifestSetting(DisplayName = "Memory profile", Description = "How many bytes one script evaluation may allocate, garbage included: Low 256 MiB, Medium 512 MiB, Maximum 1 GiB. The limit cannot be switched off.", Category = "Security", Advanced = true)]
    public JavaScriptMemoryProfile MaxMemoryProfile { get; set; } = JavaScriptMemoryProfile.Medium;

    [ManifestSetting(DisplayName = "Max array length", Description = "Maximum JavaScript array length, including sparse arrays; empty or zero disables the limit.", Category = "Security", Advanced = true)]
    public uint? MaxArrayLength { get; set; } = 100_000;

    [ManifestSetting(DisplayName = "Max input bytes", Description = "Maximum aggregate UTF-8 size of declared JSON parameter values before JavaScript evaluation.", Category = "Security", Advanced = true)]
    public int MaxInputBytes { get; set; } = 1024 * 1024;

    [ManifestSetting(DisplayName = "Max input depth", Description = "Maximum nesting depth of a declared JSON parameter value before JavaScript evaluation.", Category = "Security", Advanced = true)]
    public int MaxInputDepth { get; set; } = 64;

    [ManifestSetting(DisplayName = "Max input nodes", Description = "Maximum aggregate number of JSON values across declared parameters before JavaScript evaluation.", Category = "Security", Advanced = true)]
    public int MaxInputNodes { get; set; } = 100_000;

    [ManifestSetting(DisplayName = "Max result bytes", Description = "Maximum UTF-8 size of a JavaScript result after canonical JSON conversion.", Category = "Security", Advanced = true)]
    public int MaxResultBytes { get; set; } = 1024 * 1024;

    [ManifestSetting(DisplayName = "Max result depth", Description = "Maximum nesting depth of a JavaScript result after canonical JSON conversion.", Category = "Security", Advanced = true)]
    public int MaxResultDepth { get; set; } = 64;

    [ManifestSetting(DisplayName = "Max result nodes", Description = "Maximum number of JSON values in a JavaScript result after canonical JSON conversion.", Category = "Security", Advanced = true)]
    public int MaxResultNodes { get; set; } = 100_000;

    public void ConfigureServices(IServiceCollection services)
    {
        services.Configure<FeatureOptions>(options =>
        {
            options.EvaluationThreadStackBytes = EvaluationThreadStackBytes;
            options.EvaluationStackBudgetBytes = EvaluationStackBudgetBytes;
            options.ExecutionTimeout = ExecutionTimeout;
            options.MaxStatements = MaxStatements;
            options.MaxRecursionDepth = MaxRecursionDepth;
            options.MaxMemoryProfile = MaxMemoryProfile;
            options.MaxArrayLength = MaxArrayLength;
            options.MaxInputBytes = MaxInputBytes;
            options.MaxInputDepth = MaxInputDepth;
            options.MaxInputNodes = MaxInputNodes;
            options.MaxResultBytes = MaxResultBytes;
            options.MaxResultDepth = MaxResultDepth;
            options.MaxResultNodes = MaxResultNodes;
        });
        services.AddOptions<FeatureOptions>()
            .Validate(JintEvaluationThreads.ValidSettings,
                "Jint evaluation thread stack must be 16–64 MiB and evaluation stack budget must be 64–512 KiB.")
            .Validate(options => JavaScriptMemoryBudget.IsDefined(options.MaxMemoryProfile),
                "The JavaScript memory profile must be Low, Medium or Maximum.")
            .ValidateOnStart();

        // The built-in functions are part of every JavaScript surface (spec 1223 FR-003, FR-006, FR-007, FR-026); host
        // functions are registered beside them. The naming rule is checked at shell activation (FR-027).
        foreach (var builtIn in JavaScriptBuiltIns.Declarations)
            services.AddSingleton(builtIn);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<JavaScriptHostFunctions>();
        services.AddSingleton<IShellInitializer, JavaScriptHostFunctionCheck>();

        services
            .AddScoped<IPortableJavaScriptEvaluator, JintPortableJavaScriptEvaluator>()
            .AddScoped<IJavaScriptScriptEvaluator, JintJavaScriptScriptEvaluator>();

        // Replacement contracts (spec 1228 FR-009, FR-012): one of each is active per host. A host feature replaces the
        // default with services.Replace(...); there is no per-workflow or per-node override, and a second registration
        // fails shell activation. Both are singletons on purpose: they hold no state and no scoped dependency; the
        // per-evaluation state lives in the IScriptValueEvaluation each evaluation begins (framework §2.5.1).
        services.TryAddSingleton<IScriptValueBridge, CopyComparePatchValueBridge>();
        services.TryAddSingleton<IJavaScriptPrecisionPolicy, RefusingPrecisionPolicy>();
        JavaScriptValueCompositionValidator.Register(services);
    }
}
