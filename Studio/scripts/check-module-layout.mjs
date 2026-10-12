// Assert that every Studio module is selected by the solution, pnpm workspace, CSS lint, and
// generated-asset ignore rules after optional modules move beside their Foundation capabilities.
import { execFileSync } from "node:child_process";
import { existsSync, readFileSync, readdirSync } from "node:fs";
import { dirname, join, relative, resolve } from "node:path";
import stylelint from "stylelint";

const studioRoot = resolve(import.meta.dirname, "..");
const repoRoot = resolve(studioRoot, "..");
const readStudio = path => readFileSync(join(studioRoot, path), "utf8");
const rel = path => relative(repoRoot, path).split("\\").join("/");

const SKIP_DIRS = new Set(["node_modules", "dist", "build", "coverage", "bin", "obj", "wwwroot"]);
const extensionStudioRoots = readdirSync(join(repoRoot, "Extensions"), { withFileTypes: true })
  .filter(entry => entry.isDirectory())
  .map(entry => join(repoRoot, "Extensions", entry.name, "studio"))
  .filter(existsSync);
const moduleSourceRoots = [join(studioRoot, "src"), ...extensionStudioRoots];

/** Every file matching `predicate` beneath `roots`, skipping generated and vendored trees. */
function* walk(roots, predicate) {
  for (const root of roots.filter(existsSync)) {
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

const projects = [
  ...walk(moduleSourceRoots, name => name.endsWith(".csproj")),
  ...walk([join(studioRoot, "tests")], name => name.endsWith(".csproj"))
].map(rel).sort();
const packages = [
  ...walk(moduleSourceRoots, name => name === "package.json"),
  ...walk([join(studioRoot, "examples")], name => name === "package.json")
].map(rel).sort();
const packageManifests = new Map(packages.map(path => [
  JSON.parse(readFileSync(join(repoRoot, path), "utf8")).name,
  path
]));

// A module's root is the directory containing its project, or — for a Client-only module —
// the parent of the package directory. Generated Vite output lives here.
const moduleRoots = [...new Set([
  ...projects.filter(path => path.startsWith("Studio/src/") || path.startsWith("Extensions/"))
    .map(path => join(repoRoot, dirname(path))),
  ...packages.filter(path => path.includes("/Client/package.json"))
    .map(path => join(repoRoot, dirname(dirname(path))))
])].sort();

const failures = [];
const fail = (assertion, detail) => failures.push({ assertion, detail });

// ── 1. every workspace package is selected by pnpm-workspace.yaml ──────────────────────────
{
  const lines = readStudio("pnpm-workspace.yaml").split("\n");
  const start = lines.findIndex(line => /^packages:\s*$/.test(line));
  if (start < 0) fail("pnpm-workspace", "pnpm-workspace.yaml has no `packages:` block");

  const listed = [];
  for (const line of lines.slice(start + 1)) {
    if (/^\S/.test(line)) break;
    const entry = line.match(/^\s+-\s*"?([^"#]+?)"?\s*$/)?.[1];
    if (entry) listed.push(entry);
  }

  const matches = (pattern, path) => {
    const expression = pattern.split("*").map(part => part.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")).join("[^/]*");
    return new RegExp(`^${expression}$`).test(path);
  };
  const packageDirs = packages.map(path => relative(studioRoot, join(repoRoot, dirname(path))).split("\\").join("/"));

  for (const dir of packageDirs)
    if (!listed.some(pattern => matches(pattern, dir)))
      fail("pnpm-workspace", `${dir} has a package.json but is not selected by pnpm-workspace.yaml`);
  for (const pattern of listed)
    if (!packageDirs.some(dir => matches(pattern, dir)))
      fail("pnpm-workspace", `${pattern} selects no package.json`);
}

// ── 2. every project is in Elsa.Studio.slnx and every project reference resolves ───────────
{
  const solution = readStudio("Elsa.Studio.slnx");
  const listed = [...solution.matchAll(/<Project\s+Path="([^"]+)"/g)]
    .map(match => match[1].split("\\").join("/"));
  const projectPaths = new Set(projects);

  for (const project of projects) {
    const solutionPath = relative(studioRoot, join(repoRoot, project)).split("\\").join("/");
    if (!listed.some(path => resolve(studioRoot, path) === resolve(studioRoot, solutionPath)))
      fail("solution", `${project} is not referenced by Elsa.Studio.slnx`);

    const source = readFileSync(join(repoRoot, project), "utf8");
    for (const [, include] of source.matchAll(/<ProjectReference\s+Include="([^"]+)"/g)) {
      if (include.includes("$")) continue;
      const target = resolve(join(repoRoot, dirname(project)), include.split("\\").join("/"));
      if (!existsSync(target)) fail("project-reference", `${project} references missing ${rel(target)}`);
    }
  }

  for (const path of listed) {
    const target = resolve(studioRoot, path);
    if (!existsSync(target)) fail("solution", `Elsa.Studio.slnx references missing ${rel(target)}`);
    else if (!projectPaths.has(rel(target))) fail("solution", `Elsa.Studio.slnx includes non-discovered project ${rel(target)}`);
  }
}

// ── 3. generated wwwroot is ignored for every Studio module ─────────────────────────────────
{
  const isIgnored = path => {
    try {
      execFileSync("git", ["check-ignore", "-q", path], { cwd: repoRoot, stdio: "ignore" });
      return true;
    } catch {
      return false;
    }
  };

  for (const moduleRoot of moduleRoots) {
    const probe = `${rel(moduleRoot)}/wwwroot/studio/probe.js`;
    if (!isIgnored(probe)) fail("gitignore", `${rel(moduleRoot)}/wwwroot/ is not gitignored`);
  }
}

// ── 4. every module stylesheet is covered by the actual lint globs ─────────────────────────
{
  const script = JSON.parse(readStudio("package.json")).scripts["lint:css"];
  const globs = [...script.matchAll(/"([^"]+)"/g)].map(match => resolve(studioRoot, match[1]));
  const result = await stylelint.lint({ files: globs, configFile: join(studioRoot, ".stylelintrc.json") });
  const linted = new Set(result.results.map(result => rel(result.source)));
  const onDisk = [...walk(moduleSourceRoots, name => name.endsWith(".css"))]
    .map(rel)
    .filter(path => /\/Client\/src\//.test(path) && !path.startsWith("Studio/src/apps/Elsa.Studio.Web/"));

  for (const css of onDisk)
    if (!linted.has(css)) fail("lint:css", `${css} is module CSS but is not covered by lint:css`);
}

// ── 5. enforce the consolidated Studio source boundary ─────────────────────────────────────
{
  for (const manifest of packages.filter(path => path.startsWith("Studio/src/"))) {
    if (!/^Studio\/src\/(essentials|apps)\//.test(manifest))
      fail("layout", `${manifest} is not under Studio/src/essentials or Studio/src/apps`);
  }
  for (const project of projects.filter(path => path.startsWith("Studio/src/"))) {
    if (!/^Studio\/src\/(essentials|apps)\//.test(project))
      fail("layout", `${project} is not under Studio/src/essentials or Studio/src/apps`);
  }
  for (const project of projects.filter(path => path.startsWith("Extensions/"))) {
    if (!/^Extensions\/[^/]+\/studio\/[^/]+\//.test(project))
      fail("layout", `${project} is not under Extensions/<capability>/studio/<module>`);
  }
  for (const project of projects.filter(path => path.startsWith("Studio/src/essentials/"))) {
    const includes = [...readFileSync(join(repoRoot, project), "utf8").matchAll(/<ProjectReference\s+Include="([^"]+)"/g)];
    for (const [, include] of includes) {
      if (include.includes("$")) continue;
      const target = rel(resolve(join(repoRoot, dirname(project)), include.split("\\").join("/")));
      if (target.startsWith("Extensions/"))
        fail("boundary", `${project} is a Studio essential but references optional module ${target}`);
    }
  }

  for (const manifestPath of packages.filter(path => path.startsWith("Studio/src/essentials/"))) {
    const manifest = JSON.parse(readFileSync(join(repoRoot, manifestPath), "utf8"));
    for (const group of ["dependencies", "devDependencies", "peerDependencies"]) {
      for (const [name, range] of Object.entries(manifest[group] ?? {})) {
        const dependencyPath = packageManifests.get(name);
        if (typeof range === "string" && range.startsWith("workspace:") && dependencyPath?.startsWith("Extensions/"))
          fail("boundary", `${manifestPath} is an essential but depends on extension package ${name}`);
      }
    }
  }
}

if (failures.length > 0) {
  console.error(`check-module-layout: ${failures.length} problem(s)\n`);
  for (const { assertion, detail } of failures) console.error(`  [${assertion}] ${detail}`);
  process.exit(1);
}

console.log(`check-module-layout: ok — ${projects.length} projects, ${packages.length} packages, ${moduleRoots.length} module roots`);
