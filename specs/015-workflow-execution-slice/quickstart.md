# Quickstart: Workflow Execution Vertical Slice

## Goal

Start `Elsa.Server`, create a workflow definition/version through REST, publish the version into a `WorkflowExecutable`, and execute that artifact through Runtime REST.

## Prerequisites

- .NET SDK capable of building this repo's `net10.0` projects.
- `Elsa.Server` starts successfully.
- The activity reconciliation startup path has populated the activity catalog with `WriteLine`.

## Validate

```bash
dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj
dotnet test tests/Elsa/Workflows/Publishing/Api/Tests/Elsa.Workflows.Publishing.Api.Tests.csproj
dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj
dotnet run --project src/Apps/Elsa.Server/Elsa.Server.csproj
```

## Demo Flow

1. List constructable activities and find the `WriteLine` activity version id.

   ```http
   GET http://localhost:5000/default/publishing/activities
   ```

2. Create a workflow definition.

   ```http
   POST http://localhost:5000/default/design/workflows/definitions
   Content-Type: application/json

   {
     "name": "Monday Demo",
     "description": "Sequential WriteLine workflow"
   }
   ```

3. Create a workflow version using a JSON state with two `WriteLine` activity nodes.

   Replace `<definition-id>` and `<write-line-activity-version-id>` with values from earlier calls.

   ```http
   POST http://localhost:5000/default/design/workflows/versions
   Content-Type: application/json

   {
     "definitionId": "<definition-id>",
     "state": {
       "variables": [],
       "activityConnections": [
         {
           "source": { "activityNodeId": "write-hello", "port": "Done" },
           "target": { "activityNodeId": "write-goodbye", "port": "In" }
         }
       ],
       "activities": [
         {
           "nodeId": "write-hello",
           "activityVersionId": "<write-line-activity-version-id>",
           "inputs": [
             {
               "referenceKey": "Text",
               "value": { "value": "Hello from a REST-defined workflow", "expressionType": "Literal" },
               "autoEvaluate": null,
               "evaluatorType": null,
               "storageDriverType": null,
               "isSensitive": null
             }
           ],
           "outputs": [],
           "isContainer": false,
           "isStart": true,
           "isTerminal": false,
           "childActivities": []
         },
         {
           "nodeId": "write-goodbye",
           "activityVersionId": "<write-line-activity-version-id>",
           "inputs": [
             {
               "referenceKey": "Text",
               "value": { "value": "Workflow execution completed", "expressionType": "Literal" },
               "autoEvaluate": null,
               "evaluatorType": null,
               "storageDriverType": null,
               "isSensitive": null
             }
           ],
           "outputs": [],
           "isContainer": false,
           "isStart": false,
           "isTerminal": true,
           "childActivities": []
         }
       ],
       "inputs": [],
       "outputs": [],
       "workflowActivityOptions": null,
       "strategyOptions": null
     }
   }
   ```

4. Publish the workflow version.

   ```http
   POST http://localhost:5000/default/publishing/workflows/<version-id>/publish
   Content-Type: application/json

   {}
   ```

5. Execute the published artifact.

   ```http
   POST http://localhost:5000/default/runtime/workflows/<artifact-id>/execute
   Content-Type: application/json

   {}
   ```

Expected final result:

- `status` is `Completed`.
- `activities` contains two completed activity executions.
- Server console output includes the two `WriteLine` messages.
