# Tasks: HTTP response delivery observations

Dependencies: T001 → T002 → T003 → T004 → T005. Independent test preparation may proceed within each task; implementation runs serially in this worktree.

- [x] T001 Record specification, source research, data model, contracts and qualification plan; claim issue #3450 and create a fresh branch.
- [x] T002 Add immutable retained activity observations and idempotent in-memory/EF append; preserve facts through checkpoint writes and API summary/exact reads. Add core/store regression tests and public API entries.
- [x] T003 Add opaque HTTP exchange identity, transport write/closure evidence and post-drain late-response reconciliation for production/Test Run ingress. Add transport, timer-resume, unknown/ambiguous, race/idempotency and failure-isolation tests.
- [x] T004 Surface warning badges and exact-invocation observation/delivery details in Studio, including Unknown. Add frontend and rendered-browser coverage.
- [x] T005 Run affected whole suites and mutation proof, isolated HTTP e2e, browser QA, build/bundle, architecture/maps and diff review. Mark spec Implemented only when complete. Commit/push a draft PR, attach it and publish evidence.
