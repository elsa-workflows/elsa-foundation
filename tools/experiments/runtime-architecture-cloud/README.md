# Hosted runtime experiment runner

The first hosted path materializes the immutable reviewed v2 transfer, verifies all three original result trees,
applies a hash-bound common compiler repair, records a common Linux process-signal adaptation, then restores/builds
the complete actual baseline probe graph. The first hosted baseline compilation exposed a missing PersistenceScope
namespace and an ambiguous ActivityTransitionKind enum in the shared harness. The minimal repair patch adds the
actual namespace and qualifies the intended workflow attempt enum in all three variants, after original tree
verification. State records its SHA and before/after commit/tree, and evidence retains the patch and source commit
objects. The original v2 archive and the failed hosted build's receipts remain unchanged. Applying a repair is not
compilation evidence; the next actual build must verify it.
It commits generated dependency lock material before a separate locked, full-output-closure build qualifies binaries.
The exploratory first build never qualifies measurements. Failures and controller/compiler logs remain under
`ROOT/evidence/` for the workflow's unconditional artifact upload. No production-main source is changed.

```text
python3 tools/experiments/runtime-architecture-cloud/runner.py --root "$RUNNER_TEMP/elsa-runtime-prototypes" materialize
python3 tools/experiments/runtime-architecture-cloud/runner.py --root "$RUNNER_TEMP/elsa-runtime-prototypes" build --variant baseline
```

This entrypoint refuses local execution; it requires Linux GitHub Actions and an owned RUNNER_TEMP child directory.
The workflow invocation authorizes its selected bounded phase. Existing freezers/controllers and reviewed Linux
helpers retain 540-second command/600-second closeout accounting, exact process ownership and failure receipts.
The verified archive is source-only; its macOS helpers remain non-executable reference text. Actual Linux helpers
live under `helpers/`. Every not-yet-executed engine stage remains NOT_RUN; `qualifiedForBenchmarks` remains false
pending independent review of the downloaded actual evidence. Root owns workflow publication and dispatch.

The full hosted run uses these commands in order (each line invokes `runner.py --root ROOT`):

```text
materialize
prepare-postgres
build --variant baseline
sync-lock
focused --variant runview
focused --variant snapshot
provider-build
build --variant runview
build --variant snapshot
provider-qualify
proof --provider PostgreSql --variant VARIANT --mode MODE --case CASE
proof --provider Sqlite --variant VARIANT --mode MODE --case CASE
measure --profile count --mode Balanced --leaves 10
measure --profile timing --mode Balanced --leaves 10
```

`prepare-postgres` pulls `postgres:17.6` once for the actual host CPU (AMD64 or ARM64), records its exact repository
digest, config image ID and local daemon ID, then commits the platform pin into all three owned source trees and
the snapshot fixture before any qualified build. Each later phase creates a fresh owned container with that cached
immutable image, fixed durability/settings/resource bounds and new databases; pulls are forbidden there.
The original archive trees remain recorded separately from these explicit hosted adaptations.

Before each mode's matched observations, run all three variants through PostgreSQL `crash-before-commit`,
`crash-after-commit`, `crash-after-body`, `sequence`, and `suspension`, using `--leaves 100` and the declared cap50.
This retains a committed prefix and exercises cold resume at the measured provider. Also run SQLite `suspension`,
`late-output`, `waited-child`, `protected`, `compensation-control`, and `compensation-kill` (default leaves10).
Actual successful source-bound
receipts are required, together with the A/B focused contracts and the snapshot provider's eleven PostgreSQL
controls. The provider-control CLI has its own fixed test matrix; its Balanced label is an identifier rather than
a claim that it ran only one mode. PostgreSQL compensation qualification is not inferred from SQLite receipts.

Expand observations sequentially across Balanced/Durable and 1/10/100 leaves, with timing `--concurrency 1` or `4`;
count and timing use separate
containers/windows. `--action resume --leaves 100` measures a fresh-process Event resume with growing retained
history, one sample and no warmup. Each cell executes three serial counterbalanced rounds (nine cohorts), stores
actual SQL/wire observations separately from elapsed timing, and observes whole-database/table/index/archive
size after each cohort. Quiet-window, semantic, crash or cleanup failures stop that command and remain visible.
The runner records raw observations for independent analysis; it never writes an independent-review approval.

Container phases live under `ROOT/private/`. Only non-secret receipts/configs/logs are copied into `evidence/`;
connection/env files, protected provider exceptions and SQLite database files are excluded even after failure.
An export inventory preserves original paths and byte hashes, so downloaded receipts can be resolved without
rewriting their immutable contents. Unknown resources are preserved by the existing controller, with an incomplete
receipt; they are never globally removed. The process helper explicitly retains its polling limitations.

Uploaded evidence contains actual committed source objects in per-variant Git bundles (prerequisite production
base f4), binary-safe patches, and every committed dependency lock. Each successful probe/provider/focused build
also retains every byte of its captured output closure with an inventory and hashes. Failed exploratory restores'
generated probe locks are retained separately as diagnostic snapshots, without being called committed or qualified.
These exports include explicit Linux/native image adaptations and later committed compilation repairs, so recorded
source commits and compiled bytes remain inspectable after the temporary hosted worktrees disappear.
