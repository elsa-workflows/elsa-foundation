using System.Threading;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class AmbientCheckpointRecoveryAuthorityAccessor : IRuntimeCheckpointRecoveryAuthorityAccessor
{
    private readonly AsyncLocal<Scope?> _current = new();

    public RuntimeCheckpointRecoveryAuthority? Current => _current.Value?.Authority;

    public IDisposable Push(RuntimeCheckpointRecoveryAuthority authority)
    {
        ArgumentNullException.ThrowIfNull(authority);

        var scope = new Scope(this, authority, _current.Value);
        _current.Value = scope;
        return scope;
    }

    private sealed class Scope(
        AmbientCheckpointRecoveryAuthorityAccessor owner,
        RuntimeCheckpointRecoveryAuthority authority,
        Scope? parent) : IDisposable
    {
        private bool _disposed;

        public RuntimeCheckpointRecoveryAuthority Authority { get; } = authority;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            if (ReferenceEquals(owner._current.Value, this))
                owner._current.Value = parent;
        }
    }
}
