using CShells.Features;
using Elsa.Api.Capabilities;
using Elsa.Api.Capabilities.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Acme.TriggerAuthoring;

/// <summary>Owned advertisement fixture only. The runner disables the actual v1 API; this installs no v2 endpoint or model.</summary>
[ShellFeature("AcceptanceUnsupportedStandaloneContract", DisplayName = "Acceptance unsupported contract advertisement fixture",
    DependsOn = new object[] { "ApiCapabilities" })]
public sealed class UnsupportedContractFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services) => ApiCapabilityRegistration.Add(services,
        new ApiCapabilityDeclaration("elsa.api.standalone-triggers", 2,
            [new("identity", "runtime/standalone-triggers/identity"), new("triggers", "runtime/standalone-triggers")],
            "AcceptanceUnsupportedStandaloneContractAdvertisementFixture"));
}
