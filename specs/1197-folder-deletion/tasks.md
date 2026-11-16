# Tasks: Safe Workflow Folder Deletion

**Issue**: #3713 | **Plan**: [plan.md](plan.md)

Plan review: optional provider support, count-only authorization boundary, transaction-time preview validation and soft-deletion semantics satisfy the spec without schema changes.

- [x] T001 Add preview/delete port and EF implementation with atomic preservation, soft deletion, collision and stale guards.
- [x] T002 Add authorized capability-discovered API routes and tests for optional/disabled support and every affected resource.
- [x] T003 Add Studio dialog and API wiring with exact counts, explicit choice, errors/reload and focus; retain safe legacy behavior.
- [x] T004 Add durable and rendered regressions; prove red/green and run whole affected suites.
- [x] T005 Qualify an owned normal host/browser journey, restart/readback, architecture/maps and independent draft review.
- [x] T006 Open PR #3716, complete the independent author review and post acceptance/gate evidence. Track exact-head CI, merge and resulting-main qualification on #3713 / its PR.
