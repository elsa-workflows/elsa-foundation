using Elsa.Persistence.Groundwork.Serialization;

namespace Elsa.Persistence.Groundwork.Testing;

/// <summary>Production-equivalent runtime serializer shared by provider-level conformance suites.</summary>
public static class GroundworkProviderTestSerialization
{
    public static IGroundworkRuntimeDocumentSerializer Serializer { get; } =
        new GroundworkRuntimeDocumentSerializer(
            new GroundworkRuntimeDocumentUpcasterRegistry(
            [
                new WorkflowExecutableDocumentV1ToV2Upcaster(),
                new WorkflowExecutableDocumentV2ToV3Upcaster(),
                new WorkflowExecutableDocumentV3ToV4Upcaster(),
                new WorkflowExecutionStateDocumentV1ToV2Upcaster(),
                new WorkflowExecutionStateDocumentV2ToV3Upcaster(),
                new WorkflowExecutionStateDocumentV3ToV4Upcaster(),
                new WorkflowExecutableSourceReferenceDocumentV1ToV2Upcaster(),
                new WorkflowExecutableSourceReferenceDocumentV2ToV3Upcaster(),
                new WorkflowExecutableSourceReferenceDocumentV3ToV4Upcaster(),
                new WorkflowTriggerBindingDocumentV1ToV2Upcaster(),
                new RecurringTriggerScheduleDocumentV1ToV2Upcaster()
            ]));
}
