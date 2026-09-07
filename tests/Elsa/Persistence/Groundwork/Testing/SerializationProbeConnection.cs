using System.Reflection;
using Groundwork.Store;

namespace Elsa.Persistence.Groundwork.Testing;

/// <summary>
/// Wraps every owned session a connection hands out so each command holds the session for a short
/// while and records how many commands overlapped on it. A component that serializes its own use of an
/// owned session never overlaps; one that does not overlaps as soon as two callers race.
/// </summary>
public class SerializationProbeConnection : DispatchProxy
{
    private IStorageProviderConnection inner = null!;
    private List<ProbeSession> probes = null!;

    public static IStorageProviderConnection Wrap(IStorageProviderConnection inner, out Func<int> maxOverlap)
    {
        var proxy = Create<IStorageProviderConnection, SerializationProbeConnection>();
        var connection = (SerializationProbeConnection)(object)proxy;
        connection.inner = inner;
        var probes = connection.probes = [];
        maxOverlap = () => probes.Count == 0 ? 0 : probes.Max(probe => probe.MaxOverlap);
        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);
        var result = Forward(targetMethod, inner, args);
        if (targetMethod.Name == nameof(IStorageProviderConnection.OpenOwnedSession) && result is IOwnedStorageSession session)
        {
            var probe = ProbeSession.Wrap(session);
            probes.Add(probe);
            return probe.Proxy;
        }
        return result;
    }

    internal static object? Forward(MethodInfo method, object target, object?[]? args)
    {
        try
        {
            return method.Invoke(target, args);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is { } cause)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(cause);
            throw;
        }
    }

    /// <summary>Every capability the diagnostics stores discover by type test on their sessions.</summary>
    public interface IProbedSession :
        IOwnedStorageSession,
        IStorageInspectionSession,
        IRetentionStorageSession,
        IExactAppendStorageSession;

    public class ProbeSession : DispatchProxy
    {
        private static readonly TimeSpan Hold = TimeSpan.FromMilliseconds(25);
        private IOwnedStorageSession inner = null!;
        private int inFlight;
        private int maxOverlap;

        public IProbedSession Proxy { get; private set; } = null!;

        public int MaxOverlap => maxOverlap;

        internal static ProbeSession Wrap(IOwnedStorageSession inner)
        {
            var proxy = Create<IProbedSession, ProbeSession>();
            var probe = (ProbeSession)(object)proxy;
            probe.inner = inner;
            probe.Proxy = proxy;
            return probe;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(targetMethod);
            if (targetMethod.DeclaringType!.IsInstanceOfType(inner) is false)
                throw new NotSupportedException($"The wrapped session does not implement {targetMethod.DeclaringType.Name}.");
            // Property reads such as Unit and Access are not provider commands; only calls count as overlap.
            if (targetMethod.IsSpecialName)
                return Forward(targetMethod, inner, args);
            var now = Interlocked.Increment(ref inFlight);
            int seen;
            do
            {
                seen = maxOverlap;
            } while (now > seen && Interlocked.CompareExchange(ref maxOverlap, now, seen) != seen);
            try
            {
                Thread.Sleep(Hold);
                return Forward(targetMethod, inner, args);
            }
            finally
            {
                Interlocked.Decrement(ref inFlight);
            }
        }
    }
}
