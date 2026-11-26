# Research: Elsa Studio UI System

## Decision: Own the Elsa Studio design language

**Rationale**: Elsa Studio is modular and needs contributed pages to feel native.
Adopting an opinionated component library wholesale would make the product feel
like that library and would push module authors toward library-specific visual
workarounds.

**Alternatives considered**:

- Ant Design: complete enterprise surface, but visually dominant.
- MUI: mature and comprehensive, but Material Design is not the desired product
  language.
- Mantine: fast to build with, but still brings a recognizable external style.

## Decision: Use shared primitives plus token-driven CSS

**Rationale**: CSS variables already exist in the shell and are the right
contract for modules that ship separately built assets. Component CSS keeps the
design language inspectable and avoids making utility-class strings the module
authoring contract.

**Alternatives considered**:

- Tailwind as public contract: productive, but module authors would need to copy
  class recipes and the design language would be harder to govern.
- CSS-in-JS: unnecessary runtime and build coupling for same-origin ESM modules.

## Decision: Add behavior libraries narrowly

**Rationale**: Tables and complex primitives need robust behavior, but Elsa
should own markup, density, tokens, and visual styling. TanStack Table is a good
fit for grid state because it is headless. Headless accessible primitives are
appropriate for dialogs, drawers, menus, popovers, tabs, selects, and tooltips
when native controls are insufficient.

**Alternatives considered**:

- Hand-roll data grid behavior: likely to drift and miss edge cases.
- Full grid component with built-in styling: faster initially, but harder to
  align with the Workbench language.

## Decision: Prove through Feature Management and Module Management

**Rationale**: Feature management exercises toggles, settings, validation, dirty
state, and split inspector behavior. Module management exercises backend module
metadata, compatibility, contributions, diagnostics, and resource grids.

**Alternatives considered**:

- Build an isolated component gallery first: useful later, but not enough proof
  that the system works in real module routes.
- Redesign the dashboard first: lower value and less representative of the
  modular admin workloads.

## Stakeholder Notes

- The exploratory mockups were strongly approved; stakeholder phrasing: "your
  mockups are fire."
- The first two screens after the foundation are Feature Management and Module
  Management, including backend modules as required.
