# Contract: Backend Agent API

This contract describes provider-agnostic endpoints owned by Elsa.Server /
elsa-foundation. Studio uses these endpoints to create sessions, stream
assistant responses, review proposals, execute approved actions, and query audit
metadata. External provider calls happen behind these endpoints.

## Authentication and authorization

- All endpoints require an authenticated Studio user unless explicitly marked as
  public health/capability metadata.
- Tenant/environment context is derived from the authenticated request and
  explicit Studio runtime context.
- Every request is evaluated against `AgentPolicy`.
- Mutating endpoints require current permission checks, proposal approval, and
  policy revalidation.

## Endpoint summary

| Method | Path | Purpose |
|--------|------|---------|
| GET | `/_elsa/agent/bootstrap` | Discover assistant availability, policy summary, and provider health |
| POST | `/_elsa/agent/sessions` | Create a new agent session |
| GET | `/_elsa/agent/sessions/{sessionId}` | Load session metadata and recent messages |
| POST | `/_elsa/agent/sessions/{sessionId}/messages` | Submit a user turn and start assistant processing |
| GET | `/_elsa/agent/sessions/{sessionId}/stream` | Stream message deltas, tool events, progress, and proposals |
| POST | `/_elsa/agent/proposals/{proposalId}/approve` | Approve a proposal for execution |
| POST | `/_elsa/agent/proposals/{proposalId}/deny` | Deny a proposal with an optional reason |
| POST | `/_elsa/agent/proposals/{proposalId}/execute` | Execute an approved proposal |
| POST | `/_elsa/agent/feedback` | Record user feedback |
| GET | `/_elsa/agent/audit` | Query administrator-visible audit events |

## GET `/_elsa/agent/bootstrap`

Returns the current user's assistant availability.

All JSON responses are wrapped in `AgentApiResponse<T>`:

```json
{
  "data": {},
  "error": null
}
```

**Response data**

```json
{
  "enabled": true,
  "providerStatus": "available",
  "modes": ["explain", "build", "troubleshoot"],
  "capabilities": [
    {
      "id": "workflow.explain",
      "displayName": "Explain workflow",
      "kind": "answer",
      "risk": "read-only",
      "surfaces": ["workflow-designer"]
    }
  ],
  "policy": {
    "contextVisibility": true,
    "requiresApprovalForMutations": true,
    "retentionLabel": "Configured by administrator"
  }
}
```

## POST `/_elsa/agent/sessions`

Creates a session from the active Studio context.

**Request**

```json
{
  "conversationId": "/workflows/order-approval",
  "providerId": "github-copilot",
  "metadata": {
    "mode": "troubleshoot",
    "route": "/workflows/order-approval",
    "resourceType": "workflow-definition",
    "resourceId": "order-approval",
    "studioVersion": "1.0.0",
    "sdkVersion": "1.0.0",
    "moduleIds": "Elsa.Studio.Workflows"
  }
}
```

**Response data**

```json
{
  "id": "agt_01",
  "status": "active",
  "createdAt": "2026-06-18T01:40:00Z",
  "updatedAt": "2026-06-18T01:40:00Z",
  "metadata": {
    "route": "/workflows/order-approval"
  },
  "policy": {
    "id": "default"
  }
}
```

Studio maps this backend session shape to `AgentCreateSessionResponse`.

## POST `/_elsa/agent/sessions/{sessionId}/messages`

Submits a user turn. The response acknowledges the turn; assistant content is
delivered through the stream.

**Request**

```json
{
  "role": "user",
  "content": "Explain this workflow and identify risky branches.",
  "capabilityId": "workflow.explain",
  "contextAttachments": [
    {
      "id": "ctx_01",
      "kind": "workflow.definition",
      "displayName": "Selected workflow",
      "sensitivity": "internal",
      "summary": "Order approval workflow",
      "references": {
        "source": "workflow",
        "sourceId": "order-approval",
        "scope": "selection"
      }
    }
  ]
}
```

**Response data**

```json
{
  "message": {
    "id": "msg_02"
  },
  "warnings": []
}
```

Studio derives the stream URL as
`/_elsa/agent/sessions/{sessionId}/stream`.

## GET `/_elsa/agent/sessions/{sessionId}/stream`

Streams ordered events for the session. Transport can be implemented with the
same live-update pattern used elsewhere in Studio, as long as the event contract
is preserved.

**Event types**

```json
{ "id": "evt_01", "kind": "Started", "content": null }
{ "id": "evt_02", "kind": "MessageDelta", "content": "This workflow..." }
{ "id": "evt_03", "kind": "ProposalCreated", "proposalId": "prop_01" }
{ "id": "evt_04", "kind": "Completed" }
{ "id": "evt_05", "kind": "Error", "error": { "message": "Provider unavailable. Try again later." } }
```

Studio also tolerates Core Weaver event names such as
`conversation.started`, `assistant.delta`, `tool.result`,
`proposal.created`, and `conversation.error`.

## Proposal approval and execution

### POST `/_elsa/agent/proposals/{proposalId}/approve`

**Request**

```json
{
  "revision": "2",
  "comment": "Looks safe to apply to the draft workflow."
}
```

**Response**

```json
{
  "id": "prop_01",
  "status": "approved",
  "approvedAt": "2026-06-18T01:45:00Z"
}
```

### POST `/_elsa/agent/proposals/{proposalId}/execute`

**Request**

```json
{
  "revision": "2"
}
```

**Response**

```json
{
  "proposalId": "prop_01",
  "executed": true,
  "message": "Added validation branch to the draft workflow."
}
```

## Error model

Errors use a user-safe problem shape.

```json
{
  "title": "Agent capability unavailable",
  "detail": "Workflow proposals are disabled by policy.",
  "status": 403,
  "code": "agent.policyDenied"
}
```

## Contract guarantees

- Provider-specific errors are normalized before reaching Studio.
- Secrets and raw provider credentials are never returned.
- Policy-denied context is omitted and represented through diagnostics.
- Every proposal approval, denial, and execution produces an audit event.
