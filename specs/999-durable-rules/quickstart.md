# Durable Rules acceptance quickstart

Section 1 is runnable for the implemented semantics, migrated SQLite and direct host composition. Sections 2–4 remain implementation targets and their commands become runnable only when their owning tasks are delivered. See the [direct-runtime checkpoint](evidence/direct-runtime-checkpoint.md) for executed evidence; this document alone is not evidence that a command ran. Run heavy commands through the normal build-slot wrapper. Do not parallelize full builds on the shared machine.

## 1. Focused semantics and direct-runtime tests

```bash
dotnet test Extensions/Rules/tests/Tests/Elsa.Rules.Tests.csproj
```

The shared finite witness starts with `seed`, derives `left/right`, then `joined`, then `final`, then commits an empty-addition round. Expect four committed rounds, Succeeded and exactly `final,joined,left,right,seed`. With MaxRounds=3 expect Faulted `rules.round-budget-exhausted` after committing the same facts. Require tests for no-addition success at the final limit, invalid budgets, omission of applicable firings, canonical UTF-8 bounds, equivalent receipt replay and cancellation before/after admission.

Planned native integration cases must run real persisted Sequence and Flowchart, observe the following activity and sibling/join semantics, then cover child fault/cancellation/refusal, parent-terminal proof, duplicate delivery and recoverable Unavailable. A root-only invocation is insufficient. Results include facts and use canonical content equality after serialization.

## 2. Required four-engine suite

```bash
python3 tools/rules/verify-providers.py --require-all --evidence-dir /tmp/rules-provider-evidence
```

The driver starts/uses disposable configured SQLite, PostgreSQL, SQL Server and MySQL stores and executes `Extensions/Rules/tests/Persistence/EntityFrameworkCore/ProviderTests/Elsa.Rules.Persistence.EntityFrameworkCore.ProviderTests.csproj` in required-provider mode. Credentials come from the existing provider fixture environment convention; keep them out of evidence. Every engine must actually execute the same invariant suite with separate contexts/migrations. Missing engine/container capability is a failed qualification, not a skip. Record engine versions and per-engine counts. The driver must clean up only resources it created.

Require admission/cancellation/refusal races, exact null scope and collation cases, stale worker/revision rejection, renew/release invariants, accepted-receipt lost response, single consumption, bounded inspection, direct/no-outbox and native/proof-before-ack cases. Retain counterexample evidence for meaningful removal controls and restore original code before the final run.

## 3. Actual packages and process replacement

```bash
python3 tools/rules/verify-packaged-rules.py \
  --source-ref <immutable-source-sha> \
  --publish-state-ref <immutable-versioning-record-sha> \
  --evidence-dir /tmp/rules-package-evidence
```

Resolve real immutable refs before running; angle-bracket text is a placeholder, never a claimed tested command. The tool must reject mutable or missing refs. It computes repository versions, packs the actual module closure, creates a clean external PackageReference consumer, records content/archive/asset identities, and runs independent PostgreSQL-backed processes. Both accepted-unconsumed receipt and consumed-round recovery must pass with mutable design storage unavailable. Report loaded semantic package identity, not only version strings or source SHA.

## 4. Rebuilt host and upgrades

Run the rebuilt Workbench with a fresh isolated database using the runner documented in [backend e2e README](../../e2e-tests/README.md), then execute the new `e2e-tests/rules/` journey through that runner. The implementation supplies its exact runner switch in this document once the suite is registered. Verify publish/start, persisted Sequence continuation, persisted Flowchart sibling/join behavior, success facts, fault/cancellation/refusal and resumption. Source-only unit tests cannot substitute for this step.

The package harness additionally retains a pre-Rules native execution, installs the additive Rules schema, and proves native continuation unchanged. An admitted Rules run keeps its original semantic pin through an incompatible deployment and later compatible recovery. Rollback disables new admissions while preserving existing responsibility; no destructive schema rollback or automatic repinning.

## 5. Integration gate

Run the affected build/tests, architecture guard, public-surface/solution-filter validation and current generated-map check. Follow the current generated-maps policy instead of committing obsolete snapshots. Review the integrated diff with a bounded self-review loop. Attach evidence to the PR, verify exact-head CI and resulting-main CI/Studio/CodeQL/maps, and record skipped jobs separately from passes. Keep #3181 open until all acceptance in the specification is satisfied.
