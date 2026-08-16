# Wave 4 Agent API Evidence Model

## Endpoint contract observation

- route template and method;
- request route/query/header/body binding;
- status, headers, content type, and JSON body;
- consumed OpenAPI operation identifier, tags, parameters, and responses;
- permission action, implication, wildcard behavior, resource, and tenant disposition;
- SSE framing, cancellation, completion, and disposal behavior where applicable.

## Agent permission contribution

| Action | Owner | Implies | Routes |
| --- | --- | --- | --- |
| `agent.use` | `Elsa.Agent.Api` | none | bootstrap, sessions, messages, cancel, feedback, stream |
| `agent.proposals` | `Elsa.Agent.Api` | `agent.use` | approve, deny, execute |
| `agent.audit` | `Elsa.Agent.Api` | none | audit |

The evaluator may accept the administrative wildcard, but endpoint metadata records only the
catalog-owned action.

## Lifecycle evidence

Each of three cycles publishes the real eleven-route mapper through an isolated collectible owner
assembly, resolves DI/auth/serializer state, clears route data sources, disposes services, unloads
the context, and checks weak references. SSE cancellation separately asserts enumerator cleanup.
