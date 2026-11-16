# Task-ordered delivery slices

The owner closed the original whole-feature PR #3912 without merge on 9 November 2026 and requested stacked PRs of roughly 3,000 changed lines per spec slice. On 13 November the owner explicitly resumed both the corrected RuntimeArtifact extraction and the complete standalone-trigger feature, including necessary qualification. [Resumption and merge-exception receipt](https://github.com/elsa-workflows/elsa-foundation/issues/3582#issuecomment-6857970842).

The original branch remains a donor, not a delivered feature. Existing design reviews and historical runs retain their exact source; they do not qualify a new port to current main. Every implementation task after M0 remains unchecked until its slice is integrated and verified. This file refines delivery shape; the specification's product decisions and acceptance criteria remain unchanged.

## Order and boundaries

| Order | Spec scope | Reviewable result |
| --- | --- | --- |
| 0 | M0 records; artifact predecessor for T010/T013 | Retain the reviewed design and this delivery plan; land the corrected shared helper and linear RuntimeArtifact 1.0→1.1 chain, scope model/protected writer, complete family stamp adaptations and focused regressions. No producer switch or trigger activation. |
| 1 | T007–T009 | Resource/subscription/grant contracts and optional in-memory management, validation and capability composition, split into dependency-coherent parts. No durable transport acknowledgement. |
| 2 | T010–T013 | Source/capture/recipient contract and immutable admission/retention rules, then the native provider and exact dispatch/cancel state machine. Preserve existing workflow intent/wire. |
| 3 | T008/T011 | EF resource/source/recipient storage and schema transitions, followed by each provider's additive physical migration. Keep migration history and snapshots intact. |
| 4 | T014–T015/T025–T029 | Serving-slot target/input preparation, mapping, resource authority and native root integration with exact publication/grant retention. |
| 5 | T016–T024/T030–T041 | Management/delivery API and ordinary dispatch/recovery; HTTP reservation/response/fan-out and genuine model/BPMN adapters in separate bounded increments. |
| 6 | T042–T046 | Event cohort and resource-owned schedule integration, with recurrence arithmetic compatibility and actual due-source controls. |
| 7 | T017–T024/T045/T050–T051/T055 | Studio resource management, reverse associations and mapping, delivery/impact operations, consumer examples and documentation, with actual composed acceptance per changed surface. |
| 8 | T047–T049/T052–T057 | Complete supported composition/provider/identity and failure/restart matrix, independent current-source reviews, final main verification and issue/spec closeout. |

Each table row may need multiple PRs to stay reviewable. Split by owning contract/provider/adapter or independently useful regression group, retain task order and declare the predecessor in each PR. A file-count or size split must not introduce a permissive stub or enable a half-installed capability. Source under an unselected optional feature does not establish delivery acceptance.

## Qualification and closure

The first RuntimeArtifact slice exercises protected storage only. It does not claim automatic coordinator production, standalone source intake, public management, HTTP/Event/schedule firing, genuine BPMN starts or Studio support. Its tests must account for tracked adoption/fencing/maintenance writes after finalization, not only entity creation. Negative controls must make its changed behavior fail without the relevant guard.

Run the affected whole projects and compare discovery to executed identities. Preserve missing-fixture, infrastructure, assertion, skipped and passed outcomes separately. Original frozen BPMN inputs come from the repository's pinned restoration tool; never synthesize replacement saved executions. Build serially on the shared machine and interpret timing failures against load.

Stacked PRs merge base-first. Use `Part of #3582` while feature acceptance remains incomplete; only the finishing delivery may close the issue and mark the spec Implemented. A precursor merge never closes the whole feature. Every slice records its exact reviewed head, validation, current check states and actual merge/resulting-main evidence.

The owner's explicit personal-account admin-merge exception applies if CI has not started or is too slow. Record its use and the pending/bypassed checks on the exact PR. This exception does not constitute a passing CI result, a clean source review or acceptance evidence for unexecuted behavior. Other paused programs and transferred ownership are unchanged.
