# Design review receipt

Research/design only; no production implementation or acceptance claim. Owning issue: #3602. Reviewers used separate lenses and did not receive one another's reports.

## Specification review — 2 November 2026

Three independent reviews (issue conformance, usability/standards, architecture/correctness) found no actionable specification issue. The bounded-expression card answer is recorded in spec FR-004. Product specification is ready for planning; new-domain admission remains explicitly pending.

## Plan/contract review round 1

Surviving findings and corrections:

- Nonstandard leading unary `=`/`!=` grammar could produce nonportable DMN cells. Removed; bare literal/not(literal) cover equality/inequality.
- A 34-digit evaluator contract without lossless browser/API transport would round values above 2^53. Added tagged decimal-text transport on every decision boundary and refusal of binary-floating CLR inputs.
- ASCII symbol regex admitted FEEL literals/reserved tokens. Added explicit case-sensitive exclusion set and refusal witness.
- String-escape description was open-ended. Specified FEEL spellings, Unicode scalar handling and profile-specific restrictions.
- Proposed durable test snapshots contradicted default raw-value retention and over-specified recovery. Simplified to bounded synchronous tests with completed receipts, exact replay before mutable reads, and conflict on unavailable original content after precommit interruption.
- API permission checks did not establish authority/protection in native/BPMN invocation. Added current-subject authorization under trusted execution identity, existing result/value/inspection policies, protected-source resource denial and cross-permission witnesses.

The owner's execution-model question prompted direct inspection of now-merged #3410/#3603 and actual generic model/material/authority/checkpoint contracts. `contracts/execution-integration.md` now defines reuse. No new Decisions execution coordinator, pending-job ledger or scheduler is planned.

## Plan/contract review round 2

Architecture/correctness and Studio/usability/standards reviews converged with no remaining actionable findings. Evaluator/profile review confirmed the semantic corrections; one alleged doubled-escape finding was adversarially checked against actual file code points: newline token is [92,110], quote [92,34], backslash [92,92], u token length6 and U token length8. The displayed tool JSON had escaped these characters; the source spellings are correct and were not changed on that claim.

The evaluator reviewer withdrew the escape claim after independently checking source character codes. All three plan/contract lenses converged.

## Task review

Three independent lenses reviewed the 40-task list. Two identified the same omission: the planned exact-published-version HTTP evaluation operation lacked an explicit implementation task. T014 now includes its authorization, protected-output checks, structured outcomes and direct/HTTP parity tests. The Studio lens also made clone-to-draft explicit in API/UI/real-host tasks and its capability contract.

Final task rechecks by Studio/standards and architecture lenses converged after those fixes. Task T002 is complete; owner admission T001 and all product/issue acceptance rows remain incomplete. The final design has no surviving actionable review finding.

Local documentation verification: catalog link guard reports 0 broken links in 189 files; feature-relative link check reports 0 broken links across 11 Markdown files; task IDs are unique/sequential T001–T040. No product build/test, exact-head CI or acceptance is inferred from these documentation checks. This receipt describes local design review, not PR Code Review or merge authorization.
