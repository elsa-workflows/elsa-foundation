# Agent Composition Proof

This executable is the isolated native-composition witness for [issue #2913](https://github.com/elsa-workflows/elsa-foundation/issues/2913). It explores candidate collaboration policies over the existing experimental `UnifiedExecution.WorkProof` substrate. It does not integrate with production Elsa APIs, Studio, or a distributed cluster, and it does not claim exact compatibility with Microsoft Agent Framework.

The probe uses deterministic local executors. It makes no LLM or network calls. Its entry and release barriers are durable test gates, not timing benchmarks. `UnifiedExecution.WorkProof` owns work leases, accepted result receipts, and persisted composition state; the candidate policy only decides which work to dispatch next.

Run it from the repository root:

```bash
dotnet restore tools/experiments/AgentCompositionProof/AgentCompositionProof.csproj
dotnet build tools/experiments/AgentCompositionProof/AgentCompositionProof.csproj --no-restore
dotnet run --project tools/experiments/AgentCompositionProof/AgentCompositionProof.csproj --no-build
```

The process prints the evidence report as JSON and writes the latest report to `evidence.json` beside this README, including on failure. The report includes process IDs, assertions, UTC timestamp, runtime, run command, and explicit limits. Temporary SQLite databases are deleted after a successful run and retained on failure for diagnosis.

The scenarios exercise:

- One serialized mailbox per receiver while different receivers can run at the same time.
- A round barrier that buffers next-round messages until all current-round results are accepted. That policy allows at most one active message per receiver in each round; same-receiver queues use the separate mailbox policy.
- A full Sequence wrapper around Researcher, Specialist, Reviewer, one bounded feedback round, a validated approval, an idempotent tool action, and the Sequence's next child.
- Durable fan-in that retains distinct messages from one source, deduplicates repeated delivery of the same `MessageId`, and restores partial buffered state in a fresh OS process.
- Typed private, conversation-shared, and versioned knowledge references, rejecting wrong kinds, missing resources, foreign scopes, wrong owners, and wrong conversations while keeping content separate from control routing.
- Persisted prompt/model/tool configuration references and deterministic usage/budget accounting, with fixed-cost admission before participant, reviewer, and manager steps and accepted decisions recovered without reevaluation.
- Cancellation rejecting late result acceptance, conflicting result/message rejection, stale approval race handling, exact tool invocation binding, and a worker crash after a tool effect followed by an idempotent retry.

The local SQLite store and child processes establish only single-machine semantics for this proof. The sink is an idempotent test double. Usage admission assigns one fixed test unit to each participant, reviewer, and manager step; it does not promise a real model's token or monetary cost. Production broker/file/HTTP/SQL/RabbitMQ behavior, cluster-wide coordination, external credentials, throughput/backpressure, and production cancellation compensation remain unproven. If a live model response is held only in memory and lost before result acceptance, provider-specific retry cost and duplicate-decision behavior are outside this deterministic proof.

## Issue #2913 coverage

The evidence JSON maps to the bounded question in #2913 as follows:

- One policy run wraps the collaboration with `sequence-before` and `sequence-after`; only the before child starts first, and the after child runs after the approved tool result.
- Researcher and Specialist overlap in held child processes. Reviewer work is absent until both result receipts are accepted, and both reviewer envelopes carry the complete round's message IDs.
- The manager decision is a deterministic policy double: it records one follow-up decision, then one tool proposal after the bounded feedback round. A fresh coordinator process reads the accepted decision without evaluating it again.
- Route, round, invocation and message IDs; conversation ID; typed memory and knowledge references; configuration references; usage accounting; tool capability; approval; and idempotency identity are persisted as separate fields.
- Private memory is owner-only, conversation-shared memory is limited to its conversation, and references are versioned read-only snapshots. The proof rejects missing, wrong-kind, foreign-scope, wrong-owner, and wrong-conversation references.
- Approval validates the exact persisted request, tool, arguments and capability. A changed-arguments attempt is rejected; a second stale revision contender loses; an exact approval replay is idempotent.
- The tool crash/retry, late cancellation result, same-MessageId duplicate versus conflicting payload, conflicting result, early reviewer release, usage budgets 0–7, the budget-4 coordinator's drained receipts, and fresh-process fan-in restore have deterministic fixtures.

The recommendation recorded by this witness is to continue with a native composite design and not introduce a separate execution model based on these requirements alone. This is a design recommendation for further evidence, not production acceptance: distributed hosting, production connector contracts, approval identity, external memory writes, cancellation compensation, and independent review remain open.

The related [Microsoft Agent Framework probe](../UnifiedExecution.AgentFrameworkProbe/README.md) tests the released `Microsoft.Agents.AI.Workflows` 1.25.0 package separately. It is conceptual/package-behavior evidence only; this native policy does not invoke that package as a runtime.
