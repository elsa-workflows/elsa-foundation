# Engine-neutral refactor scope

One row per suite location holding an Elsa 3-specific value, and where it moves (spec 1235 task T019, data-model.md).

| Suite location | Elsa 3-specific value | Moves to |
|---|---|---|
| `events/basic-event-tests.json`, *Act & Assert - Execute workflow*, step `Execute` | `POST /elsa/api/workflow-definitions/{id}/execute`; reads `body.workflowState.status`, `.subStatus`, `.output.EventListenerCorrelationIdentifier` | new template `elsa-execute-workflow-definition` (`workflowStatus`, `workflowSubStatus`, `output`); the request input moves to the suite global `executeInput` |
| `events/basic-event-tests.json`, same case, step *Get event listener workflow instance details* | `instance.workflowState.output.Result`, `.Result.message` | new template `elsa-get-workflow-instance-input-output` (`output`), in a step after the existing one |
| `performance/big-data-tests.json`, *Act & Assert - Execute workflow* | execute call with the request body from `test-files/big-mock-data.json`; reads `body.workflowState.status`, `.subStatus`, `.output.OutputData.itemCount`, `.id` | new template `elsa-execute-workflow-definition-from-file` (`workflowStatus`, `workflowSubStatus`, `output`, `workflowInstanceId`) |
| `performance/commit-after-every-activity-tests.json`, *Act & Assert - Execute workflow* | execute call; reads `body.workflowState.status`, `.subStatus` and `body.workflowState.activityExecutionContexts[0].properties.Variables[…]` | `elsa-execute-workflow-definition` (`workflowStatus`, `workflowSubStatus`, `variables[…]`: the finished run's root-scope variables, as the execute response held them; a stored-instance read does not have this one); input in `executeInput` |
| `performance/concurrency-tests.json`, *Act - Execute workflow* | execute call; reads `body.workflowState.status`, `.subStatus`, `.correlationId`, `.output.DispatchCorrelationIdentifier` | `elsa-execute-workflow-definition` (`workflowStatus`, `workflowSubStatus`, `correlationId`, `output`); input in `executeInput` |
| `performance/concurrency-tests.json`, *Assert all instances finished successfully*, step *Search completed dispatched workflow instances* | `GET /elsa/api/workflow-instances` filtered by correlation id, `status`/`subStatus` `Finished`; reads `body.totalCount` | new template `elsa-count-workflow-instances` (`count`) |
| `logging/logging-tests.json`, *Assert: all variables still visible…* | `instance.workflowState.activityExecutionContexts[0].properties.Variables[…]` (4 reads) | `elsa-get-workflow-instance-variables` (`variables[…]`), in a step after the existing one |
| `storage-drivers/variables-tests.json`, *Execute test case: Memory* and *Workflow Instance* | the same `Variables[…]` reads (6 per case; `notexists` for variables not kept on the instance) | `elsa-get-workflow-instance-variables`, in a step after the existing one |
| `composition/bulk-dispatch-workflow-handling-handling-input-tests.json`, *Execute Test Case: Handling Input*, step 4 | `instance.workflowState.input.ShouldFault` | `elsa-get-workflow-instance-input-output` (`input`), in a step after the existing one |
| every `elsa-activity-instance-get` call (31, in 8 suites) | the Elsa 3 activity execution id is enough; Elsa 4 also needs the workflow instance | added parameter `workflowInstanceId` (the instance of the journal call before it); `elsa-activity-instance-find` passes it on |

## Values the contract already covers

- **Status words** compared against template outputs: `elsa-search-workflow-instances` `status`/`subStatus` (`Finished` 18,
  `Running` 3, `Suspended` 2, `Faulted` 2 uses) and `elsa-activity-instance-get` `status` (`Completed` 7, `Running` 1) use the
  status tables; `activityState.StatusCode` `Accepted` (3) uses the HTTP status rule. After the refactor no status word is
  compared against an Elsa 3 response directly; the new templates take and return the Elsa 3 words.
- **Identifiers**: definition ids, version ids (`versionId` globals) and activity ids follow the identity rules.

## Keys the Elsa 4 templates must supply

- `activityState`: `Condition`, `Content` (`.testProperty1`), `ContentType`, `End`, `Items[n]`, `OuterBoundInclusive`,
  `OutputName`, `OutputValue` (`.property1`, `.itemCount`), `ResponseHeaders` (`['Correlation-ID']`), `Start`, `StatusCode`,
  `Step`, `Text`, `Value` (`.itemCount`, `.CustomOutput.testProp1`, `.<sub-workflow output name>`), `Variable` (`.id`).
- `outputs`: `Headers[<name>]`, `ParsedContent` (`.testProperty1`), `Result` (`.itemCount`, `.CustomOutput.testProp1`).
- `elsa-activity-instance-get-journal-items-completed` entries: `activityInstanceId` (read by `single-outcome`).
- `elsa-get-workflow-instance-variables` `variables`: keyed by the Elsa 3 variable id; a variable the instance does not
  keep is absent.

## Notes

- JTest 2.0.3 resolves tokens inside an object passed in a `use` step's `with` in the template's scope, where the suite's
  `$.globals` is not visible (the value arrives as `null`); a whole-token reference such as
  `"input": "{{ $.globals.executeInput }}"` resolves in the suite's scope. Execute inputs therefore live in a suite global.
- Run 9 (refactor before that fix) failed `events`, `commit-after-every-activity` and `concurrency`; run 11 passes all 39.
