# Provider protocol research

Executable research for [#3592](https://github.com/elsa-workflows/elsa-foundation/issues/3592),
under [Elsa Agents #3519](https://github.com/elsa-workflows/elsa-foundation/issues/3519).
This prepares spec 1179 T016–T017. It neither installs a production provider nor completes M3.

```bash
bash tools/agents/research/providers/run.sh
```

Requires .NET 10 (the repository's `global.json`), Python 3 and NuGet access. The runner materializes
an isolated project from the templates, compares a normal restore with the complete committed lock,
performs a locked restore, builds and runs the probe. It prints and retains its owned temporary
directory, including restore/build logs, `report.json`, `evidence.json`, the resolved dependency graph
and fixture hashes. `--output /absolute/new/directory` chooses an external destination; existing
directories and destinations within this checkout are refused. Local MSBuild/package import barriers
prevent unrelated parent settings from entering the experiment. Templates keep the research graph outside Foundation's central pins, solution and lock
ownership. No production assembly or source-project reference is involved.

All SDK clients use explicit fixture credentials and an injected HTTP handler restricted to
`provider.fixture.invalid`. There is no socket handler, real credential resolution or model-provider
network request. Package restore can access NuGet. No inference charge is incurred. Reports contain
content type names, counts and fixture observations; they do not retain request headers, raw SDK
sessions or reasoning text. The synthetic reasoning marker exists only in the source and process.

## Pinned profile

| Package | Version | Purpose |
|---|---|---|
| Microsoft.Agents.AI | 1.27.0 | Actual `ChatClientAgent` execution and session serialization |
| Microsoft.Extensions.AI.OpenAI | 10.10.1 | MEAI adapter for Azure chat completions |
| Microsoft.Extensions.AI.Abstractions | 10.10.1, transitive | Admitted shared abstraction |
| Anthropic | 12.32.0 | Existing Foundation provider SDK boundary |
| Azure.AI.OpenAI | 2.1.0 | Stable Azure client; bounded compatibility experiment |
| OpenAI | 2.14.0, transitive | Selected by the MEAI adapter |
| System.ClientModel | 1.15.0, transitive | Transport and retry policy |

The lock records the full graph, including Azure.Core 1.44.1. The executable proves this specific
stable-Azure/newer-OpenAI combination for the tested chat-completion paths; package minimum versions
alone do not establish compatibility. No central upgrade or broader SDK/API compatibility is implied.

Primary source provenance:

- [MAF 1.27.0 source](https://github.com/microsoft/agent-framework/tree/4d1674b78951d6fd127ada532660514637f906e9).
- [Anthropic 12.32.0 options](https://github.com/anthropics/anthropic-sdk-csharp/blob/38b988d36f4f0a25f4324387573776fac6adcc4b/src/Anthropic/Core/ClientOptions.cs)
  and [request/auth retry path](https://github.com/anthropics/anthropic-sdk-csharp/blob/38b988d36f4f0a25f4324387573776fac6adcc4b/src/Anthropic/AnthropicClient.cs).
- [MEAI 10.10.1 adapter source](https://github.com/dotnet/extensions/blob/59e1741f6fb45cf7ae08e2df088aef71537a7bc4/src/Libraries/Microsoft.Extensions.AI.OpenAI/OpenAIChatClient.cs).
- [Azure 2.1.0 source](https://github.com/Azure/azure-sdk-for-net/tree/d8847b2ab3990363353f69c0c79e692f9180dcc9/sdk/openai/Azure.AI.OpenAI)
  and [OpenAI 2.14.0 retry documentation](https://github.com/openai/openai-dotnet/blob/OpenAI_2.14.0/README.md#automatically-retrying-errors).
- [Microsoft's provider guidance](https://learn.microsoft.com/en-us/agent-framework/agents/providers/azure-openai)
  distinguishes chat completions from Responses. This fixture exercises chat completions only.

## Observed boundaries

Twenty-four cases execute through the actual MAF and provider adapters. The simple supported-object success case verifies exact schema,
model/deployment, temperature and output-token option serialization, visible JSON result, token usage
(11 input, 7 output) and SDK session retention. Sending a schema does not prove model enforcement.

| Case | Anthropic | Azure OpenAI |
|---|---|---|
| Success | 1 transport request | 1 transport request |
| Repeated HTTP 429, defaults | 3 transport requests | 4 transport requests |
| Repeated HTTP 429, retries disabled | 1 (`MaxRetries = 0`) | 1 (`ClientRetryPolicy(0)`) |
| Response loss, retries disabled | 1; SDK throws | 1; SDK throws |
| Already cancelled caller | 1 handler entry; fixture rejects before request | 1 handler entry; fixture rejects before request |
| Cancellation after request observation | 1 request; cancellation propagates | 1 request; cancellation propagates |
| Refusal | `content_filter` finish reason, `TextContent` | `ErrorContent` with `Refusal` code; `stop` finish reason |
| Malformed business text | Returned without schema validation | Returned without schema validation |
| Numeric bounds in request | Moved into a description | Moved into a description |
| Numeric result outside original bounds | Returned without original-contract validation | Returned without original-contract validation |
| Truncated, valid JSON text | `length` finish reason | `length` finish reason |

Counts describe the injected transport boundary, not sockets or external effects. The handler checks
cancellation on entry before recording a request. Thus a cancelled fixture entry is not evidence that
the SDK avoided entering the HTTP stack, and this result does not prove a real provider saw no work.
Elsa must check cancellation and its durable dispatch authority before calling the provider. After a
request is observed, cancellation/response loss cannot establish no effect or authorize a blind replay.
Custom delegating handlers, proxies and authentication flows need their own budget qualification.

Two additional Anthropic cases matter for the production design:

- A response containing thinking plus visible text maps to `TextReasoningContent` and `TextContent`.
  `AgentResponse.Text` excludes reasoning, but raw MAF session serialization retains it. A blanket
  non-text rejection fails otherwise valid output; flattening all content leaks non-business text.
  T017 needs an explicit supported-content disposition and protected continuation codec. The fixture
  does not implement that codec or authorize reasoning persistence.
- A changing `IAccessTokenProvider` plus HTTP 401 makes **two** transport requests despite
  `MaxRetries = 0`: authentication refresh has its own retry path. API-key retries-disabled evidence
  cannot admit this authentication profile under a one-request budget. Guard each dispatch or reject
  that profile until separately qualified. Static fixture tokens never contact an identity service.

SDK transport success does not establish business acceptance. Elsa must inspect refusal/content/finish
signals, validate the complete result schema and enforce policy/usage limits before accepting a turn.
The research fixture records these signals and deliberately accepts no specialist business result.

## Provider schema compatibility

The numeric cases pass a contract requiring an integer between 0 and 100. Both actual adapters move
`minimum`/`maximum` into a field description in the outgoing request; the original schema is unchanged.
The probe asserts the complete vendor-specific serialized projection. Fixtures returning 42 and 101
both return normally through MAF. This observes SDK request/response handling, not a live model's
constraint enforcement. A simple string-schema forwarding test cannot establish verbatim forwarding
of all supported Elsa contracts.

[Anthropic documents this schema transformation](https://platform.claude.com/docs/en/build-with-claude/structured-outputs):
unsupported numeric/string constraints become descriptions, while helpers that validate results use
the original constraints. The tested `IChatClient`/MAF path returns the out-of-range fixture normally.
The [Azure documentation](https://learn.microsoft.com/en-us/azure/foundry/openai/how-to/structured-outputs#json-schema-support-and-limitations)
also lists numeric bounds as unsupported. Its documented profile additionally requires all properties
in `required`, closed objects, at most 100 object properties and five nesting levels. Do not substitute
a different service's limits for this Azure profile.

M3 must make the request projection explicit and bind it to the actual model/API/SDK capability
profile. Elsa's unchanged full contract remains the result-acceptance authority. Never interpret
constraints moved into descriptions as provider-enforced checks, silently widen accepted output,
coerce an invalid result into success or add an unbudgeted repair call. Any projection whose acceptance
semantics cannot be preserved needs an explicit unsupported disposition. Full projection, capability
and original-schema validation tests belong to T016/T017; this fixture supplies no production lowerer.

## Causal controls

Each control must exit **1**, with `status: Failed` and the named assertion in `report.json`:

```bash
bash tools/agents/research/providers/run.sh --enable-retries
# anthropic: disabled retries make one transport request
bash tools/agents/research/providers/run.sh --drop-schema
# anthropic: structured schema is present
bash tools/agents/research/providers/run.sh --drop-cancellation
# anthropic: cancellation propagates
bash tools/agents/research/providers/run.sh --flatten-reasoning
# private reasoning is not business result text
```

The controls alter actual SDK options, cancellation propagation or the result projection. They do not
replace the vendor clients with fake chat clients. A failed restore/build is an infrastructure failure,
not a successful negative control; its logs remain in the retained directory. If the probe host exits
before writing a report, the runner prints its log and records a missing report with the nonzero exit.
Selecting more than one provider profile is refused.
Repeat the first three controls with `--provider=azure` to exercise Azure's guards independently;
the expected failure prefix is `azure:`. `--provider=anthropic` explicitly selects the Anthropic profile.
Reasoning flattening applies only to the Anthropic thinking fixture.

No live model, streaming, Responses API, hosted tool, model capability inventory, credential rotation,
Elsa journal, restart/replay, production session codec or packaged/custom host is qualified here.
Those remain M3 and later acceptance obligations in the [spec research](../../../../specs/1179-specialist-agents/research.md).
