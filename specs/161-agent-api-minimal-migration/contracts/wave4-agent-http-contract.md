# Wave 4 Agent HTTP Contract

The immutable FastEndpoints-before observations live in:

- `tests/Elsa/Architecture/Baselines/wave4-agent-http-fastendpoints.json`
- `tests/Elsa/Architecture/Baselines/wave4-agent-openapi-fastendpoints.json`

The compatibility test compares exactly these eleven operations with an empty approval set:

| Operation | Route | Permission |
| --- | --- | --- |
| Bootstrap | `POST /_elsa/agent/bootstrap` | `agent.use` |
| CreateSession | `POST /_elsa/agent/sessions` | `agent.use` |
| GetSession | `GET /_elsa/agent/sessions/{sessionId}` | `agent.use` |
| PostMessage | `POST /_elsa/agent/sessions/{sessionId}/messages` | `agent.use` |
| CancelTurn | `POST /_elsa/agent/sessions/{sessionId}/turns/{turnId}/cancel` | `agent.use` |
| StreamSession | `GET /_elsa/agent/sessions/{sessionId}/stream` | `agent.use` |
| Feedback | `POST /_elsa/agent/sessions/{sessionId}/feedback` | `agent.use` |
| ApproveProposal | `POST /_elsa/agent/sessions/{sessionId}/proposals/{proposalId}/approve` | `agent.proposals` |
| DenyProposal | `POST /_elsa/agent/sessions/{sessionId}/proposals/{proposalId}/deny` | `agent.proposals` |
| ExecuteProposal | `POST /_elsa/agent/sessions/{sessionId}/proposals/{proposalId}/execute` | `agent.proposals` |
| Audit | `GET /_elsa/agent/audit` | `agent.audit` |

The SSE fixture is authoritative for event framing and headers. It contains no heartbeat or resume
semantics; adding either requires a separately reviewed contract and client test matrix.
