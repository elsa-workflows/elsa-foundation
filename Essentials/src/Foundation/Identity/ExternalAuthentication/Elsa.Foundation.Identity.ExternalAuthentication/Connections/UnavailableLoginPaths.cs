using Elsa.Foundation.Identity.ExternalAuthentication.Core;

namespace Elsa.Foundation.Identity.ExternalAuthentication.Connections;

/// <summary>A provider or session bridge must establish usability; configuration flags alone never do.</summary>
internal sealed class UnavailableConnectionReadiness : IConnectionReadiness
{
    public ValueTask<bool> IsReadyAsync(ConnectionRecord connection, CancellationToken cancellationToken = default) => ValueTask.FromResult(false);
}

internal sealed class UnavailableLocalRecovery : ILocalLoginRecovery
{
    public ValueTask<bool> IsUsableAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(false);
}

internal sealed class UnavailableConnectionRecovery : IConnectionRecovery
{
    public ValueTask<bool> IsUsableAsync(ConnectionRecord connection, CancellationToken cancellationToken = default) => ValueTask.FromResult(false);
}
