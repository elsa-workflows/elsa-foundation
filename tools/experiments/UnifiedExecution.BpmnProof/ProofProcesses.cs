using Bpmn.Model;
using Bpmn.Model.State;
using Bpmn.Semantics;

namespace UnifiedExecution.BpmnProof;

internal static class ProofProcesses
{
    public static BpmnProcessDefinition ForkJoin() =>
        new BpmnProcessBuilder("proof-fork-join")
            .StartEvent("Start")
            .ParallelGateway("Fork")
            .UserTask("Left", bindingRef: "left")
            .UserTask("Right", bindingRef: "right")
            .ParallelGateway("Join")
            .EndEvent("End")
            .Connect("Start", "Fork")
            .Connect("Fork", "Left")
            .Connect("Fork", "Right")
            .Connect("Left", "Join")
            .Connect("Right", "Join")
            .Connect("Join", "End")
            .Build();

    public static BpmnProcessDefinition ForkJoinChangedGraph() =>
        new BpmnProcessBuilder("proof-fork-join")
            .StartEvent("Start")
            .ParallelGateway("Fork")
            .UserTask("Left", bindingRef: "left")
            .UserTask("RightV2", bindingRef: "right")
            .ParallelGateway("Join")
            .EndEvent("End")
            .Connect("Start", "Fork")
            .Connect("Fork", "Left")
            .Connect("Fork", "RightV2")
            .Connect("Left", "Join")
            .Connect("RightV2", "Join")
            .Connect("Join", "End")
            .Build();

    public static BpmnProcessDefinition HumanWithDeadline() =>
        new BpmnProcessBuilder("proof-human-deadline")
            .StartEvent("Start")
            .UserTask("Review", bindingRef: "review")
            .EndEvent("Approved")
            .BoundaryEvent(
                "Timeout",
                "Review",
                new BpmnEventDefinition(BpmnEventDefinitionTypes.Timer,
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        [BpmnEventDefinitionProperties.Interval] = "PT1H"
                    }),
                interrupting: true,
                bindingRef: "timeout")
            .UserTask("Cleanup", bindingRef: "cleanup")
            .EndEvent("Expired")
            .ConnectSequence("Start", "Review", "Approved")
            .ConnectSequence("Timeout", "Cleanup", "Expired")
            .Build();

    public const string InterruptionMessageName = "cancel-review";

    public static BpmnProcessDefinition HumanWithMessageInterrupt() =>
        new BpmnProcessBuilder("proof-human-message-interrupt")
            .StartEvent("Start")
            .UserTask("Review", bindingRef: "review")
            .EndEvent("Approved")
            .BoundaryEvent("CancelReview", "Review",
                new BpmnEventDefinition(BpmnEventDefinitionTypes.Message,
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        [BpmnEventDefinitionProperties.Name] = InterruptionMessageName
                    }),
                interrupting: true,
                bindingRef: "cancel-review-listener")
            .UserTask("Cleanup", bindingRef: "cleanup")
            .EndEvent("Cancelled")
            .ConnectSequence("Start", "Review", "Approved")
            .ConnectSequence("CancelReview", "Cleanup", "Cancelled")
            .Build();

    public static BpmnProcessDefinition Risky(bool catchError)
    {
        var builder = new BpmnProcessBuilder(catchError ? "proof-risky-caught" : "proof-risky-propagated")
            .StartEvent("Start")
            .ServiceTask("Worker", bindingRef: "worker")
            .EndEvent("Done")
            .ConnectSequence("Start", "Worker", "Done");

        return catchError
            ? builder
                .BoundaryEvent("Failed", "Worker", new BpmnEventDefinition(BpmnEventDefinitionTypes.Error))
                .UserTask("Recover", bindingRef: "recover")
                .EndEvent("Recovered")
                .ConnectSequence("Failed", "Recover", "Recovered")
                .Build()
            : builder.Build();
    }

    public static BpmnProcessDefinition SequenceTail() =>
        new BpmnProcessBuilder("proof-sequence-tail")
            .StartEvent("Start")
            .UserTask("Continue", bindingRef: "continue")
            .EndEvent("End")
            .ConnectSequence("Start", "Continue", "End")
            .Build();

    public static IReadOnlyList<string> NormalizeDiagnostics(BpmnExecutionState state) =>
        state.Diagnostics
            .Select(diagnostic => $"{diagnostic.Kind}|{diagnostic.ElementId ?? ""}|{diagnostic.Message}")
            .ToArray();

}
