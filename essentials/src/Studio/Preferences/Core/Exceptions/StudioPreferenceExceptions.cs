namespace Elsa.Studio.Preferences.Core.Exceptions;

public abstract class StudioPreferenceException(string message) : Exception(message);

public sealed class StudioPreferenceScopeException(string message) : StudioPreferenceException(message);

public sealed class StudioPreferenceHostException(string message) : StudioPreferenceException(message);

public sealed class StudioPreferenceNamespaceNotFoundException(string name)
    : StudioPreferenceException($"Studio preference namespace '{name}' is not registered.");

public sealed class StudioPreferenceValidationException(string message) : StudioPreferenceException(message);

public sealed class StudioPreferenceQuotaExceededException(string name, int actualBytes, int maximumBytes)
    : StudioPreferenceException($"Studio preference namespace '{name}' accepts at most {maximumBytes} bytes; the value contains {actualBytes} bytes.")
{
    public int ActualBytes { get; } = actualBytes;
    public int MaximumBytes { get; } = maximumBytes;
}

public sealed class StudioPreferenceConflictException(string message) : StudioPreferenceException(message);
