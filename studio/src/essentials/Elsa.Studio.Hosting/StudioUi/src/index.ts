/**
 * Type/source surface for `@elsa-workflows/studio-ui`.
 *
 * Module clients import components from this workspace package for TypeScript
 * resolution at build time, then mark `@elsa-workflows/studio-ui` external so the
 * shared runtime copy is resolved through the host import map
 * (`/studio/vendor/studio-ui.js`, built by `Client/vite.vendor.config.ts`).
 *
 * The single source of truth lives in the host's shared UI barrel; this package only
 * re-exports it so there is no component duplication or drift.
 */
export * from "../../Client/src/app/ui/shared";
