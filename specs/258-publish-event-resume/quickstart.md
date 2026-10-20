# Quickstart: verify Publish Event → Event

Build and test only the touched projects. Allow for the build-slot queue on shared machines.

```bash
dotnet test Essentials/tests/Activities/Runtime/Tests/Elsa.Activities.Runtime.Tests.csproj --filter "FullyQualifiedName~EventReceived|FullyQualifiedName~PublishEvent|FullyQualifiedName~EventTriggerStimulusProvider|FullyQualifiedName~EventCorrelation|FullyQualifiedName~UndeclaredPayloadType|FullyQualifiedName~EventTestDrive"
```

```bash
dotnet test Essentials/tests/Activities/Bpmn/Tests/Elsa.Activities.Bpmn.Tests.csproj
```

```bash
dotnet test Essentials/tests/Activities/Behavioral/Tests/Elsa.Activities.Behavioral.Tests.csproj
```

```bash
dotnet test Extensions/Elsa3/tests/Activities/Design/Import/Integration/Tests/Elsa3.Activities.Design.Import.Integration.Tests.csproj --filter "FullyQualifiedName~PublishEventResume|FullyQualifiedName~ReusableBodyWait"
```

```bash
dotnet test Extensions/Elsa3/tests/Mapping/Tests
```

Expected results:

1. A waiting Event resumed by Publish Event, with and without a payload, completes in a regular workflow and inside a
   graph activity body. There is no `ActivityResumeFaulted` incident.
2. The Event's `Payload` output equals the published payload, or null when there was none.
3. Existing Event, BPMN, test-run and graph-boundary tests pass with unchanged assertions.
4. An Elsa 3 Event whose `Result` is captured converts with no Lost diagnostic.

Manual check (optional): in the Workbench, run a workflow that waits on Event `order.approved`, then run one that
publishes `order.approved` with a payload. The first completes, and its Event shows the payload under `Payload`.
