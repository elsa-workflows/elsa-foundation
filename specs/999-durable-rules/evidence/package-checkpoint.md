# Clean package consumer checkpoint

This is the four-module direct-package subcheckpoint for T014–T015, not full #3181 acceptance. The immutable product source is `f923b12a41ab3280c146d2c1f8a45ba0f102caaa`; version calculation uses publish-state `eb3e12531b0cbd594374df1e49df0fadef9a50f3`, whose last published source is `9462cb743c08a515e9b8973c57110e3f44df0f7f`. Native Design/Runtime modules, separate-process PostgreSQL recovery, retained-run upgrades and Workbench acceptance remain open. No package was published and publish-state was not changed.

## Implemented and executed

The coordinator creates its own detached source clone, computes real repository versions and packs the complete dependency closure with the generated version properties applied to build and pack. The four Rules modules resolved to `4.0.0-branch-agent-codex-3181-durable-rules`. Their four internal dependencies resolved to Tasks.Core and Primitives `4.0.14-preview`, Persistence.Schema `4.0.12-preview`, and Persistence.EntityFramework `4.0.32-preview`. All eight archives match calculated IDs, versions, source commit and input fingerprints.

A standalone consumer outside repository build inheritance restores those archives into a fresh cache, then restores in locked mode and publishes. Its assets graph contains zero project libraries. Every internal restored archive matches the local feed bytes. The consumer uses ordinary public invocation preparation, store admission and the real GenericHost background worker, with the real SQLite migrations. It does not replace the evaluator or implementation-identity service.

The generated deployment receipt distinguishes NuGet's own content hash from raw archive SHA-512. It compares feed/cache content hashes, the cache marker and active deps graph, then matches selected package asset bytes against published files and default-context assembly full names. Core, Runtime and Persistence.Core are semantic pins; EF and other dependencies are recorded as dependency provenance. The receipt records actual SDK/runtime identity and declared artifact/invocation/result schema versions; execution checks the latter against actual prepared and terminal values. This is file-based deployment evidence, not publisher-trust or in-memory execution attestation.

The final consumer run passed all expected checks:

| Case | Observed result |
|---|---|
| Omitted budget | Captured default 7; Succeeded after 4 committed rounds |
| Explicit budget 4 | Succeeded after 4 committed rounds |
| Explicit budget 3 | Faulted after 3 committed rounds with `rules.round-budget-exhausted` |
| Facts/history | Exactly `final,joined,left,right,seed`; contiguous round ordinals and accepted/consumed attribution |
| Exact immutable admission replay | Replay for all three terminal runs |
| Deliberately incompatible pin | Unavailable; no durable run admitted |
| Changed receipt asset digest | Identity unavailable; preparation unavailable; no durable run admitted |
| Alternate valid assembly with same full name | Rejected because deployed bytes differ |
| Original restored deployment | All direct cases pass again |

The incompatible-pin case explicitly records `fabricatedIncompatibility: true` and `versionBUpgradeProof: false`. It tests admission rejection, not recovery of retained state under a real second deployment. These are fresh direct processes and databases, not process-replacement recovery.

## Regression control and review

The control operates only on the owned detached acceptance clone and deployment. It first builds a valid alternate Runtime assembly by changing an unrelated receipt-size constant by one byte. With the real digest check intact, the deployed identity is rejected. Removing only that check causes the unavailable probe to fail with exactly `Expected deployed implementation identity to be unavailable.` Compilation/loader failures cannot satisfy this control. Source and deployed DLL are restored byte-for-byte, and the final direct probe passes. The receipt retains hashes for the original, alternate, changed-check and restored files.

Review corrected the initial probe's string-overload compilation error, zero-based round-history expectation and nullable-flow annotation. Subsequent review added explicit schema provenance/assertions and the C# template to the before/after harness hash check. Reuse now rejects receipts lacking successful packing, stable harness evidence or matching immutable inputs. Mutable `HEAD` was rejected even with Python optimization enabled, and a prior receipt lacking integrity evidence was rejected. Successful reuse revalidates source cleanliness and every package archive before starting a fresh consumer.

The final run used stable harness files and produced 14 command records: 13 successful commands and the one intended assertion failure. The actual pack commands and consumer commands, package/runtime identities, full direct outcomes, control results and raw artifact hashes are retained in [package-qualification.json](package-qualification.json). Final provenance review separately captures the build-selected SDK from the consumer directory and the launch SDK, identifies receipt-generation and direct-execution PIDs, and queries the migrated SQLite file for its live engine version. The final run records build SDK 10.0.300 and SQLite 3.49.1. Compiler warnings in the probe were eliminated. Existing NuGet audit warnings for SQLitePCLRaw.lib.e_sqlite3 2.1.11 and System.Security.Cryptography.Pkcs 8.0.1 remain visible in logs; they were not suppressed or remediated here. Python syntax, diff and current generated-map checks passed. Product source is unchanged by this tooling checkpoint, so previously completed provider/architecture suites are not represented as newly executed.

## Reproduction and retained evidence

```sh
python3 tools/rules/verify-packaged-rules.py \
  --source-ref f923b12a41ab3280c146d2c1f8a45ba0f102caaa \
  --publish-state-ref eb3e12531b0cbd594374df1e49df0fadef9a50f3 \
  --evidence-dir /tmp/rules-package-evidence
```

The commits must exist locally. This combined command packs then runs the consumer. The recorded execution used a reviewed pack run followed by `--packed-run` reuse to avoid packing unchanged inputs while fixing the probe; the durable receipt preserves both command lists. The first compiler failure and earlier pre-integrity pack are not described as qualification.

Retained pack: `/tmp/3181-packages-reviewed/run-5bfa6f869fe04035892d61add3b75e06`. Final consumer: `/private/tmp/3181-package-direct-reviewed/run-1c291cf95a7f4a5db1ec402b1e67c3ad`. Input-control logs: `/tmp/3181-package-mutable-control.log` and `/tmp/3181-package-reuse-control.log`. Feed archives, lock/assets files, deployment, databases and numbered logs remain in those local evidence directories; the committed receipt makes their identities and outcomes durable. `fullAcceptancePassed` remains false and T014–T015 remain open for the complete six-module target. Physical external effects remain at least once; historical unrelated timeout causes remain unproved.
