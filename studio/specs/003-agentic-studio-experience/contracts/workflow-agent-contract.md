# Contract: Workflow Agent Proof

The workflow-first MVP proves the agent platform through explanation,
troubleshooting, and reviewable workflow-change proposals.

## Workflow context attachment

Workflow context providers produce bounded attachments for the active workflow.

```json
{
  "source": "workflow",
  "sourceId": "order-approval",
  "label": "Order approval workflow",
  "contentType": "workflow-definition",
  "sensitivity": "internal",
  "scope": "selection",
  "content": {
    "workflowId": "order-approval",
    "version": "draft",
    "selectedActivityId": "send-email",
    "summary": "Draft order approval workflow with email notification branch.",
    "activities": [
      {
        "id": "send-email",
        "type": "Email",
        "displayName": "Send approval email"
      }
    ],
    "connections": [],
    "diagnostics": [
      {
        "severity": "warning",
        "message": "No timeout branch is configured."
      }
    ]
  }
}
```

## Workflow explanation capability

**Capability ID**: `workflow.explain`

**Risk**: `read-only`

**Inputs**

- Active workflow attachment.
- Optional selected activity/connection.
- Optional diagnostics attachment.

**Expected assistant output**

- Business-language workflow summary.
- Key activities and branches.
- Risks, missing decisions, or unclear paths.
- Source/context chips for used attachments.

## Workflow troubleshooting capability

**Capability ID**: `workflow.troubleshoot`

**Risk**: `read-only`

**Inputs**

- Active workflow attachment.
- Validation diagnostics.
- Runtime/log diagnostics when available.

**Expected assistant output**

- Prioritized issue list.
- Affected activities/connections.
- Suggested fixes.
- Confidence/uncertainty notes where context is incomplete.

## Workflow change proposal capability

**Capability ID**: `workflow.propose-change`

**Risk**: `review-required`

**Proposal shape**

```json
{
  "proposalType": "workflow-change",
  "workflowId": "order-approval",
  "baseRevision": "draft-17",
  "title": "Add timeout branch for approval email",
  "summary": "Adds a timeout path so stalled approvals can be escalated.",
  "operations": [
    {
      "op": "add-activity",
      "activity": {
        "id": "timeout",
        "type": "Timer",
        "displayName": "Approval timeout"
      }
    },
    {
      "op": "add-connection",
      "from": "send-email",
      "to": "timeout",
      "outcome": "Timeout"
    }
  ],
  "risks": [
    "Changes draft workflow behavior after approval timeout."
  ],
  "rollback": "Reject proposal or restore the previous draft revision."
}
```

## Review requirements

- Proposal shows base revision and operations before approval.
- Proposal cannot execute if the workflow changed since `baseRevision`.
- Proposal execution rechecks user permissions.
- Proposal execution produces an audit event and user-visible result.
- Failed execution leaves the workflow unchanged or reports partial-change
  recovery instructions.

## MVP acceptance checks

- Opening assistant on a workflow route includes workflow context chips.
- Asking "what does this workflow do?" produces an explanation with no mutation.
- Asking "how can I handle stalled approvals?" can produce a proposal but does
  not apply it automatically.
- Denying a proposal records the decision.
- Approving and executing a proposal records the decision and result.
