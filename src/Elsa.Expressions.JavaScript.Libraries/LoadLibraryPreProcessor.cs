using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;

namespace Elsa.Expressions.JavaScript.Libraries
{
    internal sealed class LoadLibraryPreProcessor(IOptions<JavaScriptLibrariesFeatureOptions> options) : IJavaScriptEvaluationPreProcessor
    {
        public ValueTask Process(IJavaScriptExecutionContext javascriptExecutionContext, IExpressionExecutionContext expressionExecutionContext, string expression)
        {
            var resourceName = options.Value.FullModuleResourceName;
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)!;
            using var reader = new StreamReader(stream);
            var script = reader.ReadToEnd();
            javascriptExecutionContext.Execute(script);

            return ValueTask.CompletedTask;
        }
    }
}
