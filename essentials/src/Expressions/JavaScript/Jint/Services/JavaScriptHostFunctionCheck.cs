using CShells.Lifecycle;

namespace Elsa.Expressions.JavaScript.Jint.Services;

/// <summary>
/// Fails shell activation when a registered JavaScript function breaks the naming rule (spec 1223 FR-027): resolving
/// the catalog checks every registration and names the offending function.
/// </summary>
internal sealed class JavaScriptHostFunctionCheck(JavaScriptHostFunctions functions) : IShellInitializer
{
    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _ = functions;
        return Task.CompletedTask;
    }
}
