using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Libraries.Options;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Reflection;

namespace Elsa.Expressions.JavaScript.Libraries.PreProcessors;

public sealed class LibraryResourcePreProcessor(IOptions<JavaScriptLibrariesFeatureOptions> options) : IScriptPreProcessor
{
    // Embedded library resources never change for the process lifetime, so decode each once and reuse
    // the string across evaluations instead of re-reading + re-decoding on every JS expression (#422 item 1).
    // Keyed by resource name because the feature is registered once per library (lodash, moment, ...).
    private static readonly ConcurrentDictionary<string, string> DecodedResources = new();

    public ValueTask PreProcess(string script, IJavaScriptExecutionContext executionContext, IExpressionExecutionContext expressionContext, IExpressionEvaluatorOptions? evaluatorOptions, CancellationToken cancellationToken)
    {
        var libraryScript = DecodedResources.GetOrAdd(options.Value.FullModuleResourceName, DecodeResource);
        executionContext.Execute(libraryScript);

        return ValueTask.CompletedTask;
    }

    private static string DecodeResource(string resourceName)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}