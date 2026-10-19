# Contract: diagnostic codes added by spec 246

Defined in `ReusableActivityImportDiagnosticCodes`
(`Extensions/Elsa3/src/Activities/Design/Import/Elsa3.Activities.Design.Import/Models/ReusableActivityImportPlan.cs`).
Appended at the end of the class, a file #2717 (spec 247) may also touch. The codes are stable once merged.

| Code | Severity | Raised by | Message (shape) | Guidance |
|---|---|---|---|---|
| `ELS3-REUSABLE-UNSUPPORTED-START-TRIGGER` | error, blocks apply | analyzer, per reusable source with a `CanStartWorkflow` node that owns no nested behaviour | "Reusable workflow '{definitionId}' v{version} starts from trigger '{nodeId}' ({type}). Elsa 4 cannot start a reusable activity from a trigger." | "Move the trigger into its own Elsa 3 workflow that calls the reusable workflow, then re-export the collection." |
| `ELS3-REUSABLE-PUBLISHING-UNAVAILABLE` | error, blocks apply | importer, before commit, when the selection holds a reusable version and no `IReusableActivityImportPublisher` is registered | "This host cannot publish imported reusable activities." | "Enable the `Elsa3ImportActivitiesPublishing` feature (it needs `WorkflowsPublishingApi`)." |

Metadata:
- `UNSUPPORTED-START-TRIGGER` carries `nodeId` and `activityType`, at the node's JSON path.
- `PUBLISHING-UNAVAILABLE` has no node.

Unchanged: `ELS3-REUSABLE-UNSUPPORTED-TRIGGER` (a trigger owning nested behaviour) keeps its meaning. A node that matches
it gets only that code, not both.

Publisher refusals during the publish phase are **not** wrapped in new codes. Each `ReusableActivityPublicationState`
carries the publisher's own code (`activity.*`) and diagnostics. The exception is a version id held by a publication of
a different version, which reuses `ELS3-REUSABLE-IDENTITY-COLLISION`.
