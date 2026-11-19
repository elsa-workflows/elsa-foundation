# Implementation Plan: Safe Workflow Folder Deletion

**Date**: 2026-11-03 | **Issue**: #3713 | **Spec**: [spec.md](spec.md)

## Design

Specification review: the three scenarios and FR-001–006 cover the approved choice; no clarification remains. Follow the repository's manual specification → plan → tasks → implementation path.

Add an optional provider-neutral folder-deletion store port beside the existing library port. Its authoritative preview returns bounded direct workflow targets and affected folder identities; API authorization runs before exposing a count-only wire projection. New advertised preview/commit routes leave the existing empty-only DELETE route intact. Unsupported providers advertise neither new relation.

EF implements the port on the existing library store, using the existing tenant scope, hierarchy lease/fence and atomic writer. A deterministic preview revision binds selected/destination folders, descendant revisions and direct workflow membership/lifecycle revisions. The API re-reads/authorizes targets at commit; the transaction re-reads and matches that revision before staging anything. Reparent direct child folders, rewrite descendant paths/depths, move direct memberships, optionally mark active definitions soft-deleted, and record audit facts in one commit. Reject destination name collisions. No schema, package or project-reference changes.

Studio adds a focused deletion dialog using existing modal styles/focus handling, authoritative server counts, unchecked choice, count-specific button, busy/cancel/error/reload controls and stale context suppression. Legacy backends retain empty-only deletion with accurate conditional wording rather than asserting emptiness.

## Constitution check

Framework v6.1.0 and Elsa v6.3.0, both Gate indexes and the review checklist loaded. Keep provider logic internal; the new store contract/models are public for third-party persistence implementations. Core takes no new dependency; API consumes only the port. No Runtime → Design edge, publication mutation or authored-state changes. Existing optional-backend replacement registration owns the additional port. Risk-based durable/API/UI tests cover failure/atomicity/staleness; no generic abstraction or new domain. Draft/provisional authored-state rules are not relied on. Public surface is additive; no existing interface member is removed.

## Validation and delivery

Run affected whole persistence/API/frontend suites with discovery/execution counts; prove a meaningful stale/deletion/UI regression red then green. Run architecture restore/guard and maps check. Use an owned temporary normal host and browser for preserve, optional soft-delete, restoration and persistence readback. Review correctness, standards and issue conformance independently on the draft, fix findings, then mark ready with evidence and request review. Required exact-head checks precede merge; qualify resulting main. Preserve all pre-existing hosts and databases.
