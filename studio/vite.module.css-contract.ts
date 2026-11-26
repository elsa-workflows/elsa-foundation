import type { Plugin } from "vite";

// Hashed names make Vite's deferred CSS imports safe across module package upgrades.
export const MODULE_CSS_PATTERN = /^module-[A-Za-z0-9_-]+\.css$/;
export const MODULE_ENTRY_STYLES_FILE = "entry-styles.json";

export function findModuleCssContractViolations(fileNames: string[]): string[] {
  return fileNames
    .filter(name => /\.css$/i.test(name))
    .filter(name => !MODULE_CSS_PATTERN.test(name));
}

/** Emit the exact entry/static-import CSS closure, plus numbered aliases for older hosts. */
export function moduleCssContractPlugin(): Plugin {
  return {
    name: "elsa:module-css-contract",
    apply: "build",
    enforce: "post",
    configResolved(config) {
      if (config.build.lib || !config.build.cssCodeSplit)
        throw new Error("Studio module styles require app-mode ESM output with cssCodeSplit enabled.");
    },
    generateBundle(_, bundle) {
      const violations = findModuleCssContractViolations(Object.keys(bundle));
      if (violations.length > 0)
        this.error(
          `Emitted stylesheet(s) violate the module CSS naming contract: ${violations.join(", ")}. ` +
          `Keep hashed module CSS names or update StudioModuleManifestProvider in lockstep.`
        );

      const styles = new Set<string>();
      const visited = new Set<string>();
      const visit = (file: string) => {
        if (visited.has(file)) return;
        visited.add(file);
        const chunk = bundle[file];
        if (!chunk || chunk.type !== "chunk") this.error(`Missing module chunk ${file}.`);
        // Match ES module dependency evaluation order; dynamic imports deliberately stay deferred.
        for (const imported of chunk.imports) if (bundle[imported]) visit(imported);
        const metadata = (chunk as typeof chunk & { viteMetadata?: { importedCss: Set<string> } }).viteMetadata;
        for (const css of metadata?.importedCss ?? []) {
          if (!bundle[css]) this.error(`Missing module stylesheet ${css}.`);
          styles.add(css);
        }
      };
      for (const chunk of Object.values(bundle))
        if (chunk.type === "chunk" && chunk.isEntry) visit(chunk.fileName);

      this.emitFile({
        type: "asset", fileName: MODULE_ENTRY_STYLES_FILE,
        source: JSON.stringify({ version: 1, styles: [...styles] })
      });

      // Existing hosts enumerate module.css/module2.css/... and load all of them. Keep that
      // behavior when a new module is installed in an older host; new hosts ignore these aliases.
      let index = 0;
      for (const asset of Object.values(bundle)) {
        if (asset.type !== "asset" || !asset.fileName.endsWith(".css")) continue;
        index++;
        this.emitFile({ type: "asset", fileName: `module${index === 1 ? "" : index}.css`, source: asset.source });
      }
    }
  };
}
