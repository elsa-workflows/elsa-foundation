# Agent Framework integration research

Executable spike for [#3529](https://github.com/elsa-workflows/elsa-foundation/issues/3529),
under [Elsa Agents #3519](https://github.com/elsa-workflows/elsa-foundation/issues/3519).
This is a research fixture, not an installed agent module or an Elsa runtime adapter.

```bash
bash tools/agents/research/run.sh
```

Requires the repository's .NET 10 SDK and access to NuGet for the locked packages. The runner creates
and removes an owned temporary project so the experiment cannot change Foundation's central package
pins, solution filters or production composition. The fixture makes no model-provider network calls,
requires no API key and incurs no inference charge. Package restore can access NuGet.
The checked-in lock fixtures use a `.template` suffix; the runner materializes `packages.lock.json`
only beside its temporary project. This preserves the repository's solution-lock ownership guard.

The project pins Microsoft.Agents.AI and Microsoft.Agents.AI.Workflows 1.27.0 and their complete
transitive lock. Source provenance is
[`dotnet-1.27.0`, commit `4d1674b78951d6fd127ada532660514637f906e9`](https://github.com/microsoft/agent-framework/tree/4d1674b78951d6fd127ada532660514637f906e9).
The package declares the MIT license. The dependency graph needs Microsoft.Extensions.AI.Abstractions
10.10.1; Foundation currently pins 10.5.1. Production compatibility/upgrade remains a design gate.

The two JSON results name each actual SDK observation, including a second process restoring the
session persisted by the first. They explicitly distinguish a deterministic model
client from a live provider and do not claim Elsa publication, durable effects or process integration.
Schema forwarding is not proof of model schema enforcement; the malformed-result control demonstrates
that raw agent output still needs Elsa validation. Session serialization is not approval authority,
tenant/version ownership or transactional effect recovery. The bounded team probe executes two
fixture-backed agents through the real workflow SDK and collects their messages at a fan-in barrier;
it does not integrate that workflow runtime into Elsa or prove durable team effects.

The continuity negative control deliberately drops the restored session. It must exit nonzero with
`Probe failed: restored session sends previous user message`:

```bash
bash tools/agents/research/run.sh --drop-continuity
```

The schema negative control substitutes an unconstrained object schema. It must fail at
`dynamic schema reaches model client`; the normal assertion compares the complete schema and name,
including required members and `additionalProperties`:

```bash
bash tools/agents/research/run.sh --replace-schema
```

For the bounded source-project compatibility probe:

```bash
bash tools/agents/research/run.sh --foundation-compatibility
```

This mode builds the current Agent, Anthropic, GitHub Copilot and Events projects in their normal
source locations and activates separate real CShells compositions alongside the pinned MAF package.
It resolves Agent services and constructs the existing Anthropic `IChatClient` with an unused fixture
credential; it makes no provider request. It requires Python 3 for XML path substitution. Its own lock
must match the current source dependency graph. It is not a packaged/custom-host or live-provider
qualification, and it does not modify Foundation's package pins.

Full research findings and the remaining integration questions belong to
[the research report](../../../docs/reports/elsa-agents-research.md).
