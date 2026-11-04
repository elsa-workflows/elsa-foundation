namespace Elsa.Foundation.Identity.ExternalAuthentication.Oidc;

public sealed class ManagedOidcOptions
{
    /// <summary>Only permits literal loopback HTTP in a positively identified Development host.</summary>
    public bool AllowLoopbackHttpForDevelopment { get; set; }
    public TimeSpan BackchannelTimeout { get; set; } = TimeSpan.FromSeconds(10);
}
