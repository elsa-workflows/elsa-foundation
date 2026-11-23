import { createHash } from "node:crypto";
import { existsSync, mkdirSync, readFileSync, realpathSync, writeFileSync } from "node:fs";
import { dirname, isAbsolute, join, resolve } from "node:path";
import type { Plugin } from "vite";

// The release build captures the modules Rollup retained and the imports it left
// to the consuming host. Keep the intermediate records outside distributable files.
export function releaseInventory(kind: string): Plugin | false {
  const destination = process.env.STUDIO_INVENTORY;
  if (!destination) return false;
  return {
    name: "studio-release-inventory",
    apply: "build",
    writeBundle(options, bundle) {
      const outDir = resolve(options.dir!);
      const packages = new Map<string, { name: string; version: string; workspace: boolean }>();
      const externals = new Set<string>();
      const files: Record<string, string> = {};
      const visited = new Set<string>();
      function recordSource(input: string) {
        const file = resolve(input.split("?")[0]);
        if (!existsSync(file)) return; // Vite and Rollup also supply virtual module IDs.
        const source = realpathSync(file);
        let directory = dirname(source);
        while (!visited.has(directory)) {
          visited.add(directory);
          const manifest = join(directory, "package.json");
          if (existsSync(manifest)) {
            const { name, version } = JSON.parse(readFileSync(manifest, "utf8"));
            if (name && version) {
              packages.set(name + "@" + version, { name, version, workspace: !source.includes("/node_modules/") });
              return;
            }
          }
          const parent = dirname(directory);
          if (directory === parent) break;
          directory = parent;
        }
      }
      for (const item of Object.values(bundle)) {
        files[item.fileName] = createHash("sha256").update(readFileSync(join(outDir, item.fileName))).digest("hex");
        if (item.type === "asset") {
          for (const source of item.originalFileNames) recordSource(source);
        } else {
          for (const source of item.moduleIds) if (isAbsolute(source)) recordSource(source);
          for (const imported of [...item.imports, ...item.dynamicImports]) {
            if (!Object.hasOwn(bundle, imported)) externals.add(imported);
          }
        }
      }
      mkdirSync(destination, { recursive: true });
      const key = createHash("sha256").update(outDir + ":" + kind).digest("hex");
      writeFileSync(join(destination, key + ".json"), JSON.stringify({
        kind, outDir, packages: [...packages.values()], externals: [...externals].sort(), files
      }));
    }
  };
}
