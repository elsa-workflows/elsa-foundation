# Research: Connection picker load-failure explanation

**Status**: Source review complete; no implementation or tests run.

## Decisions

### Use one generic explanation for rejected loads

**Decision**: Show a short generic load-failure explanation beside the existing Retry action whenever the Connection metadata request fails.

**Rationale**: At the current source head, the editor catches the rejection and records only a failed state with an empty metadata list. The failure cause is discarded before presentation. A permission-specific message would claim more than the available evidence and would require a wider error contract.

**Alternatives considered**:

- No explanation: preserves the current failure state but leaves authors with a disabled selector and an unexplained Retry button.
- Permission-specific wording: not supported by a verified status in the editor and outside #3102's no-new-error-format boundary.

### Reuse the existing recovery path and stored reference

**Decision**: Keep the existing Retry action and loading/result lifecycle. Add only the missing explanation, preserving the current editor value and its handling of context changes, pending responses, unmount, disabled selection, and unavailable references.

**Rationale**: The user-visible issue is missing guidance, while the existing picker already has a retry trigger and a local loading state. No change to connection selection, value serialization, authorization, or server behavior is required.

### Reuse the current client and browser test seams

**Decision**: Add a focused React DOM/Vitest regression for rejection and retry/recovery, then strengthen `Studio/tests/browser/connection-syntax-layout.spec.ts` for the visible failure and keyboard/browser presentation.

**Rationale**: The Connections client package already uses Vitest with React DOM mounting helpers and has no direct test for `ConnectionPickerEditor`. The existing Studio browser test exercises loading, empty, failed, Retry, and ready states at 320px and 1440px with a metadata-only route fixture; its failed-state assertion currently checks only that Retry is visible. This gives the smallest useful browser seam without a new fixture or test project.

## Source Findings

- `Extensions/Connections/studio/Elsa.Studio.Connections/Client/src/ConnectionPickerEditor.tsx`: the `catch` branch discards the error and enters a failed state; the selector is disabled unless the state is ready; the stored reference remains represented while metadata is absent; Retry increments the existing load attempt.
- `Extensions/Connections/studio/Elsa.Studio.Connections/Client/src/connectionsApi.ts`: the editor calls the existing Connection picker operation and receives the existing Connection metadata response. The editor does not receive a verified failure classification.
- `Extensions/Connections/studio/Elsa.Studio.Connections/Client/src/__tests__/`: current tests cover the API, page, module registration, and create dialog; there is no direct editor component regression yet. `module.test.tsx` verifies registration and does not render the inline picker.
- `Extensions/Connections/studio/Elsa.Studio.Connections/Client/package.json`: the package's focused test runner is `vitest run`.
- `Studio/tests/browser/connection-syntax-layout.spec.ts`: the fixture exercises both 320px and 1440px, metadata-only states, keyboard navigation, and retry. It does not currently assert explanatory failure copy.
- Issue #3102's source-level evidence and unmocked HTTP fixture do not establish that a real composed Workbench permission-denied journey was reproduced.

## Scope Result

No new request/response shape, permission policy, credential read, API, connection lifecycle, feature registration, dependency, or module activation is needed. No `contracts/` artifact is appropriate because no external contract changes. The plan's small `data-model.md` records that all data shapes are existing and unchanged, as required by the planning workflow.
