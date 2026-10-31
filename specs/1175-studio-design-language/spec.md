# Studio design language

**Status**: In progress

Owner request: audit Studio, plan the correction, and deliver consistent UI across installed,
disabled and future third-party modules. Tracking: [#3511](https://github.com/elsa-workflows/elsa-foundation/issues/3511),
under [Elsa 4 Simplification](../../docs/program-goals/elsa-4-simplification.md).

## Outcomes

1. Every routed page and non-route UI contribution has an explicit disposition in the
   [audit and migration inventory](../../docs/reports/studio-design-language-audit.md).
2. Equivalent components have one visual language: page spacing, heading scale, action hierarchy,
   collection controls, row treatment, pagination, feedback, fields and sections. Themes retain
   their identity through existing semantic tokens.
3. Public `@elsa-workflows/studio-ui` components and CSS work without host-private imports or copied
   stylesheet rules. Third-party authors get a small composable API, working examples and guidance.
4. Repository modules adopt these patterns whether or not installed in the reference host.
5. Domain behavior remains intact: permissions, masked values, server cursors and truthful counts,
   deferred filter application, selection, confirmations, draft persistence, undo and read-only
   authoring. Dense diagnostic streams and graph workspaces retain purposeful layouts.

## Acceptance

- Collection families share page/header, toolbar, table/row and footer treatments in light/dark
  themes, at desktop and narrow widths. Overflow belongs to the collection, not the viewport.
- Equivalent actions have equivalent emphasis. One primary page action, visible text for common
  actions, accessible names for icon controls, sentence-case UI headings and column headings.
- Empty, loading, error and filtered-empty states remain distinguishable, accessible and actionable.
- Settings, detail, import and authoring pages use the same typography and controls while retaining
  the information architecture needed for their task.
- A consumer of the prepared npm package renders the patterns with supported CSS imports alone;
  declarations and package exports include the new primitives/styles.
- Existing behavior suites pass, and browser checks verify actual rendered cross-module invariants.

The normative contract stays in [Studio UX design specification](../../Studio/docs/ux/design-spec.md)
and [design tokens](../../Studio/docs/design-tokens.md); this spec does not define a second system.
