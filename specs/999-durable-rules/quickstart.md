# Durable Rules acceptance quickstart

Sections 1–3 are runnable for semantics, direct host composition, four-provider store contracts and the four-module package consumer. Section 3 also runs the five PostgreSQL process-boundary cases. Live-successor stale acceptance, isolated removal controls and section 4 remain implementation targets. See the [direct-runtime checkpoint](evidence/direct-runtime-checkpoint.md), [provider checkpoint](evidence/provider-checkpoint.md) and [package checkpoint](evidence/package-checkpoint.md) and [process-boundary checkpoint](evidence/process-boundary-checkpoint.md) for executed evidence; this document alone is not evidence that a command ran. Run heavy commands through the normal build-slot wrapper. Do not parallelize full builds on the shared machine.

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

The driver starts/uses disposable configured SQLite, PostgreSQL, SQL Server and MySQL stores and executes `Extensions/Rules/tests/Persistence/EntityFrameworkCore/ProviderTests/Elsa.Rules.Persistence.EntityFrameworkCore.ProviderTests.csproj` in required-provider mode. Optional server connections use `ELSA_RULES_EF_POSTGRESQL_TEST_CONNECTION_STRING`, `ELSA_RULES_EF_SQLSERVER_TEST_CONNECTION_STRING` and `ELSA_RULES_EF_MYSQL_TEST_CONNECTION_STRING`; the account must be able to create/drop disposable test databases. Otherwise Docker is required. Keep credentials out of evidence. Every engine must actually execute the same invariant suite with separate contexts/migrations. Missing engine/container capability is a failed qualification, not a skip. Record engine versions and per-engine counts. Fixtures clean up only resources they created. The receipt distinguishes the eight shared journey cases from their many invariant assertions and records source stability; delivery proofs in this suite are synthetic, not native parent acceptance.

Require admission/cancellation/refusal races, exact null scope and collation cases, stale worker/revision rejection, renew/release invariants, accepted-receipt lost response, single consumption, bounded inspection, direct/no-outbox and native/proof-before-ack cases. Retain counterexample evidence for meaningful removal controls and restore original code before the final run.

## 3. Actual packages and direct consumer

```bash
python3 tools/rules/verify-packaged-rules.py \
  --source-ref <immutable-source-sha> \
  --publish-state-ref <immutable-versioning-record-sha> \
  --evidence-dir /tmp/rules-package-evidence
```

Resolve real immutable refs before running; angle-bracket text is a placeholder, never a claimed tested command. The tool rejects mutable or missing refs. It computes repository versions, packs the actual four-module closure, creates a fresh external PackageReference consumer and cache, and records distinct NuGet content/archive/loaded-asset identities. It runs real SQLite-backed direct success, budget fault, replay, incompatible-pin and changed-asset rejection cases. An actual assembly-digest removal control must fail its intended assertion; restoration must pass direct execution again. Each run retains commands, logs, packages, resolved assets and result JSON outside the checkout. No packages are published.

Use `--stage pack` for packing only; this never reports a consumer pass. `--packed-run <absolute-run-directory>` reuses a successful, unchanged pack matching both immutable refs; each consumer cache and deployment remains fresh. Add `--stage process` to run the direct proof followed by five independent-process PostgreSQL cases. Docker must be available. The coordinator creates a disposable PostgreSQL 16.15 container bound only to loopback and a separate database per case. It stops the original process after committed acceptance or consumption, then starts a successor against the same database. Two additional cases delay the original response until successor completion; the fifth cancels an accepted, unconsumed receipt and checks cancellation before admission. No design modules are present. The coordinator records actual PIDs, database identity, receipt history, terminal bytes and cleanup, keeping credentials out of evidence.

The receipt keeps `fullAcceptancePassed: false`. `processBoundaryProofPassed: true` covers those five cases only. T019–T020 remain open for stale new acceptance while a successor holds the lease and isolated cancellation, fencing and consumption removal controls. Native modules and retained-run upgrades are also pending. Repeated fresh direct processes alone are not recovery proof.

## 4. Rebuilt host and upgrades

Run the rebuilt Workbench with a fresh isolated database using the runner documented in [backend e2e README](../../e2e-tests/README.md), then execute the new `e2e-tests/rules/` journey through that runner. The implementation supplies its exact runner switch in this document once the suite is registered. Verify publish/start, persisted Sequence continuation, persisted Flowchart sibling/join behavior, success facts, fault/cancellation/refusal and resumption. Source-only unit tests cannot substitute for this step.

The package harness additionally retains a pre-Rules native execution, installs the additive Rules schema, and proves native continuation unchanged. An admitted Rules run keeps its original semantic pin through an incompatible deployment and later compatible recovery. Rollback disables new admissions while preserving existing responsibility; no destructive schema rollback or automatic repinning.

## 5. Integration gate

Run the affected build/tests, architecture guard, public-surface/solution-filter validation and current generated-map check. Follow the current generated-maps policy instead of committing obsolete snapshots. Review the integrated diff with a bounded self-review loop. Attach evidence to the PR, verify exact-head CI and resulting-main CI/Studio/CodeQL/maps, and record skipped jobs separately from passes. Keep #3181 open until all acceptance in the specification is satisfied.
