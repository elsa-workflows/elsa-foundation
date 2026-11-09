# Workflow contribution to Weaver

This optional server module owns workflow context admission, installed guidance, tool validation, permission requirements, review payloads and completion evidence. It specializes the generic Agent feature for composition while provider adapters depend only on Agent contracts.

The accepted M1 subset reads a bounded snapshot of an open workflow and prepares one reviewed designer position change. For explanation and troubleshooting, the snapshot also carries the designer's own validation diagnostics, graph activities by definition and version, and the selected activity's declared properties: expression types and bounded content, credential and sensitive values only as withheld, and Secret References by name, type and scope. Expression interpretation, semantic diagnostics and edits are unconditionally unavailable; routing them to the owning Expression Module is deferred to [#3711](https://github.com/elsa-workflows/elsa-foundation/issues/3711). See [Explain and troubleshoot](EXTENSION_POINTS.md#explain-and-troubleshoot-spec-1193-slice-d). `workflow_set_designer_position` contract `1` takes `{ activityId, x, y }`. It targets the exact definition/revision and browser generation, requires `workflows/design:manage`, and always requires explicit review. A server execution request cannot apply this browser operation.

`workflow_apply_operation_batch` contract `1` prepares one reviewed Operation Batch for the same exact working draft: add catalog activities into an explicit scope path and slot, set the root, move, set declared inputs (a literal or a visible Secret Reference, never a credential literal), update the display name, description or start setting, remove, connect and disconnect. Every operation is strict, every reference is resolved before review, risk is labelled per operation, and the batch is reviewed in every autonomy mode. A missing, hidden or ambiguous target is a clarification, never a guess. See [Draft authoring](EXTENSION_POINTS.md#draft-authoring-spec-1193-slice-c).

The owning Studio workflow module applies the approved change through its native atomic edit path, observes the committed working state and returns a guarded undo token. The backend admits only the expected typed observed result and reports a `ClientWorkingState` receipt. It never claims that local state was saved or published. Unsupported context kinds return diagnostics. The activity catalog (`workflow.catalog`) and the actor's permissions on the exact target (`workflow.permissions`) are the owning services' current answers, read again for every turn through `IActivityAuthoringCatalog` and `IWorkflowDefinitionAuthorizer`; no fixed catalog or permission set is presented, and a missing owner is reported as unavailable. See [Truth seams](EXTENSION_POINTS.md#truth-seams-spec-1193).

The workflow library, the new-workflow create dialog and Reusable Workflow Templates have their own context source and tools: reviewed tag, favorite, folder and view changes through the library's own commands with per-target outcomes and operation-key recovery, reviewed changes to the visible list and the open create dialog, and template batches against the template editor. See [Library, create dialog and templates](EXTENSION_POINTS.md#library-create-dialog-and-templates-spec-1193-slice-e).

A persisted workflow version has its own context source and read-only tool: the page names one exact version, the server reads it through `IWorkflowVersionReader` under the owning definition authorizer, and `workflow_read_version` returns its activities by scope path and its own structural comparison with another exact version. Versions are never changed. Executable and Run support lives in the Runtime-side package [`Elsa.Weaver.Workflows.Runtime`](../Runtime/README.md). See [Versions](EXTENSION_POINTS.md#versions-spec-1193-slice-f).

## Cross-domain contributions

See [the module extension-point catalog](EXTENSION_POINTS.md) for its `IAgentContextProvider`, `IAgentCapabilityProvider`, `IAgentTool` and `IAgentClientTool` contributions, and [Agent foundation](../../Elsa.Weaver/EXTENSION_POINTS.md) for their generic lifecycle.


Current context and retained results are re-admitted by `workflows.context` under the native
`workflows/design:read` permission. The workflow tools link explicitly to that disclosure source. An
accepted working-state receipt remains a historical effect observation; later permission changes
can withhold its disclosure and never authorize a repeat. See the [disclosure rules](EXTENSION_POINTS.md#current-disclosure-ownership-3439).

Concrete workflow authority also comes from Workflow Design Core's `IWorkflowDefinitionAuthorizer`.
Its native API adapter checks definition existence in the current tenant and the same typed definition,
folder and ancestor decisions as owning reads. The tools contribute their read/manage decision through
`IAgentToolAuthorization`, including reviewed dispatch after approval. An unsaved editor revision
is preserved; reading persisted metadata never replaces the working-state graph.
