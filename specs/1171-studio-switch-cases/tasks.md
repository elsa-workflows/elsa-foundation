# Tasks: Visual Switch cases

- [x] T001 Add focused descriptor/model and mounted regressions for empty/add/rename/delete, exact duplicates, branch preservation, malformed/multi-child descriptors and read-only/external updates. Capture failing baseline.
- [x] T002 Implement catalog-driven immutable collection edits in `collectionSlots.ts` and the inspector collection editor, preserving fixed slots and existing branch navigation/choice.
- [x] T003 Wire discoverable canvas add actions into Sequence and graph nodes in both controlled graph hosts, retaining permissions and existing persistence/undo paths.
- [x] T004 Extend the shared browser editor fixture and add Switch/Parallel cases across hosts, composition modes, keyboard/narrow/read-only, undo/redo and persisted reload. Capture rendered light/dark evidence and baseline red/green.
- [ ] T005 Run whole affected Workflows tests, Studio typecheck/lint/build, affected browser tests, architecture/maps and relevant consumer checks. Review the complete diff independently for correctness, standards and acceptance; fix and repeat until clean.
- [ ] T006 Recheck claim, competing PRs and current main; commit, open/attach one draft PR and post acceptance-mapped evidence. Finish review on the exact integrated head, mark ready/request review, verify required checks, merge under org rules, and qualify resulting main. Mark this spec Implemented in the delivery PR.

Spec reviewed before plan; plan reviewed against both constitution Gate indexes and the repo review checklist before tasks. This task list has been checked against every acceptance criterion of #3493. No new dependency, provider, backend contract or persistence schema is planned.
