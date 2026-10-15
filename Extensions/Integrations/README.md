# Elsa Integrations

Optional Elsa Integrations modules. Elsa 4 only.

A connector is one NuGet package. [#2726](https://github.com/elsa-workflows/elsa-foundation/issues/2726)
forbids a second activity catalog: no hand-authored `connector.json` for activity metadata, no
parallel secret store, no Elsa 3 shims. HTTP uses the foundation `http` connection and
`SendHttpRequest` (connections plan D1).

## Layout

```
Extensions/Integrations/
  src/Sdk/                         STUB of Elsa.Integrations.Sdk (#2726)
  src/Connectors/OAuth2/           Generic OAuth2 scheme (#2727)
  tests/Connectors/OAuth2/Tests/   Harness tests against a mock OAuth server
```

## SDK stub (#2726)

Checked 2026-10-13: #2726 has no branch or PR. The stub follows that issue's revised contract:

| Rule | How the stub follows it |
|---|---|
| No duplicate manifest | No `connector.json` catalog; no runtime manifest reader |
| No shims (ADR 0045) | No Elsa 3 forwarding types; no parallel `ISecretStore` |
| Secrets | `ISecretValueResolver` + `IRuntimeSecretMask`; writes via CAS `IGrantSecretWriter` |
| HTTP client | Named `Elsa.Activities.Http` client + `HttpActivityClientLogger` |
| Design/runtime split | Scheme settings are data on the connection record; the runtime does not load a manifest to execute |

`IntegrationsSdkStub.IsStub` is `true` so a later swap can be grepped.

### Swap when #2726 lands

1. Delete `Extensions/Integrations/src/Sdk`.
2. Point the connector at the real `Elsa.Integrations.Sdk` package.
3. Keep `ConnectorActivity`, `IConnectionContext`, and `IConnectionHandle` unless the real SDK chose different names.

## Connections phase 1 blocker

[#2727](https://github.com/elsa-workflows/elsa-foundation/issues/2727) is blocked on foundation
connections phase 1 (`http` connection, D1). No tracking issue exists yet. The harness binds the
OAuth2 scheme to a stub http connection until `SendHttpRequest` grows `[ActivityConnection]` and
drops the interim `Authorization` input.

## Callback and refresh

- Callback path: `/_elsa/connections/oauth/callback`, built from `PublicBaseUrl` (D2)
- Refresh: compare-and-swap on the secret version; lock is an optimization
- Grants: `RequiresWritableStore`; read-only stores are refused
