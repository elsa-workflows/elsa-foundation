# Provider protocol fixtures

This independently packed fixture replaces only the public client factories used by the real Elsa Copilot and Anthropic adapters. It adds no agent provider, orchestrator, tool registry or resource implementation. Every run is **deterministic provider-protocol evidence**, never live-provider evidence.

Materialize this directory outside the repository. Replace `__PRODUCT_VERSION__`, `__CSHELLS_VERSION__` and `__FIXTURE_VERSION__`, rename `.csproj.in` files to `.csproj` and test `.cs.in` files to `.cs`, then restore/pack against the retained feed. Microsoft.Extensions.AI.Abstractions is explicitly pinned to central's current `10.5.1`. Product dependencies are PackageReferences only. Keep both adapter artifacts and this fixture package in the consumer's baseline. The independently authored resource is not a dependency.

## Exact composition

Discover the fixture assembly through normal public shell assembly discovery, for example `typeof(CopilotProtocolFixtureFeature).Assembly`. Neither feature runs unless selected. Select exactly one of these pairs:

| Profile | Actual provider feature | Fixture feature |
|---|---|---|
| Copilot | `GitHubCopilotAgent` | `AcmeCopilotProtocolFixture` |
| Anthropic | `AnthropicAgent` | `AcmeAnthropicProtocolFixture` |

Each fixture declares a Type dependency on that exact actual provider feature. Do not enable the other provider feature, even with `Enabled=false`: its adapter registration would still conflict. Both fixture features have `CommandFile` and `TraceFile` string properties; bind these to **different absolute paths** inside the driver's private owned evidence directory. The optional `GateFile` defaults to blank; when configured it must be a third distinct absolute private path. The directory must already exist with private access (0700 on Unix). Unix trace files are created/tightened to mode 0600. Paths, configuration and normal host authorization remain driver-owned.

The ordinary provider's readiness checks still run. For Copilot use `Enabled=true`, `RuntimeUrl="http://127.0.0.1:1"`, `UseLoggedInUser=false`, and set `GitHubToken=""`, `GitHubTokenEnvironmentVariable=""`, `RuntimeConnectionToken=""`. For Anthropic use `Enabled=true`, `ApiKey="protocol-fixture-only-not-a-credential"`, `ApiKeyEnvironmentVariable=""`. Empty strings explicitly suppress the default environment-token lookup without depending on null configuration binding. These declared fixture values are not credentials; the substitute factories ignore their authentication parameters and never construct a network client. Never inspect existing user/provider authentication state to configure this fixture.

The public feature registration replaces only `IGitHubCopilotClientFactory` or `IAnthropicChatClientFactory`, after the actual feature registers its services. If composing manually instead of through CShells, call the actual provider's registration first and then the corresponding fixture feature's `ConfigureServices`. The real adapters and engine stay unchanged. For a native-loaded fixture, explicitly share/version-admit the baseline `Elsa.Agent.GitHubCopilot` and `Elsa.Agent.Anthropic` assemblies/packages as well as the common contracts: the public factory interfaces and provider feature Types live in those assemblies. A private copied provider assembly cannot replace the baseline adapter's factory contract.

## Driver command

Atomically replace the private command file before each explicit user request:

```json
{"commandId":"unique-driver-turn-id","toolName":"name-from-installed-descriptors","arguments":{}}
```

These are the only three command fields. `arguments` accepts any JSON object with unique member names; nested payload meanings are never inspected. Hold this file unchanged until that turn's review and completion/refusal settle. Give a new explicit request a new command ID. A replayed user request may intentionally retain an ID for detection: the fixture does **not** globally deduplicate command IDs, sessions or prompts.

Copilot resolves the exact name among the actual supplied `AIFunction`s and invokes that supplied function with the unchanged object arguments. SDK tool-execution completion means function transport returned; it does not claim a resource was applied. Pending approval and the actual opaque result return through the unchanged adapter/engine. On the adapter's tool-free receipt continuation, the fixture requires the actual serialized generic tool result, including tool name/call identity, and never invokes another function.

Anthropic validates that the exact tool name was advertised, emits a generic `FunctionCallContent` using the command ID, and leaves invocation to the real host orchestrator. It completes only after that adapter supplies the matching `FunctionResultContent` after the latest user message. A historical result before a newer user message cannot suppress another request. Completion text contains only a hash of the actual opaque result and makes no domain success assertion.

Private JSONL traces record admitted prompt/context/tool declarations, calls, returned opaque envelopes and continuations. Factory credential parameters are never recorded. Credential-shaped JSON fields, bearer values and API-key-shaped text are redacted defensively; arbitrary resource field names are not filtered. The driver must separately prove admission/redaction against native owner observations and must not treat trace redaction as evidence that the upstream payload omitted a canary. Never publish these raw traces. Each record carries `evidenceKind="deterministic-provider-protocol"`.

## Optional pre-call cancellation gate

The separate `GateFile` supports a bounded observation point before the supplied Copilot function is invoked or an Anthropic function call is yielded. It does not change the three-field command file or interpret tool arguments. Initialize a valid nonmatching record before launching a profile that configures this file; a configured missing or malformed file fails closed.

```json
{"commandId":"the-explicit-driver-command","epoch":"unique-observation-epoch","state":"hold","timeoutMilliseconds":60000}
```

Only `commandId`, `epoch`, `state` and optional `timeoutMilliseconds` are accepted. State is `hold` or `release`; the timeout defaults to 60 seconds and must be within 1..60000 milliseconds. A different command ID proceeds normally. A matching hold records `protocol.gate.waiting` with provider, command ID, epoch and timeout, then waits asynchronously. Atomically changing only the state to `release` for the same command/epoch records `protocol.gate.released` and permits the real call boundary. Changing identity while a call waits is a fixture error; rewriting the timeout does not extend the captured deadline.

For native cancellation evidence, wait for the actual native started/turn ID and this gate's matching waiting trace, then call the native cancellation endpoint. The real passed cancellation token interrupts the wait, records `protocol.gate.cancelled` and propagates cancellation before any `copilot.call`/`anthropic.call` or function dispatch. There is no file action that fabricates cancellation. An unreleased deadline is a fixture error (`protocol.gate.timedOut`), not a provider outcome or native cancellation claim. Leave the file valid afterward; subsequent distinct commands are unaffected. The driver must separately verify the cancellation response/event and unchanged native owner state.

## Cross-domain contributions

This package contributes no Agent context/capability/tool source or fan-in handler. Its two explicitly selected shell features replace the public provider-client factories in `Elsa.Agent.GitHubCopilot` and `Elsa.Agent.Anthropic`; the actual adapters continue to supply the provider and tool-policy integration.

## Contract checks

The optional external `Tests/` package consumer exercises arbitrary nested arguments through supplied Copilot functions, declaration-only Anthropic calls and actual opaque continuations, cross-turn/session replay visibility, unavailable-tool refusal, malformed input and private trace credential omission. It references the packed fixture by PackageReference, never this source project. Run it only after the retained fixture package exists. These checks do not establish real-host composition, approval/effect handling or live-provider success; the full witness supplies those observations. No fixture build or test run is claimed by this source template.
