# Tagging Owns Vocabulary And Target Domains Own Assignments

Status: accepted (2026-07-19; ratified through the workflow-tagging design grilling session)

## Context

Elsa needs one reusable tenant tagging vocabulary that can classify workflow definitions first and
other resource kinds later. Keeping the whole feature inside Workflow Design would duplicate or
require migrating that vocabulary when another domain adopts tags. Letting a universal tagging
store own polymorphic resource attachments would instead weaken target-domain authorization,
lifecycle cleanup, referential integrity, and query ownership.

## Decision

The Tagging domain therefore owns tag definitions, controlled values, normalization, value
semantics, lifecycle, and target-kind eligibility. It does not own, mutate, or interpret target
resources. Each target domain owns its assignments and query projections. The first integration is
`WorkflowDefinitionTagAssignment` in Workflow Design, attached to the logical workflow definition
rather than authored `WorkflowDefinitionState`, an immutable workflow version, or a runtime
artifact.

Tagging exposes provider-neutral catalog contracts. Workflow Design exposes provider-neutral
assignment and workflow-definition query contracts. The concrete durable implementations shipped
by this repository use Groundwork in accordance with
[ADR 0042](0042-elsa-foundation-ships-only-groundwork-persistence-implementations.md), but
Groundwork types do not cross either domain's core boundary.

## Consequences

This split keeps one vocabulary while allowing each target domain to enforce its own visibility,
authorization, deletion, reconciliation, persistence, and filtering rules. Cross-resource
reporting, if later required, consumes domain-owned projections instead of moving resource
ownership into Tagging.

Adding another taggable resource kind requires a target-owned assignment integration and query
projection. It is deliberately more work than inserting a polymorphic row, because that work is
where the target's authorization, lifecycle, and consistency rules become explicit.
