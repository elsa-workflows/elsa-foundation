<!--
Sync Impact Report
Version change: template -> 1.0.0
Modified principles: template placeholders -> Elsa Studio UI system principles
Added sections: Design System Contract, Spec-Driven Delivery Workflow
Removed sections: none
Templates requiring updates:
- ✅ .specify/templates/plan-template.md
- ✅ .specify/templates/spec-template.md reviewed; no changes required
- ✅ .specify/templates/tasks-template.md reviewed; no changes required
Follow-up TODOs: none
-->
# Elsa Foundation Studio Constitution

## Core Principles

### I. Modular UI Is A Product Contract
Elsa Studio is a modular product surface. Every module-contributed page, panel,
route, widget, setting editor, and diagnostic view MUST feel native to the host.
New feature work MUST use shared Studio UI primitives before introducing local
layout, typography, status, form, or navigation patterns. A module MAY introduce
specialized UI only when the shared system cannot express the workflow, and the
plan MUST document why the new primitive belongs outside the shared system.

### II. Workbench Patterns Over Dashboard Decoration
Resource-heavy pages MUST default to the Modular Admin Workbench language:
data grid or resource list, compact toolbar, summary strip when useful, and
inspector/drawer for detail. Configuration-heavy pages MUST default to split
inspector or grouped settings patterns. Diagnostics and operations pages MUST
prefer dense rows, timelines, severity filters, and console/log panels over
large cards. Cards are allowed for repeated dashboard widgets and empty states,
but MUST NOT be the default page container model.

### III. Typography, Density, And Tokens Are Governed
UI typography MUST remain compact, professional, and token-driven. Page titles
SHOULD stay in the 18-22px range unless a documented workflow requires larger
display text. Controls, rows, labels, sidebars, tabs, tables, and inspector
chrome MUST define intentional font size, weight, and line-height. Colors,
radii, spacing, borders, focus rings, shadows, and semantic statuses MUST come
from Studio design tokens rather than one-off module CSS.

### IV. Accessible Primitives, Predictable Interaction
Interactive primitives MUST support keyboard operation, focus visibility,
screen-reader semantics, disabled/loading states, and validation feedback.
Shared controls such as dialogs, drawers, tabs, menus, selects, switches,
tooltips, and tables SHOULD be built on native semantics or accessible headless
primitives. Data-heavy components MUST support predictable selection, sorting,
filtering, pagination, loading, empty, error, and bulk-action states where those
states are relevant.

### V. Incremental Proof Through Real Screens
Design system work MUST be proven through real Studio screens, not only isolated
component demos. The initial implementation order is: shared UI foundation,
feature management, then module management including backend modules as needed.
Each implementation slice MUST include focused tests and browser verification
for the changed workflow. Visual changes MUST be checked against the accepted
Workbench direction: Minimal Data Grid as the default information architecture,
Split Inspector as the default interaction pattern, and Command Center behavior
in the shell.

## Design System Contract

The shared UI system MUST provide stable module-facing primitives for:

- App shell, sidebar navigation, top command bar, and backend/environment status.
- Page header, toolbar, filters, segmented controls, search, and action groups.
- Summary strip, data grid, resource list, pagination, and row selection.
- Inspector, drawer, tabs, grouped metadata, and sticky action footer.
- Form field, setting group, switch, select, input, secret field, textarea,
  validation message, alert banner, status chip, and empty/loading/error state.
- Bottom panel, console/log rows, severity chips, and diagnostics timelines.

The shared UI contract MUST document which CSS variables and component props
modules can rely on. Module authors MUST NOT need to copy host CSS internals to
produce native screens.

## Spec-Driven Delivery Workflow

Substantial UI-system work MUST use Spec Kit artifacts before implementation:

1. Constitution compliance is checked before planning and after design.
2. Specifications describe user value, module extensibility, and acceptance
   scenarios without leaking implementation detail.
3. Plans record technology choices, component contracts, migration boundaries,
   testing strategy, and visual verification gates.
4. Tasks are grouped by independently testable user stories.
5. Follow-up priorities and stakeholder notes are recorded in the relevant
   spec or plan so design intent survives across implementation slices.

## Governance

This constitution supersedes ad hoc UI conventions in module code and local CSS.
Amendments require an update to this file, a version change, and review of the
Spec Kit templates for alignment. Versioning follows semantic versioning:
MAJOR for incompatible governance changes, MINOR for new or materially expanded
principles, and PATCH for clarifications. Plans and reviews MUST explicitly
state whether a change complies with this constitution or justify any temporary
exception in the plan's complexity tracking section.

**Version**: 1.0.0 | **Ratified**: 2026-06-16 | **Last Amended**: 2026-06-16
