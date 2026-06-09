using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Libraries.Options;
using Microsoft.Extensions.Options;
using System.Reflection;

namespace Elsa.Expressions.JavaScript.Libraries.PreProcessors;

internal sealed class LibraryResourcePreProcessor(IOptions<JavaScriptLibrariesFeatureOptions> options) : IScriptPreProcessor
{
    public ValueTask PreProcess(string script, IJavaScriptExecutionContext executionContext, IExpressionExecutionContext expressionContext, IExpressionEvaluatorOptions? evaluatorOptions, CancellationToken cancellationToken)
    {
        var resourceName = options.Value.FullModuleResourceName;
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)!;

        using var reader = new StreamReader(stream);
        var libraryScript = reader.ReadToEnd();
        executionContext.Execute(libraryScript);

        return ValueTask.CompletedTask;
    }
}