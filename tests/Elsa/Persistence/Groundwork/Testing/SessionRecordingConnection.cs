using System.Reflection;
using Groundwork.Store;

namespace Elsa.Persistence.Groundwork.Testing;

/// <summary>
/// Forwards every member to a real provider connection and records the sessions it hands out, so a
/// test can assert that the component under test disposed each session it opened (#1597).
/// </summary>
public class SessionRecordingConnection : DispatchProxy
{
    private IStorageProviderConnection inner = null!;
    private List<IStorageSession> sessions = null!;

    public static IStorageProviderConnection Wrap(IStorageProviderConnection inner, out IReadOnlyList<IStorageSession> opened)
    {
        var proxy = Create<IStorageProviderConnection, SessionRecordingConnection>();
        var recording = (SessionRecordingConnection)(object)proxy;
        recording.inner = inner;
        var sessions = new List<IStorageSession>();
        recording.sessions = sessions;
        opened = sessions;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);
        object? result;
        try
        {
            result = targetMethod.Invoke(inner, args);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is { } cause)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(cause);
            throw;
        }
        if (targetMethod.Name is nameof(IStorageProviderConnection.OpenSession) or nameof(IStorageProviderConnection.OpenOwnedSession) && result is IStorageSession session)
            sessions.Add(session);
        return result;
    }
}
