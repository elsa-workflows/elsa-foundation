# Contract: diagnostic codes added by spec 246

Defined in `ReusableActivityImportDiagnosticCodes`
(`Extensions/Elsa3/src/Activities/Design/Import/Elsa3.Activities.Design.Import/Models/ReusableActivityImportPlan.cs`).
Appended at the end of the class, a file #2717 (spec 247) may also touch. The codes are stable once merged.

| Code | Severity | Raised by | Message (shape) | Guidance |
|---|---|---|---|---|
| `ELS3-REUSABLE-UNSUPPORTED-START-TRIGGER` | error, blocks apply | analyzer, per reusable source with a `CanStartWorkflow` trigger (a flagged node the activity map does not know as a non-trigger) that owns no nested behaviour | "Reusable workflow '{definitionId}' v{version} starts from trigger '{nodeId}' ({type}). Elsa 4 cannot start a reusable activity from a trigger." | "Move the trigger into its own Elsa 3 workflow that calls the reusable workflow, then re-export the collection." For a type the map does not know, it adds: "If '{type}' is not a trigger, clear 'Can start workflow' on it instead, and check that the flowchart still starts where it should: Elsa 3 then starts at the first activity nothing leads into." |
| `ELS3-REUSABLE-PUBLISHING-UNAVAILABLE` | error, blocks apply | importer, before commit, when the selection holds a reusable version and no `IReusableActivityImportPublisher` is registered | "This host cannot publish imported reusable activities." | "Enable the `Elsa3ImportActivitiesPublishing` feature (it needs `WorkflowsPublishingApi`)." |

Metadata:
- `UNSUPPORTED-START-TRIGGER` carries `nodeId` and `activityType`, at the node's JSON path.
- `PUBLISHING-UNAVAILABLE` has no node.

Unchanged: `ELS3-REUSABLE-UNSUPPORTED-TRIGGER` (a trigger owning nested behaviour) keeps its meaning. A node that matches
it gets only that code, not both.

Publisher refusals during the publish phase are **not** wrapped in new codes. Each `ReusableActivityPublicationState`
carries the publisher's own code (`activity.*`) and diagnostics. The exception is a version id held by a publication of
a different version, which reuses `ELS3-REUSABLE-IDENTITY-COLLISION`.

## Added during implementation (D-7, 2026-10-19)

Defined in `Elsa3ActivityDiagnosticCodes` (`Extensions/Elsa3/src/Models/Elsa3ActivityDiagnosticCodes.cs`), also a file
#2717 may touch.

| Code | Severity | Raised by | Message (shape) | Guidance |
|---|---|---|---|---|
| `ELS3-ACTIVITY-REUSABLE-INTRINSIC-UNSUPPORTED` | error, blocks apply | conversion of a reusable workflow, per node that resolves to an engine intrinsic (SetVariable, SetOutput, Correlate, SetName, Finish), and per HTTP Endpoint whose captured Headers or QueryStringData needs a synthesized set step | "Reusable workflow node '{nodeId}' ({type}) becomes an Elsa 4 engine intrinsic, which a graph activity cannot hold yet." For an HTTP Endpoint: "... captures its '{output}' output, which needs a step a graph activity cannot hold yet." | Per type (`Elsa3ReusableGuidance.Intrinsic`, R-19): delete an unread Set Variable or Set Output; move Correlate or Set Name into each caller; replace Finish with a Complete outcome the callers connect to a Finish; remove a captured HTTP projection. Otherwise wait for #3023, or import as a regular workflow, whose outputs callers read from the Elsa 4 Dispatch Workflow's Result after import. |

| `ELS3-ACTIVITY-REUSABLE-VALUE-READ-UNSUPPORTED` | error, blocks apply | value-flow lowering of a reusable workflow, per property that reads an input, a variable or another activity's result, or carries an Elsa 3 memory reference (D-8) | "The reusable workflow body reads {kind} '{name}', which an Elsa 4 graph activity body cannot reach yet." A bound property: "... reads a variable or another activity's result ..."; a captured output: "Output '{name}' of reusable workflow node '{nodeId}' is captured into a variable, which nothing in an Elsa 4 graph activity body can read yet." | Keep the body to literal values (a capture: remove it), or import as a regular workflow (see above). |

Metadata: `ActivityNodeId`, `ActivityType`, at the node's JSON path. A node already refused for its own reason (for
example an End outside the root flowchart) is not reported twice.
