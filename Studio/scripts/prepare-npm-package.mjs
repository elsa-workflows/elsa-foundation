// prepare-npm-package.mjs — rewrite a workspace package.json into a publishable manifest.
//
// WHY this exists (and why it's a script, not just `publishConfig` fields):
//
// On disk every Studio workspace package stays `private: true` and points its `exports`/
// `types`/`main` at raw `src/` (e.g. `./src/index.ts`). That is exactly what the in-repo dev
// flow wants: workspace consumers (`@elsa-workflows/studio-workflows` -> `studio-ui` etc. via
// `workspace:*`) resolve each other's TypeScript sources directly, with no build step, so
// edits are picked up live and there is a single source of truth. The compiled `dist/` output
// only exists in CI after `build:lib` runs.
//
// npm's own `publishConfig` field-override mechanism cannot express what we need here:
//   - it CANNOT flip `private: true` -> publishable (npm refuses to publish a private package,
//     publishConfig or not), and
//   - `publishConfig` override coverage for fields like `exports`/`main`/`types` has been
//     inconsistent across npm versions, so relying on it is fragile.
// So instead of half-trusting publishConfig, CI checks out the repo, runs this script to
// produce a clean, dist-pointing, non-private manifest in place, then packs. The on-disk
// source manifests are never mutated in a way that leaks into a commit — only the ephemeral CI
// checkout is rewritten, just before `pnpm pack`.
//
// Usage: node scripts/prepare-npm-package.mjs <pkgDir> <version>

import { readFileSync, writeFileSync, existsSync } from "node:fs";
import { resolve, join, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const scriptDir = dirname(fileURLToPath(import.meta.url));
const repoRoot = resolve(scriptDir, "..");

function fail(message) {
  console.error(`prepare-npm-package: ${message}`);
  process.exit(1);
}

const [, , pkgDirArg, versionArg] = process.argv;

// --- Validate arguments up front, fail loud on anything malformed. ---
if (!pkgDirArg) fail("missing <pkgDir> argument (usage: node scripts/prepare-npm-package.mjs <pkgDir> <version>)");
if (!versionArg) fail("missing <version> argument (usage: node scripts/prepare-npm-package.mjs <pkgDir> <version>)");

// A loose semver-ish check: MAJOR.MINOR.PATCH with an optional -prerelease / +build suffix.
// We deliberately tolerate the CI form `4.0.0-preview.123`.
const semverish = /^\d+\.\d+\.\d+(?:[-+][0-9A-Za-z-.]+)?$/;
if (!semverish.test(versionArg)) fail(`version "${versionArg}" is not semver-ish (expected e.g. 4.0.0 or 4.0.0-preview.123)`);

const pkgDir = resolve(pkgDirArg);
const manifestPath = join(pkgDir, "package.json");
if (!existsSync(manifestPath)) fail(`no package.json found at ${manifestPath}`);

let pkg;
try {
  pkg = JSON.parse(readFileSync(manifestPath, "utf8"));
} catch (err) {
  fail(`could not parse ${manifestPath}: ${err.message}`);
}

if (typeof pkg.name !== "string" || !pkg.name.startsWith("@elsa-workflows/")) {
  fail(`manifest at ${manifestPath} has no @elsa-workflows/-scoped name (found: ${JSON.stringify(pkg.name)})`);
}

// --- Rewrite the manifest into publishable shape. ---
pkg.version = versionArg;
delete pkg.private;

pkg.main = "./dist/index.js";
pkg.module = "./dist/index.js";
pkg.types = "./dist/index.d.ts";

// Base subpath export. Extra CSS subpaths are added below for the packages that emit them.
const exportsMap = {
  ".": {
    types: "./dist/index.d.ts",
    import: "./dist/index.js",
    default: "./dist/index.js"
  }
};
// studio-ui ships design tokens; studio-workflows ships a bundled stylesheet.
if (pkg.name.endsWith("studio-ui")) exportsMap["./tokens.css"] = "./dist/tokens.css";
if (pkg.name.endsWith("studio-workflows")) exportsMap["./style.css"] = "./dist/style.css";
pkg.exports = exportsMap;

pkg.files = ["dist"];
pkg.sideEffects = false;

// Rewrite intra-monorepo workspace deps (workspace:*, workspace:^, workspace:~, workspace:<ver>)
// to the exact version being published — all four packages publish together in one run, so they
// pin to each other by exact version.
for (const depKey of ["dependencies", "peerDependencies", "optionalDependencies"]) {
  const deps = pkg[depKey];
  if (!deps) continue;
  for (const [name, range] of Object.entries(deps)) {
    if (name.startsWith("@elsa-workflows/") && typeof range === "string" && range.startsWith("workspace:")) {
      deps[name] = versionArg;
    }
  }
}

// Hygiene: neither of these belongs in a published tarball's manifest.
delete pkg.devDependencies;
delete pkg.scripts;

pkg.publishConfig = { access: "public" };

// Copy license/repository from the root manifest when the package omits them — but only if the
// root actually declares them. We never invent these values.
const rootManifestPath = join(repoRoot, "package.json");
if (existsSync(rootManifestPath)) {
  try {
    const rootPkg = JSON.parse(readFileSync(rootManifestPath, "utf8"));
    if (rootPkg.license && !pkg.license) pkg.license = rootPkg.license;
    if (rootPkg.repository && !pkg.repository) pkg.repository = rootPkg.repository;
  } catch (err) {
    fail(`could not parse root package.json at ${rootManifestPath}: ${err.message}`);
  }
}

writeFileSync(manifestPath, `${JSON.stringify(pkg, null, 2)}\n`, "utf8");
console.log(`prepare-npm-package: rewrote ${pkg.name}@${versionArg} manifest at ${manifestPath}`);
