using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Jint.Options;

namespace Elsa.Expressions.JavaScript.Jint.Services;

/// <summary>
/// The isolated Jint script evaluator for a host that composes it outside a shell, such as a kernel host's RunJavaScript
/// engine: the same engine, limits and closed value world the shell's <see cref="IJavaScriptScriptEvaluator"/> uses.
/// </summary>
public static class JintScriptEvaluators
{
    /// <remarks>
    /// It takes the shell's defaults for the replacement contracts and the built-in functions only: the copy-compare-patch
    /// value bridge, the refusing precision policy (spec 1228), and no host functions beside the built-ins (spec 1223).
    /// </remarks>
    public static IJavaScriptScriptEvaluator Create(FeatureOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        // Outside a shell no options validation runs: an unknown profile (a number bound from configuration) fails here,
        // when the host composes the evaluator, not on every script it later runs.
        if (!MemoryBudget.IsDefined(options.MaxMemoryProfile))
            throw new ArgumentException("The JavaScript memory profile must be Low, Medium or Maximum.", nameof(options));
        var featureOptions = Microsoft.Extensions.Options.Options.Create(options);
        return new JintJavaScriptScriptEvaluator(
            featureOptions,
            new CopyComparePatchValueBridge(featureOptions),
            new RefusingPrecisionPolicy(),
            new JavaScriptHostFunctions(JavaScriptBuiltIns.Declarations));
    }
}
