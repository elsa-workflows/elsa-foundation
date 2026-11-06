# GitHub Copilot agent provider

`Elsa.Agent.GitHubCopilot` is the provider SDK binding from Elsa's
provider-neutral agent contracts to the GitHub Copilot SDK. It registers provider
ID `github-copilot` and keeps all `GitHub.Copilot.SDK` references isolated in
this package.

Studio and participating module code must continue to call only the
provider-neutral `/_elsa/agent/*` endpoints. Do not add Copilot SDK DTOs,
session objects, tokens, tool calls, or model-specific fields to Studio-facing
contracts.

## Configuration

The provider is registered by the `GitHubCopilotAgent` feature, but it is
disabled until explicitly configured.

```json
{
  "Elsa": {
    "Agent": {
      "GitHubCopilot": {
        "Enabled": true,
        "GitHubTokenEnvironmentVariable": "COPILOT_GITHUB_TOKEN",
        "UseLoggedInUser": false,
        "Model": "auto",
        "BaseDirectory": ".elsa/copilot",
        "WorkingDirectory": "."
      }
    }
  }
}
```

Supported settings:

- `Enabled`: must be `true` before the provider reports available diagnostics.
- `GitHubToken`: backend-owned token value. Prefer environment variables for
  local development and deployments.
- `GitHubTokenEnvironmentVariable`: environment variable to read. Defaults to
  `COPILOT_GITHUB_TOKEN`.
- `UseLoggedInUser`: use the backend machine's stored Copilot login. Useful for
  local development only.
- `RuntimeUrl` and `RuntimeConnectionToken`: connect to an already running
  Copilot runtime instead of spawning the bundled runtime.
- `BaseDirectory`: root for SDK-owned state when Elsa spawns the runtime; a hash of the trusted
  server tenant partitions session directories beneath it. The default is a host temporary
  Elsa directory. Provider diagnostics use a separately tagged directory. The SDK ignores
  this option for `RuntimeUrl`: that runtime's operator must provide its tenant isolation and
  state/credential storage boundaries. Elsa does not persist SDK internals.
- `WorkingDirectory`: process/session working directory for the SDK runtime.
- `Model`, `ReasoningEffort`, `SystemMessage`, `Streaming`: passed to Copilot
  session configuration.
- `AvailableTools` and `ExcludedTools`: filters for the installed policy-guarded Weaver
  tools. The allowlist always contains only currently supplied custom tools; an explicit
  configured allowlist narrows that set. It cannot add an ambient SDK tool. SDK permission
  requests may enter only a current installed, allowlisted custom callback in the same session;
  the callback then passes through Elsa's guarded invoker. All ambient requests are denied.

## Local development

Use one of these backend-owned authentication modes:

1. Set `COPILOT_GITHUB_TOKEN` to a supported GitHub token with Copilot access and
   set `Enabled=true`.
2. Run Copilot CLI login for the backend user and set `UseLoggedInUser=true`.
3. Run a Copilot runtime separately and set `RuntimeUrl`.

The .NET SDK package bundles the Copilot CLI runtime for .NET, so no separate
CLI install is required for the default stdio runtime path. The first build may
download the platform runtime into the project `obj` directory.

## Session lifecycle

The SDK runs in `CopilotClientMode.Empty`, with explicit custom tools and its server defaults
for disabled ambient instructions, skills, plugins, environment, telemetry and memory. This
keeps the SDK's agent loop while the installed modules own all product reads and effects.
Use a runtime supporting this pinned SDK profile when selecting an external runtime.

Elsa `AgentSession.Id` is used as the Copilot SDK session ID. Elsa stores only
provider-neutral session state. Copilot conversation/runtime state remains owned
by the SDK under its configured base directory or external runtime.

## After an interrupted turn

Copilot owns its conversation. Elsa sends only the latest prompt, and Copilot keeps the earlier turns under its own
state directory or runtime. That state is provider-owned, not Elsa's recorded state.

- When a turn is interrupted, for example by a host restart, Elsa records the prompt as Cancelled with the reason
  `interrupted`. Copilot may still hold output or tool calls from that turn in its own history. Elsa never counts them
  as accepted outcomes; only the receipts Elsa recorded are accepted.
- The next turn resumes the Copilot session with the Elsa session ID (ADR 0022). If Copilot cannot resume it, the
  provider creates a new Copilot session with the same ID. For a session with earlier turns, this is an explicit
  outcome: the stream carries a `Progress` event whose payload is an `AgentProviderNotice` with code
  `agent.provider.github_copilot.conversation_reset`, and the orchestrator records it on the turn's audit trail as a
  `ProviderDiagnostic` record with the code and turn ID. Earlier turns stay in Elsa's transcript, but the new Copilot
  conversation does not hold them. A first turn, which has no earlier conversation, is not a reset.

## Streaming

The provider maps SDK text, tool lifecycle and errors to generic stream events. Installed tools execute through Elsa's invoker. Their full typed result (type ID, schema version, JSON payload and any owning-resource receipt) is returned to the SDK and emitted as `ContributedResult` / `ModuleResult`; provider code does not decode resource-specific payloads.

A pending reviewed mutation returns an explicit `executed=false` proposal outcome. Review and browser application occur through the generic API, followed by the owning module's admitted acknowledgement. A receipt continuation contains the completed result, advertises no tools and does not replay the original mutation prompt. The pending review, its receipt and the paused turn are recorded through the session owner's record store. With a durable record store configured, the continuation runs once after a host restart; the default in-memory store lasts only as long as the process.

The fallback host-driven path accepts only generic message, error and tool-call envelopes. Model-authored resource JSON cannot create an applied module result.

Cancellation is passed through to the SDK session stream. The SDK adapter aborts the active Copilot session when the stream cancellation token is cancelled.

## Tool and proposal policy

Only current installed custom tool callbacks pass the SDK permission gate, so Elsa's own
invoker can check current authority and policy. SDK shell, file, network, MCP, memory and
unknown tool permission requests are denied. Mutations must be represented as
Elsa-owned proposals and executed through Elsa approval, revision validation,
permission checks, and audit.

The fallback host-driven `ContinueTurnAsync` path also requires the session service to resolve
the trusted owner. A missing owner never falls back to a shared or diagnostics state directory.

This is deliberate: the current Elsa agent facade has proposal approval
semantics, but it does not yet have a durable provider-neutral pending SDK tool
approval lifecycle. See `docs/adr/0021-copilot-provider-keeps-tool-mutation-elsa-owned.md`.

## Security notes

- Do not hardcode or commit Copilot/GitHub tokens.
- Do not pass tokens from Studio requests.
- Diagnostics report only redacted auth status such as `configured-token`,
  `environment:COPILOT_GITHUB_TOKEN`, `logged-in-user`, or `external-runtime`.
- Context attachments are serialized as prompt material after Elsa collection and
  sanitization. Server filesystem paths are not passed as SDK file attachments.
- Sensitive context content is excluded by default; summaries may still be
  included.

## Known SDK limitations

- Available models are reported only when diagnostics can successfully start and
  ping the SDK runtime.
- SDK tool approval is intentionally not bridged to Studio yet.
- Real SDK smoke testing requires Copilot credentials and is not part of the
  normal unit test suite.
- SDK permission-decision APIs are currently marked by the SDK analyzer as
  subject to change; this package suppresses that analyzer warning locally
  because denying SDK tool execution is required by Elsa's provider boundary.
