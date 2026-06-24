# Quickstart: Workflow Definition Test Runs

## Prerequisites

- Elsa Server has Workflows Design, Activities Design, Workflows Publishing, and Workflows Runtime features enabled.
- A workflow definition version exists with a supported root activity and literal input bindings.

## Scenario 1: Start a test run from an unpublished workflow version

1. Create or import a workflow definition version with a valid root activity.
2. Request a test run for that version.
3. Confirm the response includes:
   - `testRunId`
   - `artifactId` beginning with the test/transient artifact prefix
   - `workflowExecutionId`
   - `status = DispatchAccepted`
4. Confirm normal published executable storage/listing does not include the test artifact.
5. Confirm runtime dispatch metadata includes test-run correlation values.

## Scenario 2: Reject invalid workflow content before dispatch

1. Create a workflow definition version without a root activity.
2. Request a test run for that version.
3. Confirm the response is rejected with a reason mentioning the missing root activity.
4. Confirm no workflow execution id is returned.
5. Confirm no command is enqueued to the runtime execution agent.

## Scenario 3: Normal runtime execution does not start transient artifacts

1. Start a valid workflow test run and capture the returned transient artifact id.
2. Attempt to execute that artifact through the normal runtime execute-by-artifact-id path.
3. Confirm the normal runtime path rejects it as not found or otherwise unavailable as a durable published artifact.

## Validation commands

Run the focused test projects:

```bash
dotnet test tests/Elsa/Workflows/Publishing/Api/Tests/Elsa.Workflows.Publishing.Api.Tests.csproj
dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj
```
