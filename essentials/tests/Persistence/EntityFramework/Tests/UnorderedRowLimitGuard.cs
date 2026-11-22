using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Elsa.Persistence.EntityFramework.Tests;

/// <summary>
/// Fails every EF query in the test assembly that limits rows with <c>Skip</c> or <c>Take</c> but has no
/// <c>OrderBy</c>, which EF otherwise reports only as warning 10102.
/// </summary>
/// <remarks>
/// Without an order the provider chooses which rows a limit returns, and EF logs the warning once, when the query
/// first compiles, so a passing test never shows it. A per-context <c>ConfigureWarnings</c> covers only the fixtures
/// that remember to set it, and many store tests build their contexts through the module's own registration. This
/// guard instead observes EF's diagnostic events for every context in the process, and installs itself when the
/// assembly loads. Each EF module's test project compiles this file in.
/// </remarks>
internal sealed class UnorderedRowLimitGuard : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>
{
    private static readonly string EventName = CoreEventId.RowLimitingOperationWithoutOrderByWarning.Name!;

    [ModuleInitializer]
    internal static void Install() => DiagnosticListener.AllListeners.Subscribe(new UnorderedRowLimitGuard());

    void IObserver<DiagnosticListener>.OnNext(DiagnosticListener listener)
    {
        if (listener.Name == DbLoggerCategory.Name)
            listener.Subscribe(this, name => name == EventName);
    }

    void IObserver<KeyValuePair<string, object?>>.OnNext(KeyValuePair<string, object?> diagnostic)
    {
        if (diagnostic.Key == EventName)
            throw new InvalidOperationException($"{EventName} (EF warning 10102): {diagnostic.Value}");
    }

    void IObserver<DiagnosticListener>.OnCompleted()
    {
    }

    void IObserver<DiagnosticListener>.OnError(Exception error)
    {
    }

    void IObserver<KeyValuePair<string, object?>>.OnCompleted()
    {
    }

    void IObserver<KeyValuePair<string, object?>>.OnError(Exception error)
    {
    }
}
