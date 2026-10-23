# Durable Rules acceptance quickstart

Sections 1–3 are runnable for semantics, direct host composition, four-provider store contracts and the package consumer. Section 3 also runs PostgreSQL process replacement, live-successor stale acceptance and isolated persistence controls. Section 4 describes the package-backed host runner; execution receipts, not these instructions, establish its acceptance. See the [direct-runtime checkpoint](evidence/direct-runtime-checkpoint.md), [provider checkpoint](evidence/provider-checkpoint.md) and [package checkpoint](evidence/package-checkpoint.md) and [process qualification checkpoint](evidence/process-checkpoint.md) for executed evidence; this document alone is not evidence that a command ran. Run heavy commands through the normal build-slot wrapper. Do not parallelize full builds on the shared machine.

## 1. Focused semantics and direct-runtime tests

```bash
dotnet test Extensions/Rules/tests/Tests/Elsa.Rules.Tests.csproj
```

The shared finite witness starts with `seed`, derives `left/right`, then `joined`, then `final`, then commits an empty-addition round. Expect four committed rounds, Succeeded and exactly `final,joined,left,right,seed`. With MaxRounds=3 expect Faulted `rules.round-budget-exhausted` after committing the same facts. Require tests for no-addition success at the final limit, invalid budgets, omission of applicable firings, canonical UTF-8 bounds, equivalent receipt replay and cancellation before/after admission.

Native integration cases run real persisted Sequence and Flowchart, observe the following activity and sibling/join semantics, and cover child fault/cancellation/refusal, parent-terminal proof, duplicate delivery and recoverable Unavailable. A root-only invocation is insufficient. Results include facts and use canonical content equality after serialization.

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

Resolve real immutable refs before running; angle-bracket text is a placeholder, never a claimed tested command. The tool rejects mutable or missing refs. It computes repository versions, packs the actual module closure, including both native bridge modules when present, creates a fresh external PackageReference consumer and cache, and records distinct NuGet content/archive/loaded-asset identities. It runs real SQLite-backed direct success, budget fault, replay, incompatible-pin and changed-asset rejection cases. An actual assembly-digest removal control must fail its intended assertion; restoration must pass direct execution again. Each run retains commands, logs, packages, resolved assets and result JSON outside the checkout. No packages are published.

`--stage direct` proves a clean restore of every packed module root, including all six Rules modules when the native modules are available. `--stage process` reuses that same packed closure but builds a separate clean consumer from the four direct runtime/storage roots only. That process consumer must resolve the exact selected roots and any resolved packed package bytes, and its dependency graph must contain no Design/Definitions package; it does not claim native workflow execution. The two stages are complementary evidence.

Use `--stage pack` for packing only; this never reports a consumer pass. `--packed-run <absolute-run-directory>` reuses a successful, unchanged pack matching both immutable refs; each consumer cache and deployment remains fresh. Add `--stage process` to run the four-root direct-runtime proof followed by six independent-process PostgreSQL cases and three isolated persistence controls. Docker must be available. The coordinator creates a disposable PostgreSQL 16.15 container bound only to loopback and a separate database per case. It stops the original process after committed acceptance or consumption, then starts a successor against the same database. Two additional cases delay the original response until successor completion; the fifth cancels an accepted, unconsumed receipt and checks cancellation before admission. A sixth case holds a successor lease while the old worker submits new work, reads and releases; all three old operations must return LeaseLost without payload or semantic change. This process host uses only direct Rules runtime/storage package roots and does not register native design services. The coordinator records actual PIDs, database identity, receipt history, terminal bytes and cleanup, keeping credentials out of evidence.

The cancellation-fingerprint, same-owner fence and consumed-marker controls each require an intact baseline, failure at the named assertion after removing exactly one check in the disposable source clone, and a passing rerun after source/deployed-assembly restoration. All use real worker-produced accepted receipts. `processBoundaryProofPassed` covers the original five cases; `processProofPassed` additionally requires the live-successor case and all three controls. The process-stage receipt keeps `fullAcceptancePassed: false`: native integration and retained-run upgrades require their separate journeys below, and final integrated leaf gates still apply. Repeated fresh direct processes alone are not recovery proof.

## 4. Rebuilt host and upgrades

The Workbench qualification uses the same immutable package closure and a fresh package cache. It
resolves all six Rules modules as exact PackageReferences, verifies the deployed DLLs against the
selected archive assets, and checks the semantic deployment receipt against the host dependency graph.
Other Workbench dependencies can remain source project references. The runner copies the immutable Workbench host into a separate sibling project within the disposable source clone, preserving host source/configuration and replacing only its four direct Rules project references with six exact package references. Production Workbench references remain unconditional; no special dependency-guard exception is required.

```bash
python3 tools/rules/verify-packaged-rules.py \
  --source-ref <immutable-source-sha> \
  --publish-state-ref <immutable-versioning-record-sha> \
  --evidence-dir /tmp/rules-workbench-evidence \
  --stage workbench
```

After direct-package checks, this runner invokes `e2e-tests/rules/Test-DurableRules.ps1` with the
published assembly path and verified receipt. The script owns its temporary content root, SQLite
databases and loopback server; it uses actual login, CLR catalog, submit, publish, start and runtime
inspection APIs. It is registered as a separate package qualification in the
[backend e2e README](../../e2e-tests/README.md), not silently counted in the source-only suite.
No feature is enabled in the default Workbench shell. Retain the generated receipt and logs; a
successful host publish alone is not a passing REST journey.

`Test-DurableRules.ps1` includes Sequence, linear Flowchart and a parallel Flowchart whose Event
sibling stays suspended after Rules completes. It restarts the owned host before resuming that
Event, verifies one join/following activity and rejects a duplicate stimulus. Its public output
assertions verify the observed JSON payload and declared snapshot alias, not CLR type identity or
complete canonical terminal bytes. Read-only SQLite snapshots verify the join and following
activity each have one completed internal attempt; public retry-lineage counts may be absent.

The Workbench stage also runs `Test-RulesAdmissionRecovery.ps1`: publish while admission is enabled,
restart with new Rules admissions disabled, retain two native invocations without child rows,
cancel one via the public alteration API, and restore admission for the other. The cancelled
invocation stays terminal; the other completes with one persisted following-activity attempt.
An invalid round limit faults through the native boundary without a fabricated Rules row. The
script uses Python 3 to capture read-only native/Rules SQLite snapshots after bounded durable-state
polls. This tests unavailable admission, not an injected resume-reader outage.

The separate persisted C# journeys cover resume-reader unavailability, consumed-child redelivery
and proof-before-ack regression controls; see the [compiled-runtime](evidence/native-compiled-runtime-checkpoint.md)
and [native recovery](evidence/native-recovery-acceptance-checkpoint.md) checkpoints. Only cases named
in an execution receipt are proved by that run; the [task list](tasks.md) tracks final delivery gates.

The Workbench stage also runs `e2e-tests/rules/Test-NativeRulesInstallation.ps1`. It first starts
without Rules features, persists a Sequence waiting on an Event, and stops the owned process. It
then enables Rules against the same SQLite database and checks every pre-existing schema object
plus the complete retained workflow/activity/bookmark/value/artifact rows before resuming the
original Event. The original variable must survive, the following output executes once, and a
second stimulus must resume nothing. Rules assemblies are already present in the qualified host;
this proves additive feature/schema installation, not an upgrade from an older binary release.

To repeat that journey against a previously qualified deployment:

```bash
pwsh -NoProfile -File e2e-tests/rules/Test-NativeRulesInstallation.ps1 \
  -AssemblyPath <qualified-Elsa.Workbench.dll> \
  -DeploymentReceiptPath <qualified-deployment-receipt.json> \
  -EvidencePath /tmp/rules-native-installation/result.json
```

The script needs Python 3 for read-only SQLite snapshots. Retain the evidence JSON and both
snapshot files. The snapshot comparator fails if existing state or schema changes; a passing
HTTP readiness response alone is insufficient.

For retained Rules deployment acceptance, prepare two independent four-runtime-root consumer runs
with `verify-packaged-rules.py --stage runtime` and their own immutable `--source-ref` and verified
`--packed-run`. This stage runs direct execution and identity removal/restoration checks but does
not run the full process suite. Never share a NuGet cache between the two runs. Then run:

```bash
python3 tools/rules/package_upgrade.py \
  --baseline-run <completed-runtime-consumer-A> \
  --successor-run <completed-runtime-consumer-B> \
  --evidence-dir /tmp/rules-retained-upgrade
```

The coordinator verifies both package feeds and loaded assets, requires distinct trusted pins with
compatible common record schemas, and owns one disposable PostgreSQL container/database. A commits
an accepted receipt and is stopped. B consumes only that common receipt, retains A's pin, and cannot
claim the next semantic round. A returns with `AcceptNewInvocations=false`: the original invocation
replays and completes, while a fresh identity is rejected without a row. Exact round history, pins,
process IDs, full semantic snapshots, immutable deployment hashes and container cleanup are recorded.
These are local proof deployments, not package releases; the computed versions may match while
selected assembly hashes differ. No destructive schema rollback or automatic repinning is permitted.

## 5. Integration gate

Run the affected build/tests, architecture guard, public-surface/solution-filter validation and current generated-map check. Follow the current generated-maps policy instead of committing obsolete snapshots. Review the integrated diff with a bounded self-review loop. Attach evidence to the PR, verify exact-head CI and resulting-main CI/Studio/CodeQL/maps, and record skipped jobs separately from passes. Keep #3181 open until all acceptance in the specification is satisfied.
