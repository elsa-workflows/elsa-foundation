// check-module-layout.mjs — assert that every module on disk is actually selected by the
// things that select modules.
//
// Almost everything that picks up a Studio module does so BY PATH: the pnpm workspace globs,
// the solution file, the stylelint glob, the .gitignore pattern for generated assets. Each of
// those fails by finding LESS, silently, which is how a module can leave the build without a
// single red check. Measured instances (issue #515):
//
//   - `lint:css` is pinned to `src/*/Client/src/**/*.css`. A module at a different depth is
//     simply not linted: 21 files -> 20, exit 0, no warning. The --studio-* token contract
//     stops being enforced for it.
//   - `.gitignore` has `src/*/wwwroot/`. A module at a different depth has its generated Vite
//     output become committable — this one fails by finding MORE.
//   - `pnpm install --frozen-lockfile` exits 0 when a package dir leaves the workspace globs.
//     For a package with no in-repo consumer nothing fails at all; it silently stops being
//     built, typechecked and tested.
//   - Dropping a project from Elsa.Studio.slnx (rather than repathing it) makes `dotnet pack`
//     emit 15 nupkgs instead of 16, exit 0, no warning — a package silently stops shipping.
//
// This guard is the one thing that notices. It is deliberately layout-agnostic: it discovers
// what is on disk and checks the selectors against that, so it holds both for today's flat
// `src/<Module>/` tree and for the `src/essentials|apps|extensions/<Module>/` tree of #515.

import { execFileSync } from "node:child_process";
import { existsSync, readFileSync, readdirSync } from "node:fs";
import { dirname, join, relative, resolve } from "node:path";
import stylelint from "stylelint";

const repoRoot = resolve(import.meta.dirname, "..");
const read = path => readFileSync(join(repoRoot, path), "utf8");
const rel = path => relative(repoRoot, path).split("\\").join("/");

const SKIP_DIRS = new Set(["node_modules", "dist", "build", "coverage", "bin", "obj", "wwwroot"]);

/** Every file matching `predicate` beneath `roots`, skipping generated and vendored trees. */
function* walk(roots, predicate) {
  for (const root of roots.map(r => join(repoRoot, r)).filter(existsSync)) {
    const stack = [root];
    while (stack.length) {
      for (const entry of readdirSync(stack.pop(), { withFileTypes: true })) {
        const path = join(entry.parentPath, entry.name);
        if (entry.isDirectory()) {
          if (!SKIP_DIRS.has(entry.name)) stack.push(path);
        } else if (predicate(entry.name)) {
          yield path;
        }
      }
    }
  }
}

const projects = [...walk(["src", "tests"], name => name.endsWith(".csproj"))].map(rel).sort();
const packages = [...walk(["src", "examples"], name => name === "package.json")].map(rel).sort();

// A module's root is the directory the .csproj lives in, or — for a Client-only module such as
// CodeEditor — the parent of the package directory. This is what owns the generated `wwwroot/`.
const moduleRoots = [
  ...new Set([
    ...projects.filter(p => p.startsWith("src/")).map(dirname),
    ...packages.filter(p => p.startsWith("src/")).map(p => dirname(dirname(p)))
  ])
].sort();

// The deployable host is its own category: it is excluded from the module CSS contract, and it
// is the one place where referencing an extension is legitimate. Matching on the project name
// rather than a path keeps this true across both layouts.
const isHostApp = path => path.includes("Elsa.Studio.Web");

const failures = [];
const fail = (assertion, detail) => failures.push({ assertion, detail });

// ── 1. every workspace package is in pnpm-workspace.yaml ────────────────────────────────────
// A missing entry is the silent case: install still exits 0 and the package drops out of
// `pnpm -r`. Consumers of a dropped package fail later and blame themselves, not the move.
{
  // Only the `packages:` block — the file also carries `onlyBuiltDependencies:`, whose entries
  // are package names rather than paths.
  const lines = read("pnpm-workspace.yaml").split("\n");
  const start = lines.findIndex(line => /^packages:\s*$/.test(line));
  if (start < 0) fail("pnpm-workspace", "pnpm-workspace.yaml has no `packages:` block");
  const listed = new Set();
  for (const line of lines.slice(start + 1)) {
    if (/^\S/.test(line)) break;
    const entry = line.match(/^\s+-\s*"?([^"#]+?)"?\s*$/)?.[1];
    if (entry) listed.add(entry);
  }
  for (const manifest of packages) {
    const dir = dirname(manifest);
    if (!listed.has(dir)) fail("pnpm-workspace", `${dir} has a package.json but is not listed in pnpm-workspace.yaml`);
  }
  for (const entry of listed) {
    if (!existsSync(join(repoRoot, entry, "package.json")))
      fail("pnpm-workspace", `pnpm-workspace.yaml lists ${entry}, which has no package.json`);
  }
}

// ── 2. every project is in Elsa.Studio.slnx ─────────────────────────────────────────────────
// A dropped entry still builds (the host pulls it in transitively) but silently stops packing.
{
  const solution = read("Elsa.Studio.slnx");
  const listed = new Set([...solution.matchAll(/<Project\s+Path="([^"]+)"/g)].map(m => m[1].split("\\").join("/")));
  for (const project of projects)
    if (!listed.has(project)) fail("solution", `${project} is not referenced by Elsa.Studio.slnx`);
  for (const entry of listed)
    if (!existsSync(join(repoRoot, entry))) fail("solution", `Elsa.Studio.slnx references ${entry}, which does not exist`);
}

// ── 3. generated wwwroot is ignored for every module ────────────────────────────────────────
// This one inverts: an unmatched module starts committing its build output.
{
  // `git check-ignore` matches patterns, so the probe path does not need to exist.
  const isIgnored = path => {
    try {
      execFileSync("git", ["check-ignore", "-q", path], { cwd: repoRoot, stdio: "ignore" });
      return true;
    } catch {
      return false;
    }
  };

  for (const moduleRoot of moduleRoots) {
    const probe = `${moduleRoot}/wwwroot/studio/probe.js`;
    if (!isIgnored(probe))
      fail("gitignore", `${moduleRoot}/wwwroot/ is not gitignored — generated assets would be committed`);
  }
}

// ── 4. every module stylesheet is actually linted ───────────────────────────────────────────
// Run the real `lint:css` globs through stylelint and compare with what is on disk, rather
// than reasoning about the glob. A hole here silently unenforces the design-token contract.
{
  const script = JSON.parse(read("package.json")).scripts["lint:css"];
  const globs = [...script.matchAll(/"([^"]+)"/g)].map(m => m[1]);
  const result = await stylelint.lint({ files: globs, configFile: join(repoRoot, ".stylelintrc.json") });
  const linted = new Set(result.results.map(r => rel(r.source)));

  const onDisk = [...walk(["src"], name => name.endsWith(".css"))]
    .map(rel)
    .filter(path => /\/Client\/src\//.test(path) && !isHostApp(path));

  for (const css of onDisk)
    if (!linted.has(css)) fail("lint:css", `${css} is module CSS but is not covered by the lint:css globs`);
}

// ── 5. the tier boundary, once tiers exist ──────────────────────────────────────────────────
// Vacuous until #515 lands, and deliberately so: it must not pass by accident afterwards, so a
// partially-created tier set is itself a failure.
{
  const tier = path => path.match(/^src\/(essentials|apps|extensions)\//)?.[1] ?? null;
  const tiered = projects.concat(packages).filter(p => tier(p));

  if (tiered.length > 0) {
    for (const required of ["src/essentials", "src/apps", "src/extensions"])
      if (!existsSync(join(repoRoot, required)))
        fail("tiers", `${required} is missing while other tier folders exist — half-migrated tree`);

    for (const project of projects.filter(p => tier(p) === "essentials")) {
      const includes = [...read(project).matchAll(/<ProjectReference\s+Include="([^"]+)"/g)].map(m => m[1]);
      for (const include of includes) {
        const target = rel(resolve(join(repoRoot, dirname(project)), include.split("\\").join("/")));
        if (tier(target) === "extensions")
          fail("boundary", `${project} is an essential but references the extension ${target}`);
      }
    }

    const packageDir = new Map(
      packages.map(manifest => [JSON.parse(read(manifest)).name, dirname(manifest)])
    );
    for (const manifest of packages.filter(p => tier(p) === "essentials")) {
      const deps = JSON.parse(read(manifest));
      for (const group of ["dependencies", "devDependencies", "peerDependencies"]) {
        for (const [name, range] of Object.entries(deps[group] ?? {})) {
          if (typeof range === "string" && range.startsWith("workspace:") && tier(packageDir.get(name) ?? "") === "extensions")
            fail("boundary", `${manifest} is an essential but depends on the extension package ${name}`);
        }
      }
    }
  }
}

if (failures.length > 0) {
  console.error(`check-module-layout: ${failures.length} problem(s)\n`);
  for (const { assertion, detail } of failures) console.error(`  [${assertion}] ${detail}`);
  console.error(
    "\nEach of these is a selector that would otherwise fail silently — see the comment at the top\n" +
      "of scripts/check-module-layout.mjs for what each one costs when it goes unnoticed."
  );
  process.exit(1);
}

console.log(
  `check-module-layout: ok — ${projects.length} projects, ${packages.length} packages, ${moduleRoots.length} module roots`
);
