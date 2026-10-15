using Elsa.Integrations.Sdk.Connections;
using Elsa.Integrations.Sdk.OAuth;

namespace Elsa.Integrations.Sdk.Harness;

/// <summary>
/// Stub of D1: the named HTTP client is bound to the tenant's http connection until
/// <c>SendHttpRequest</c> grows an <c>[ActivityConnection]</c> input.
/// </summary>
internal sealed class HarnessAuthState : IActiveConnectionAccessor
{
    public ConnectionRecord? ActiveConnection { get; set; }
    public OAuth2TokenService? Tokens { get; set; }
    public CapturingSecretMask Mask { get; } = new();
    public string ActivityExecutionId { get; set; } = "actexec-oauth2";

    ConnectionRecord IActiveConnectionAccessor.Current =>
        ActiveConnection ?? throw new InvalidOperationException("No active http connection is bound.");
}
