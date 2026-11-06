using Elsa.Activities.Bpmn.Core;
using Elsa.Workflows.Runtime.Core.Messaging;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Bpmn.Internal;

/// <summary>Pure, unregistered BPMN strategy. Owner/source closure and actual model delivery remain separate.</summary>
internal sealed class BpmnMessageMaterialInterestProvider : IMessageMaterialInterestProvider
{
    public string ProviderId => "elsa.bpmn.message-material/v1";
    public RuntimeExecutionProtocol ModelProtocol { get; } = new(BpmnModelProtocol.Key, BpmnModelProtocol.Version);
    public MessageMaterialInterestResult Describe(RuntimeExecutionMaterial material)
    {
        ArgumentNullException.ThrowIfNull(material);
        try { return DescribeChecked(material); }
        catch (BpmnModelValidationException)
        {
            // The generic Runtime extractor intentionally recognizes only safe framework exception families.
            // Domain validation can name private graph elements; discard both its text and its raw cause here.
            throw Unavailable();
        }
    }
    private MessageMaterialInterestResult DescribeChecked(RuntimeExecutionMaterial material)
    {
        if (material.Model is not { } model || model.Protocol != ModelProtocol) return MessageMaterialInterestResult.NotRecognized;
        if (model.Profile == BpmnMessageModelDefinition.Profile)
            return MessageMaterialInterestResult.Recognized(BpmnMessageMaterial.ReadChecked(model).Bindings.Select(binding => binding.Declaration()).ToArray());
        if (model.Profile is not (BpmnModelProtocol.Profile or BpmnModelProtocol.NativeWorkProfile or BpmnModelProtocol.ClaimsProfile))
            return MessageMaterialInterestResult.NotRecognized;
        if (model.Identity.ArtifactHash != model.ComputeContentHash()) throw Unavailable();
        BpmnDirectExecutionDriver.ValidateLegacyInterestMaterial(model);
        return MessageMaterialInterestResult.Recognized([]);
    }
    private static NotSupportedException Unavailable() => new("BPMN Message material interest cannot be determined.");
}
