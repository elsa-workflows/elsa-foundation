import type { Plugin } from "vite";

/**
 * Build-time guard for the module stylesheet naming contract.
 *
 * The host links module stylesheets by enumerating static web assets that match
 * `^module(\d*)\.css$` flat in `wwwroot/studio/modules/<slug>/` — see `ModuleStylePattern`
 * in `src/Elsa.Studio.Api/Services/StudioModuleManifestProvider.cs`. Any emitted CSS asset
 * outside that naming is silently never served (the PR #440 bug: `cssCodeSplit` output
 * drifted from a hardcoded single `module.css` link). The producer side of the contract is
 * `assetFileNames: "module[extname]"` in `vite.module.base.ts`; this plugin fails the build
 * if the emitted bundle drifts from it — an overridden `assetFileNames`, a Vite/Rollup
 * upgrade changing the collision-suffix scheme (e.g. `module-1.css`), or CSS landing in a
 * subdirectory.
 */

// Stricter than the host's IgnoreCase pattern on purpose: the assetFileNames template only
// ever produces lowercase, so a case variant proves the template was overridden. Everything
// this accepts, the host serves.
export const MODULE_CSS_PATTERN = /^module(\d*)\.css$/;

/**
 * Returns the emitted file names that a Studio host would fail to serve as module styles.
 * Non-CSS files are ignored; `module1.css` is rejected because the host orders it identically
 * to `module.css` (Rollup's collision dedup starts at 2, so it never occurs legitimately).
 */
export function findModuleCssContractViolations(fileNames: string[]): string[] {
  return fileNames
    .filter(name => /\.css$/i.test(name))
    .filter(name => !MODULE_CSS_PATTERN.test(name) || name === "module1.css");
}

export function moduleCssContractPlugin(): Plugin {
  return {
    name: "elsa:module-css-contract",
    apply: "build",
    // Must run after vite:css-post, which emits the CSS assets; a normal-order plugin's
    // generateBundle would see no CSS at all and pass vacuously.
    enforce: "post",
    generateBundle(_, bundle) {
      const violations = findModuleCssContractViolations(Object.keys(bundle));
      if (violations.length > 0)
        this.error(
          `Emitted stylesheet(s) violate the module CSS naming contract: ${violations.join(", ")}. ` +
            `The host only serves stylesheets matching module(\\d*).css flat in the module directory ` +
            `(ModuleStylePattern in src/Elsa.Studio.Api/Services/StudioModuleManifestProvider.cs); ` +
            `anything else is silently dropped. Keep assetFileNames as "module[extname]" ` +
            `(vite.module.base.ts) or update the host contract in lockstep.`
        );
    }
  };
}
