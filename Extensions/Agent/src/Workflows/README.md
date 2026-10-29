# Workflow contribution to Weaver

This optional server module owns workflow context admission, installed guidance, tool validation, permission requirements, review payloads and completion evidence. It specializes the generic Agent feature for composition while provider adapters depend only on Agent contracts.

The current candidate reads a value-free snapshot of an open workflow and prepares one reviewed designer position change. `workflow_set_designer_position` contract `1` takes `{ activityId, x, y }`. It targets the exact definition/revision and browser generation, requires `workflows/design:manage`, and always requires explicit review. A server execution request cannot apply this browser operation.

The owning Studio workflow module applies the approved change through its native atomic edit path, observes the committed working state and returns a guarded undo token. The backend admits only the expected typed observed result and reports a `ClientWorkingState` receipt. It never claims that local state was saved or published. Unsupported graph authoring and context kinds return diagnostics; no fixed activity catalog is presented as live discovery.

## Cross-domain contributions

See [the module extension-point catalog](EXTENSION_POINTS.md) for its `IAgentContextProvider`, `IAgentCapabilityProvider`, `IAgentTool` and `IAgentClientTool` contributions, and [Agent foundation](../Agent/EXTENSION_POINTS.md) for their generic lifecycle.
