# Workflow template management
**Status**: Implemented
**Date**: 2026-10-30
**Issue**: #3456
**Program**: none/free-flow

## User Scenarios & Testing
1. An author saves the current workflow as a named template, finds it under Workflows → Templates, and edits it with the familiar designer. Graph, variables, inputs/outputs, layout and activity labels survive reload/restart.
2. An author chooses a built-in or custom template when creating a workflow. The resulting workflow is independent: later template edits or deletion never change it.
3. Built-in starters can be copied to editable custom templates, but never overwritten or deleted. Blank templates allow choosing the root activity. Missing activities produce a useful explanation instead of silently creating an incomplete workflow.

## Functional Requirements
- FR-001: Provide a persistent, tenant-scoped custom template catalog with name, description, authored state, layout and activity labels.
- FR-002: Expose list/read/create/update/delete through authenticated Design APIs. Reuse Design permissions, atomic persistence and optimistic concurrency; stale edits must not overwrite newer content.
- FR-003: Provide a searchable Templates management view with create, edit, copy, delete and create-workflow actions. Built-ins are visibly read-only.
- FR-004: Offer Save as template on an existing workflow, capturing current authored content including unsaved edits.
- FR-005: Reuse the workflow designer for template editing; templates cannot be published or executed as workflows.
- FR-006: Populate Starting point from the catalog; clone selected content and retain layout/presentation. Preserve blank-root selection and existing HTTP starter availability checks.
- FR-007: Creating/copying/deleting a template never changes previously created workflows. Templates never enter runtime executable/template stores or normal workflow inventories.
- FR-008: Handle unavailable APIs, stale revision conflicts, missing dependencies and failed saves without losing local edits. Built-ins remain usable on older hosts.

## Key Entities
Workflow authoring template: identity, tenant, revision, name/description and authored document/presentation. Built-in recipe: stable identity and code-owned graph construction. Workflow created from a template: independent design resource.

## Assumptions and Scope
Custom templates are mutable authoring snapshots, without a separate publication/version lifecycle. Existing activity version identities remain pinned. Built-in recipes use the connected server catalog and are copied into stored snapshots when customized. No runtime changes or automatic activity upgrades.

## Success Criteria
Every create/save/edit/copy/delete/use action works through real APIs and survives SQLite restart. Concurrent edits are refused safely. Built-in mutation is refused. Existing workflows remain independent. Light/dark browser journeys preserve graph and declaration editing without template publication/run actions.
