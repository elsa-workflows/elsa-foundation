using CShells.Features;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection.Extensions;

// Test-only qualification seam (spec 1193, story 9; T045). It is not product code and never ships.
// The driver selects this feature only for the workflow-run inventory, through the ordinary native feature apply, and
// deselects it afterwards. The runtime's default capture policy never captures a sensitive payload, so a canary authored
// into a sensitive input would never reach execution evidence and a "canary absent" check could not fail. This seam
// replaces the single IRuntimePayloadCapturePolicy registration with a wrapper over the host's actual policy that captures
// sensitive activity inputs as full payloads, and delegates every other decision unchanged. The canary then really is in
// the run's value evidence, so a regression that released a captured sensitive value to Weaver would leak natively.
[ShellFeature(name: "QualificationRunCapture", DependsOn = new object[] { "WorkflowsRuntimeApi" })]
public sealed class QualificationRunCaptureFeature : IShellFeature
{
    internal const string Reason = "Qualification seam: sensitive activity inputs are captured so a disclosure of them is detectable.";

    public void ConfigureServices(IServiceCollection services)
    {
        var actual = services.LastOrDefault(item => item.ServiceType == typeof(IRuntimePayloadCapturePolicy))
                     ?? throw new InvalidOperationException("QualificationRunCapture needs the host's actual runtime payload capture policy.");
        // Exactly one policy stays registered; it delegates to the host's actual one.
        services.RemoveAll<IRuntimePayloadCapturePolicy>();
        services.AddSingleton<IRuntimePayloadCapturePolicy>(provider => new SensitiveInputCapturePolicy(Resolve(provider, actual)));
    }

    private static IRuntimePayloadCapturePolicy Resolve(IServiceProvider provider, ServiceDescriptor descriptor) =>
        descriptor.ImplementationInstance as IRuntimePayloadCapturePolicy
        ?? descriptor.ImplementationFactory?.Invoke(provider) as IRuntimePayloadCapturePolicy
        ?? (IRuntimePayloadCapturePolicy)ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType
            ?? throw new InvalidOperationException("The host's runtime payload capture policy has no implementation."));
}

sealed class SensitiveInputCapturePolicy(IRuntimePayloadCapturePolicy actual) : IRuntimePayloadCapturePolicy
{
    public RuntimePayloadCaptureDecision Decide(RuntimePayloadCaptureRequest request) =>
        request.IsSensitive && request.Subject == RuntimePayloadCaptureSubject.ActivityInput
            ? new RuntimePayloadCaptureDecision(RuntimePayloadCaptureMode.Payload, QualificationRunCaptureFeature.Reason)
            : actual.Decide(request);
}
