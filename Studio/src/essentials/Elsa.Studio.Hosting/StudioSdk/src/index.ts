/**
 * Type/source surface for `@elsa-workflows/studio-sdk`.
 *
 * Studio module clients import the SDK's contribution types, contracts, and runtime helpers from this
 * workspace package for TypeScript resolution at build time, then mark `@elsa-workflows/studio-sdk`
 * external (see the root `vite.module.base.ts`) so the shared runtime copy is resolved through the host
 * import map (`/studio/sdk/index.js`, built from `Client/src/sdk/index.ts`) to one shared instance.
 *
 * The single source of truth lives in the host's SDK barrel (`Client/src/sdk/index.ts`, which itself
 * re-exports the auth surface via `export * from "../auth"`); this package is a thin shell that only
 * re-exports it, so there is no type duplication or drift between what modules compile against and what
 * the host serves at runtime. Publishing this package (`build:lib`) rolls those same declarations up into
 * a standalone `dist/` for external module authors who build outside this monorepo.
 */
export * from "../../Client/src/sdk";
