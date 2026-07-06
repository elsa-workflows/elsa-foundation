using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Libraries.Options;
using Elsa.Expressions.JavaScript.Libraries.PreProcessors;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Expressions.Tests.Unit;

/// <summary>
/// #422 (item 1): <see cref="LibraryResourcePreProcessor"/> re-read and re-decoded the embedded JS
/// library resource from the assembly manifest on every single evaluation, even though it never
/// changes for the process lifetime. It must decode each resource once and reuse the cached string.
/// </summary>
public sealed class LibraryResourcePreProcessorTests
{
    // A real embedded resource shipped by Elsa.Expressions.JavaScript.Libraries (see the .csproj
    // EmbeddedResource glob over ClientLib/dist/*.js).
    private const string LodashResourceName = "Elsa.Expressions.JavaScript.Libraries.ClientLib.dist.lodash.js";

    [Fact]
    public async Task PreProcess_ExecutesTheDecodedLibraryScript()
    {
        var context = new ScriptCapturingExecutionContext();

        await Preprocess(LodashResourceName, context);

        Assert.False(string.IsNullOrWhiteSpace(context.ExecutedScripts.Single()));
    }

    [Fact]
    public async Task PreProcess_DecodesResourceOnce_ReusesSameStringInstanceAcrossCalls()
    {
        // Distinct preprocessor instances model the Scoped registration: a per-instance field would not
        // survive across evaluations, so caching must be process-wide (keyed by resource name).
        var first = new ScriptCapturingExecutionContext();
        var second = new ScriptCapturingExecutionContext();

        await Preprocess(LodashResourceName, first);
        await Preprocess(LodashResourceName, second);

        // Pre-fix each call runs StreamReader.ReadToEnd(), yielding a fresh string every time.
        // Post-fix the decoded string is cached once and handed to every call by reference.
        Assert.Same(first.ExecutedScripts.Single(), second.ExecutedScripts.Single());
    }

    private static ValueTask Preprocess(string resourceName, IJavaScriptExecutionContext context)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new JavaScriptLibrariesFeatureOptions { FullModuleResourceName = resourceName });
        var preProcessor = new LibraryResourcePreProcessor(options);
        return preProcessor.PreProcess("", context, null!, null, CancellationToken.None);
    }

    private sealed class ScriptCapturingExecutionContext : IJavaScriptExecutionContext
    {
        public List<string> ExecutedScripts { get; } = [];

        public void Execute(string script) => ExecutedScripts.Add(script);

        public object? GetValue(string name) => null;
        public void SetValue(string name, object? value) { }
        public void RegisterFunction(IJavaScriptFunction function) { }
        public void RegisterType(Type type) { }
        public object? NormalizeValue(object? value) => value;
        public object? Evaluate(string script) => null;
    }
}
