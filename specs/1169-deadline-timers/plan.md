# Implementation Plan: Durable timer deadlines

**Branch**: `agent/codex/3478-deadline-timers` | **Date**: 2026-10-30 | **Spec**: [spec.md](spec.md)

## Summary

Keep durable timers and claims authoritative. Add an optional earliest-eligible-deadline store query, implemented by the built-in in-memory and EF providers. A bounded process-local hint advances the hosted pump's next wakeup after a successful timer save. A non-overlapping schedule uses the earlier of that deadline and the recovery cadence, starts immediately, rediscovers all persistence partitions and suppresses acceleration during whole-sweep failure backoff.

## Technical Context

C#/.NET 10, existing Tasks lifecycle and TimeProvider; no new dependency or project. Existing runtime Core, Runtime, Scheduling and EF persistence and their whole test projects. DurableTimer JSON and relational schema unchanged; query effective eligibility including claim visibility with a bounded result. No benchmark work; the real five-second workflow is a timing correctness witness. Custom stores inherit a default no-deadline response and retain periodic recovery.

## Constitution Check

Framework v6.1.0 and Elsa v6.3.0 gate indexes and contributor checklist reviewed. New public provider/notification contracts belong in Runtime.Core and are additive; track their public API. The singleton notification state is intentionally process-local, bounded and hint-only. No runtime-to-design dependency, schema or feature identity changes. Scheduling implementation internals stay internal. Extend the existing Tasks schedule override seam while preserving all other schedules. Update extension-point docs. Preserve ownership, backoff and valid tests. Deterministic regressions and mutation red/green are required. Draft framework §2.24 and Elsa provisional sections are not used as authority.

## Source Structure

- Runtime.Core contracts: optional deadline query and named deferred timer-scheduled event.
- Runtime Bookmarks: notify after save and in-memory deadline query.
- Runtime EF: bounded scoped deadline query.
- Runtime Scheduling: retained hint, single timer schedule, deadline discovery and feature registration.
- Tasks schedules: virtual schedule factory, preserving default behavior.
- Existing Scheduling, Runtime, Activities Scheduling, Tasks and EF/provider tests; scheduling e2e.

## Execution and Review

Spec reviewed against #3478; owner authorized the recommended design and end-to-end delivery. Plan and tasks reviewed before implementation. Run focused regressions, mutation control, whole affected suites/discovery counts and a private rebuilt fresh-database Workbench scheduling journey. Complete architecture/maps, independent correctness/standards/acceptance review on the draft, external review and exact-head CI/merge gates. Verify resulting main before closing delivery.

## Risks

Use a generation snapshot across async deadline discovery to preserve concurrent earlier notifications. Query effective visibility rather than raw due time to avoid spinning on leased/backed-off timers. Suppress acceleration on every sweep failure, including its first. Disposal prevents rearm and awaits active callbacks. Very short timers keep existing not-found grace behavior. Peer writes without a distributed notification are bounded by recovery.
