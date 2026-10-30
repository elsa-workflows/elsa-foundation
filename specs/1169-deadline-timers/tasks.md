# Tasks: Durable timer deadlines

## Definition and design

- [x] T001 Claim #3478, verify competing work, define/review spec and plan. Program route none/free-flow.
- [x] T002 Research claim visibility, persistence scopes, startup and notification/query races.

## User stories 1 and 2

- [x] T003 Add compatible provider deadline query, post-save wakeup seam and built-in implementations with focused regressions.
- [x] T004 Implement retained hint and non-overlapping deadline schedule with startup/recovery, backoff and disposal coverage.
- [x] T005 Wire pump discovery/feature registration, public APIs and extension-point docs.
- [x] T006 Prove regressions red under mutation then green; run whole affected suites and discovery counts.

## Delivery

- [x] T007 Rebuild private fresh-DB Workbench; qualify five-second Delay, restart and scheduling e2e; retain evidence.
- [x] T008 Run architecture/maps and required restore checks.
- [ ] T009 Open draft PR; independent correctness, standards and acceptance review loop; repair concrete findings and repeat evidence.
- [ ] T010 Mark ready, request external review, verify exact-head CI, merge under the applicable org protocol and verify resulting main.
