using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Jint.Options;

namespace Elsa.Expressions.JavaScript.Jint.Services;

/// <summary>
/// The isolated Jint script evaluator for a host that composes it outside a shell, such as a kernel host's RunJavaScript
/// engine: the same engine, limits and closed value world the shell's <see cref="IJavaScriptScriptEvaluator"/> uses.
/// </summary>
public static class JintScriptEvaluators
{
    public static IJavaScriptScriptEvaluator Create(FeatureOptions options) => new JintJavaScriptScriptEvaluator(Microsoft.Extensions.Options.Options.Create(options));
}
