# Wave 4 Agent REST and SSE API migration

Status: implementation evidence complete for the bounded Agent slice; control-room integration
gates are still required.

This report records issue [#1370](https://github.com/elsa-workflows/elsa-foundation/issues/1370).
The scope is exactly the eleven concrete `Elsa.Agent.Api` FastEndpoints registrations. It does not
migrate other Elsa modules, redesign public routes, replace HTTP/JSON, or add an SSE heartbeat or
resume protocol that was not present in the consumed baseline.

## Decision

**Recommendation: migrate Agent to explicit Minimal API mappings and retain temporary mixed-host
coexistence.** The real before/after evidence and focused gates support Minimal APIs as the Agent
authoring model while FastEndpoints remains available to unrelated migration waves.

## Contract baseline and implementation

The immutable FastEndpoints-before evidence was committed separately in
`9293cb029` (`test: freeze wave 4 Agent FastEndpoints baseline`) before production endpoint changes.
It contains eleven HTTP observations and the consumed OpenAPI document for:

| Operation | Route family | Permission metadata |
| --- | --- | --- |
| Bootstrap | `GET /_elsa/agent/bootstrap` | `agent.use` |
| Create/Get session | `POST/GET /_elsa/agent/sessions...` | `agent.use` |
| Post/cancel turn | `POST /_elsa/agent/sessions/{sessionId}/...` | `agent.use` |
| Stream session | `GET /_elsa/agent/sessions/{sessionId}/stream` | `agent.use` |
| Feedback | `POST /_elsa/agent/feedback` | `agent.use` |
| Approve/deny/execute proposal | `POST /_elsa/agent/proposals/...` | `agent.proposals` |
| Audit | `GET /_elsa/agent/audit` | `agent.audit` |

The implementation adds `AgentApi.MapAgentApi`, an `IWebShellFeature` adapter, an Agent-owned
permission contributor, and owner-local source-generated response/SSE contexts. The eleven old
endpoint classes and the production `Elsa.Api.FastEndpoints` reference are removed. Operation
names, routes, response metadata, and tags are explicit rather than discovered.

## Evidence matrix

| Gate | Evidence | Result |
| --- | --- | --- |
| HTTP parity | `Wave4AgentMinimalApiCompatibilityTests`, 11 before/after HTTP cases | Pass; comparer has no approvals |
| OpenAPI parity | Same test, 11 Agent operations projected from a host that also maps an FE canary | Pass; no unapproved differences |
| Authorization | `Wave4AgentAuthorizationTests` | Pass: 401/403, exact, implied, wildcard, resource, tenant, and mixed FE/Minimal cases |
| SSE wire contract | `Wave4AgentSseLifecycleTests` | Pass: `text/event-stream`, no-cache, anti-buffering, two newline-terminated data frames |
| SSE write pacing | `Wave4AgentSseLifecycleTests.Every_sse_event_flushes_the_response_body_before_the_next_event` | Pass: each event awaits a response-body flush |
| SSE authorization error | `Wave4AgentSseErrorCompatibilityTests` and `wave4-agent-sse-error-fastendpoints.json` | Pass: exact error payload plus generated ID and `UtcNow` timestamp semantics |
| Binding parity | `Wave4AgentBindingCompatibilityTests` and `wave4-agent-binding-fastendpoints.json` | Pass: empty/malformed JSON and invalid `take` preserve FastEndpoints 400 ProblemDetails status, media type, and body |
| Authentication/provider execution | `Wave4AgentCollectibilityTests` | Pass: the configured authentication scheme establishes the principal, then bootstrap/session/provider delegates execute through the published routes |
| SSE cancellation/disposal | Tracking async enumerator test | Pass: cancellation reaches the enumerator finally path |
| Collectibility | `Wave4AgentCollectibilityTests`, 3 real route-publication cycles | Pass: mapped binder, typed serializer, provider/auth delegates, completed and cancelled SSE, DI provider, route endpoints, and generated JSON context execute and collect |
| Mixed coexistence | FE canary mapped with Agent Minimal API in one TestServer | Pass |
| Transition ratchet | `FastEndpointsTransitionTests` and baseline JSON | Pass: 156 → 145 registrations; 11 Agent entries removed |

The SSE baseline contains no heartbeat, resume token, or separate backpressure protocol. The test
therefore preserves existing framing and awaited streaming/cancellation cleanup and explicitly does
not claim those absent semantics. A future heartbeat/resume design requires a reviewed contract and
client matrix.

## Authorization disposition

`Elsa.Agent.Api` contributes three catalog-owned actions:

- `agent.use` for session, message, cancellation, feedback, and stream operations;
- `agent.proposals` for approve/deny/execute, implying `agent.use`;
- `agent.audit` for audit reads, with no implication.

Endpoint metadata names only the owning action. The administrative wildcard remains evaluator-level
grant compatibility. Authentication establishes the normalized principal outside the endpoint
handlers; resource and tenant ownership checks remain in the existing Agent authorization services
and fail closed.

## Remaining review items and warnings

- The generated Agent JSON context uses a custom enum converter to retain the existing wire casing;
  the build reports `SYSLIB1034` because the non-generic converter is not AOT-supported. This is an
  advisory warning and should be tracked separately from the migration gate.
- Repository builds retain existing analyzer/nullable/obsolete warnings and the SSH.NET advisory;
  no new failing warning gate was introduced by this slice.
- The collectible test proves real mapper publication, mapped binding, typed serialization, provider
  and authorization service execution, completed/cancelled SSE, DI setup, generated serializer
  metadata, and route release. It intentionally does not claim dynamic OpenAPI document-cache
  unloadability; that remains a broader publication concern.

## Follow-up boundary

1. Review and merge this bounded Agent migration only after the parent control room reruns the full
   architecture, Agent, build, maps, and relevant E2E gates.
2. Apply the same exact-before-fixture, catalog-contributor, source-generation, and lifecycle pattern
   to Wave 5 OpenTelemetry, with protobuf and SSE-specific evidence.
3. Keep dynamic shell collision/atomic publication work under #1345; this Agent migration does not
   implement a second route-generation mechanism.
4. Track AOT enum-converter cleanup and any heartbeat/resume protocol separately rather than hiding
   either behind a compatibility approval.

## Validation commands

```bash
dotnet test tests/Elsa/Agent/Tests/Elsa.Agent.Tests.csproj --no-restore
dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj --no-restore --filter 'FullyQualifiedName~Wave4Agent|FullyQualifiedName~FastEndpointsTransitionTests'
dotnet run --project tools/maps/Elsa.Maps.Generator -- check
dotnet build Elsa.Server.slnx --no-restore
```
