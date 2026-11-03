# Tasks: Safe Workflow Folder Deletion

**Issue**: #3713 | **Plan**: [plan.md](plan.md)

Plan review: optional provider support, count-only authorization boundary, transaction-time preview validation and soft-deletion semantics satisfy the spec without schema changes.

- [ ] T001 Add preview/delete port and EF implementation with atomic preservation, soft deletion, collision and stale guards.
- [ ] T002 Add authorized capability-discovered API routes and tests for optional/disabled support and every affected resource.
- [ ] T003 Add Studio dialog and API wiring with exact counts, explicit choice, errors/reload and focus; retain safe legacy behavior.
- [ ] T004 Add durable and rendered regressions; prove red/green and run whole affected suites.
- [ ] T005 Qualify an owned normal host/browser journey, restart/readback, architecture/maps and independent draft review.
- [ ] T006 Open PR with acceptance evidence, resolve review/CI, deliver merge and resulting-main qualification.
