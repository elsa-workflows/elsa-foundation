# T005 Core contract evidence

Date: 6 November 2026. This slice implements immutable scope/logical-definition/admission identities, the submission/receipt SDK and unregistered provider/source preparation contracts. It registers no producer, store, handler or new format. Durable acceptance, delivery, cancellation races, physical providers, API, activities and UX remain NOT RUN.

| Check | Passed | Failed | Skipped | Retained result |
|---|---:|---:|---:|---|
| SDK finite-deadline regression | 25 | 2 expected | 0 | [red TRX](sdk-boundary-red.trx.gz) |
| Corrected SDK | 27 | 0 | 0 | [green TRX](sdk-boundary-green.trx.gz) |
| Identity/source mutation control | 31 | 8 expected | 0 | [red TRX](contract-mutation-red.trx.gz) |
| Restored identity/source contracts | 39 | 0 | 0 | [green TRX](contract-green.trx.gz) |
| Complete affected Runtime project | 3,469 | 0 | 0 | [whole TRX](whole-runtime.trx.gz) |

The SDK regression uses the actual T004 lifetime helper at `MaxValue - 1 second`: a finite one-second buffer or deduplication window legitimately ends at `MaxValue`. Both cases compiled and failed before removing only the SDK deadline rejection. Acceptance-time validation and strictly later deadlines remain enforced; the T004 helper and its 52 tests are byte-identical to their earlier evidence.

The separate mutation control deliberately collapsed definition namespaces, changed default unsupported source participation to true, and let bare completion-aware dispatch silently succeed. Eight actual assertions failed: six identity cases and two source-boundary cases. Production files were restored byte for byte and all 39 affected cases then passed. These controls exercise values and refusal contracts, not physical provider authority or durable delivery.

The complete Runtime run passed on the corrected, unmutated source before mutation; restoration hashes and the subsequent focused run prove the same source was restored. It includes 23 identity, 27 SDK and 16 source-contract cases alongside the existing project. Source/test/result hashes, expected-control outcomes and restoration evidence are in [manifest.json](manifest.json). Runs used the existing project with one build worker, disabled shared compilation for compilation, and `--no-build --no-restore` for the complete project. An intermediate build encountered declarations still being written by the parallel contract author; it executed no tests and is not counted as a green or causal-red run.

Independent static reviews found no remaining concrete identity/SDK/source-value defect. Provider registry enforcement, complete typed Message participants, schema guards, actual claim completion and cancellation checkpoints are still implementation work in [tasks](../../tasks.md). This is component evidence, not a complete-PR review round or product acceptance.
