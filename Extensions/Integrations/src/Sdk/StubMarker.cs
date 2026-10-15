namespace Elsa.Integrations.Sdk;

/// <summary>
/// Marks this assembly as a stand-in for <c>Elsa.Integrations.Sdk</c> until
/// <see href="https://github.com/elsa-workflows/elsa-foundation/issues/2726">#2726</see> lands.
/// </summary>
/// <remarks>
/// Contract rules from the 13 Oct revision of #2726, which this stub follows:
/// no duplicate activity manifest, no Elsa 3 shims, secrets via
/// <c>ISecretValueResolver</c>, HTTP via the named <c>Elsa.Activities.Http</c> client,
/// design-time schema never loaded to execute. Checked 2026-10-13: #2726 has no branch or PR.
/// </remarks>
internal static class IntegrationsSdkStub
{
    public const string Issue = "elsa-workflows/elsa-foundation#2726";
    public const bool IsStub = true;
}
