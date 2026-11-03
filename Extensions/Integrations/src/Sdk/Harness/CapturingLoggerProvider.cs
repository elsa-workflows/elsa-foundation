using Microsoft.Extensions.Logging;

namespace Elsa.Integrations.Sdk.Harness;

internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly List<string> _messages = [];

    public IReadOnlyList<string> Messages
    {
        get
        {
            lock (_messages)
                return [.._messages];
        }
    }

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, categoryName);

    public void Dispose()
    {
    }

    private void Add(string message)
    {
        lock (_messages)
            _messages.Add(message);
    }

    private sealed class CapturingLogger(CapturingLoggerProvider provider, string categoryName) : ILogger
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var text = formatter(state, exception);
            provider.Add($"{categoryName}: {text}");
            if (state is IReadOnlyList<KeyValuePair<string, object?>> values)
            {
                foreach (var pair in values)
                    provider.Add($"{categoryName} state {pair.Key}={pair.Value}");
            }
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose()
        {
        }
    }
}
