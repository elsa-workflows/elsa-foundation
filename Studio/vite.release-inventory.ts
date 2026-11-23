import { createHash } from "node:crypto";
import { existsSync, mkdirSync, readFileSync, realpathSync, writeFileSync } from "node:fs";
import { dirname, isAbsolute, join, resolve } from "node:path";
import type { Plugin } from "vite";

// Keep Rollup's retained module owners and inter-package imports outside product outputs.
export function releaseInventory(kind: string): Plugin | false {
  const destination = process.env.STUDIO_INVENTORY;
  if (!destination) return false;
  return {
    name: "studio-release-inventory",
    apply: "build",
    writeBundle(options, bundle) {
      const outDir = resolve(options.dir!);
      const packages = new Map<string, { name: string; version: string; workspace: boolean; directory: string }>();
      const dirs = new Map<string, string | undefined>();
      function owner(directory: string): string | undefined {
        if (dirs.has(directory)) return dirs.get(directory);
        const manifest = join(directory, "package.json");
        if (existsSync(manifest)) {
          const { name, version } = JSON.parse(readFileSync(manifest, "utf8"));
          if (name && version) {
            packages.set(directory, { name, version, workspace: !directory.includes("/node_modules/"), directory });
            dirs.set(directory, directory);
            return directory;
          }
        }
        const parent = dirname(directory);
        const result = parent === directory ? undefined : owner(parent);
        dirs.set(directory, result);
        return result;
      }
      function recordSource(input: string) {
        const file = resolve(input.split("?")[0]);
        return existsSync(file) ? owner(dirname(realpathSync(file))) : undefined;
      }
      const entries = new Set<string>();
      const owners = new Map<string, string>();
      const externals = new Set<string>();
      const files: Record<string, string> = {};
      for (const item of Object.values(bundle)) {
        files[item.fileName] = createHash("sha256").update(readFileSync(join(outDir, item.fileName))).digest("hex");
        if (item.type === "asset") {
          for (const source of item.originalFileNames) recordSource(source);
        } else {
          for (const source of item.moduleIds) {
            if (!isAbsolute(source)) continue;
            const directory = recordSource(source);
            if (directory) owners.set(source, directory);
          }
          if (item.isEntry && item.facadeModuleId && isAbsolute(item.facadeModuleId)) {
            const directory = recordSource(item.facadeModuleId);
            if (directory) {
              entries.add(directory);
              owners.set(item.facadeModuleId, directory);
            }
          }
          for (const imported of [...item.imports, ...item.dynamicImports]) {
            if (!Object.hasOwn(bundle, imported)) externals.add(imported);
          }
        }
      }
      const included = new Map(packages);
      const imports = new Map<string, Set<string>>();
      // Facades and re-export barrels can contribute no rendered bytes. Follow their
      // resolved imports, keeping only relationships between observed package owners.
      // Map iteration includes intermediary modules added during this traversal.
      for (const [source, directory] of owners) {
        const info = this.getModuleInfo(source);
        if (!info) continue;
        for (const id of [...info.importedIds, ...info.dynamicallyImportedIds]) {
          let target = owners.get(id);
          if (!target && isAbsolute(id)) {
            const result = recordSource(id);
            if (result && included.has(result)) {
              target = result;
              owners.set(id, result);
            }
          }
          target ??= externals.has(id) ? id : undefined;
          if (!target || target === directory) continue;
          if (!imports.has(directory)) imports.set(directory, new Set());
          imports.get(directory)!.add(target);
        }
      }
      mkdirSync(destination, { recursive: true });
      const key = createHash("sha256").update(outDir + ":" + kind).digest("hex");
      writeFileSync(join(destination, key + ".json"), JSON.stringify({
        kind, outDir, packages: [...included.values()], entries: [...entries],
        imports: [...imports].map(([from, to]) => [from, [...to]]), externals: [...externals].sort(), files
      }));
    }
  };
}
