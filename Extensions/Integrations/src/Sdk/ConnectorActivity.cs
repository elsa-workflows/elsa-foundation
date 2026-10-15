using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Integrations.Sdk;

/// <summary>
/// Base for connector activities. v1 is activities only: no host endpoints, hosted services, or other
/// host-integrated features, so the package stays collectible.
/// </summary>
public abstract class ConnectorActivity<TResult> : Activity<TResult>
{
    protected ConnectorActivity(IConnectionContext connectionContext)
    {
        ArgumentNullException.ThrowIfNull(connectionContext);
        ConnectionContext = connectionContext;
    }

    protected IConnectionContext ConnectionContext { get; }
}

/// <inheritdoc cref="ConnectorActivity{TResult}"/>
public abstract class ConnectorActivity : ConnectorActivity<ActivityUnit>
{
    protected ConnectorActivity(IConnectionContext connectionContext) : base(connectionContext)
    {
    }
}
