using Elsa.Studio.Dashboard.Core.Exceptions;
using Elsa.Studio.Dashboard.Core.Models;

namespace Elsa.Studio.Dashboard.Management.Query;

/// <summary>Shared bounded admission state; released scopes are removed rather than retained as a user catalog.</summary>
internal sealed class DashboardReadGate
{
    private readonly object _sync = new();
    private readonly Dictionary<(string Tenant, string Subject), int> _active = [];

    public IDisposable Enter(DashboardRequestScope scope, int maximum)
    {
        var key = (scope.TenantId, scope.SubjectId);
        lock (_sync)
        {
            var count = _active.GetValueOrDefault(key);
            if (count >= maximum || _active.Values.Sum() >= 32)
                throw new DashboardQueryException("QUERY_CONCURRENCY_LIMIT", "Too many dashboard reads are active. Cancel a read or retry shortly.");
            _active[key] = count + 1;
        }
        return new Lease(this, key);
    }

    private void Leave((string Tenant, string Subject) key)
    {
        lock (_sync)
        {
            var count = _active[key];
            if (count == 1) _active.Remove(key);
            else _active[key] = count - 1;
        }
    }

    private sealed class Lease(DashboardReadGate owner, (string Tenant, string Subject) key) : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) owner.Leave(key);
        }
    }
}
