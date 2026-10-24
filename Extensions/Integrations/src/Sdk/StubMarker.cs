namespace Elsa.Integrations.Sdk;

/// <summary>
/// Marks this assembly as a stand-in for <c>Elsa.Integrations.Sdk</c> until
/// <see href="https://github.com/elsa-workflows/elsa-foundation/issues/2726">#2726</see> lands.
/// </summary>
/// <remarks>
/// #2726 is being built on <c>cursor/integrations-sdk-v1-af3f</c> (PR #2825). This assembly
/// stays a non-packable marker until that PR merges. It does not copy the SDK implementation.
/// </remarks>
internal static class IntegrationsSdkStub
{
    public const string Issue = "elsa-workflows/elsa-foundation#2726";
    public const bool IsStub = true;
}
