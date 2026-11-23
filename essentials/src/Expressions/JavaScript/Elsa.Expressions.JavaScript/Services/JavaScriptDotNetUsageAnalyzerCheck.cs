using CShells.Lifecycle;
using Elsa.Expressions.JavaScript.Core.Contracts;

namespace Elsa.Expressions.JavaScript.Services;

/// <summary>
/// Fails shell activation when more than one <see cref="IJavaScriptDotNetUsageAnalyzer"/> is composed (framework §2.6.2):
/// a replacement contract has exactly one implementation per host, and a second registration must not win silently.
/// </summary>
internal sealed class JavaScriptDotNetUsageAnalyzerCheck(IEnumerable<IJavaScriptDotNetUsageAnalyzer> analyzers) : IShellInitializer
{
    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var composed = analyzers.ToArray();
        if (composed.Length > 1)
        {
            throw new InvalidOperationException(
                $"More than one JavaScript .NET-usage analyzer is registered ({string.Join(", ", composed.Select(analyzer => analyzer.GetType().FullName))}). " +
                "IJavaScriptDotNetUsageAnalyzer is a replacement contract: register one, before the JavaScript feature or with Replace.");
        }

        return Task.CompletedTask;
    }
}
