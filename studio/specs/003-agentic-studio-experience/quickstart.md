# Quickstart: Validate Elsa Studio Weaver Experience

This guide validates the workflow-first agentic experience once implementation
tasks exist. It describes expected checks without prescribing implementation
details.

## Prerequisites

- Studio can run with the shared UI system and auth direction from existing
  specs.
- Elsa.Server / elsa-foundation exposes the provider-agnostic agent API contract
  from [contracts/agent-backend-api.md](./contracts/agent-backend-api.md).
- A workflow module contributes the workflow agent contract from
  [contracts/workflow-agent-contract.md](./contracts/workflow-agent-contract.md).
- Test provider settings are configured in the backend; provider secrets are not
  available to the browser.

## Static validation

1. Confirm the Studio SDK exposes agent contribution registries described in
   [contracts/studio-agent-sdk.md](./contracts/studio-agent-sdk.md).
2. Confirm module-contributed context providers, prompt starters, capabilities,
   and actions have stable IDs and risk/permission metadata.
3. Confirm mutating workflow actions are represented as proposals, not direct UI
   mutations.
4. Confirm Weaver disabled/unavailable state leaves Studio navigation and normal
   workflow editing available.

Studio validation command:

```bash
pnpm --filter @elsa-workflows/studio-web test
pnpm --filter @elsa-workflows/studio-web build
```

Current Studio MVP coverage includes the Weaver launcher/panel, agent client
and stream handling, workflow context, prompt starters, proposal review, context
redaction indicators, disabled/unavailable presentation, feedback controls, and
route/module/permission/policy-based contribution filtering.

## Backend contract validation

1. Request `GET /_elsa/agent/bootstrap` as an authorized Studio user.
2. Verify the response reports availability, provider health, policy summary,
   and workflow capabilities.
3. Create a session with `POST /_elsa/agent/sessions` using an active workflow
   surface.
4. Submit a message with `POST /_elsa/agent/sessions/{sessionId}/messages`.
5. Subscribe to the session stream and verify ordered message/progress/proposal
   events.
6. Approve, deny, and execute proposals using the proposal endpoints.
7. Query audit events and verify session, context, approval/denial, execution,
   and feedback records are visible to an administrator.

## Studio workflow MVP validation

1. Start Studio and open a workflow definition.
2. Open Weaver from the shell using mouse and keyboard.
3. Verify focus moves to Weaver and closing restores focus.
4. Verify context chips identify the active workflow, selected item, and
   diagnostics used by the assistant.
5. Ask: "What does this workflow do?"
6. Verify the answer explains the workflow in business terms and does not create
   a proposal.
7. Ask: "How should I handle stalled approvals?"
8. Verify the assistant can produce a reviewable workflow-change proposal with
   impact, operations, risk, and rollback notes.
9. Deny the proposal and verify the workflow is unchanged and an audit event is
   recorded.
10. Recreate or reopen a proposal, approve it, execute it, and verify execution
    rechecks permissions and records the result.

## Failure-state validation

- Disable agent features by policy and verify the assistant reports disabled
  state while Studio remains usable.
- Simulate provider unavailability and verify a user-safe error appears.
- Remove the required workflow permission and verify workflow proposal actions
  are hidden or denied.
- Change the workflow after proposal creation and verify execution is blocked by
  revision mismatch.
- Include sensitive setting values in context sources and verify they are
  redacted before display or provider use.

## Expected outcomes

- Users can open Weaver from any Studio screen in under 5 seconds.
- Workflow explanation uses active workflow context without manual copy/paste.
- Mutating workflow changes always require explicit review and approval.
- Agent unavailability does not block non-agent Studio workflows.
- Administrator-visible audit records exist for sessions, context decisions,
  approvals, denials, executions, failures, and feedback.
