# GitHub Copilot agent provider

`Elsa.Agent.GitHubCopilot` is the provider SDK binding from Elsa's
provider-neutral agent contracts to the GitHub Copilot SDK. It registers provider
ID `github-copilot` and keeps all `GitHub.Copilot.SDK` references isolated in
this package.

Studio and workflow-authoring code must continue to call only the
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
- `BaseDirectory`: SDK-owned Copilot state directory. Elsa does not persist SDK
  internals.
- `WorkingDirectory`: process/session working directory for the SDK runtime.
- `Model`, `ReasoningEffort`, `SystemMessage`, `Streaming`: passed to Copilot
  session configuration.
- `AvailableTools` and `ExcludedTools`: SDK tool filters. Mutating built-in tool
  names are excluded by default, and permission requests are denied by policy.

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

Elsa `AgentSession.Id` is used as the Copilot SDK session ID. Elsa stores only
provider-neutral session state. Copilot conversation/runtime state remains owned
by the SDK under its configured base directory or external runtime.

## Streaming

The provider maps SDK session events to provider-neutral stream events:

- session start -> `Started`
- assistant message delta -> `MessageDelta`
- session idle -> `Completed`
- SDK exceptions or session errors -> `Error`

Cancellation is passed through to the SDK session stream. The SDK adapter aborts
the active Copilot session when the stream cancellation token is cancelled.

## Tool and proposal policy

Copilot SDK permission requests are denied by this provider. Workflow, file,
package, runtime, and external-service mutations must be represented as
Elsa-owned proposals and executed through Elsa approval, revision validation,
permission checks, and audit.

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
