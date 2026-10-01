# Implementation evidence: explicit private environment inputs

Implementation owner: [#2292](https://github.com/elsa-workflows/elsa-foundation/issues/2292), under #1962 / #1959. Authoring #2277 and its causal delivery dependency #2282 are complete. This records incremental local evidence, not a production capability or delivery pass.

## Setup checkpoint

Root reviewed the inherited public-label/private-value boundary and added unknown overlay-only error-identity omission to the contract/matrix and T030/T034. The specification is Approved to implement, not Implemented. The specify-stage non-claim is prose; the 27 actual design checklist items are complete. The optional pre-implement auto-commit hook was skipped because there was no outstanding implementation checkpoint at entry; root commits explicit reviewed paths.

T001 records ownership and compatibility at the worker transport, file-only candidate contract, persistence DTO contract and selected host. No old request/method/response shape was changed. T002 adds independent Version1 and the metadata-only assembly declaration shape (int/string, assembly target, read-only properties, duplicate detection allowed). T003 enrolls Workbench only; Program.cs source order remains unchanged. T004 adds shared synthetic public/private canaries, correlation IDs and exact-byte document builders to the existing disposable fixture; no extra project or private-value artifact was created.

Actual local checks at this setup checkpoint:

- EF library build: success, zero warnings/errors.
- Real Workbench build: success, 92 existing migration/style/obsolete warnings, zero errors. Build does not execute startup, prove capability binding or establish inspection behavior.
- Existing architecture source inventory test: one passed, zero failed/skipped, with the two new files added to the exact inventory. The future operation file remains to be added under T039.
- Old-reader regression cases: 12 passed, zero failed/skipped. All nine old persistence commands first parse their baseline, then reject an environment-input object and explicit null. The old candidate reader rejects both forms at the outer/candidate/file boundaries. This is compatibility guard evidence before new transport integration; T028 and final compatibility controls remain open.

The original candidate-v1 host operation, validator and persistence preparation policy remain unchanged. No new operation method, public CLI option, private capture/protocol or runtime/DB acceptance is claimed at this checkpoint. Remaining tasks, adverse controls, complete affected suites, real Workbench/public-wrapper journey, maps and gated delivery remain required. No hosted implementation PR is open yet.
