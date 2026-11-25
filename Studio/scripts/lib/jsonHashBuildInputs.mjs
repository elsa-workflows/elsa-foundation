import ts from "typescript";
import { existsSync, readFileSync, readdirSync, statSync } from "node:fs";
import { dirname, extname, isAbsolute, join, relative, resolve, sep } from "node:path";

const code = /\.[cm]?[jt]sx?$/;
function moduleFiles(file, name) {
  const selected = resolve(dirname(file), name.split(/[?#]/)[0]);
  const candidates = [selected, ...[".ts", ".tsx", ".mts", ".cts", ".js", ".mjs", ".cjs", ".json"].flatMap(extension =>
    [selected + extension, join(selected, "index" + extension)])];
  if ([".js", ".mjs", ".cjs"].includes(extname(selected))) candidates.push(selected.replace(/\.[cm]?js$/, ".ts"), selected.replace(/\.[cm]?js$/, ".mts"), selected.replace(/\.[cm]?js$/, ".cts"));
  const found = candidates.filter(candidate => existsSync(candidate) && statSync(candidate).isFile());
  if (!found.length) throw new Error("Unresolved local Studio build import: " + file + " -> " + name);
  return found;
}

function assertStaticBuildInputs(file) {
  const source = ts.createSourceFile(file, readFileSync(file, "utf8"), ts.ScriptTarget.Latest, true);
  const factories = new Set(["createRequire"]), readers = new Set(["readFile", "readFileSync"]);
  const filesystem = /^(?:node:)?fs(?:\/promises)?$/;
  const moduleLoader = /^(?:node:)?module$/;
  const unsupported = () => { throw new Error("Unsupported Studio build loader/read input; bind it through a static import: " + file); };
  for (const statement of source.statements) if (ts.isImportDeclaration(statement) && ts.isStringLiteral(statement.moduleSpecifier)) {
    const module = statement.moduleSpecifier.text;
    const names = statement.importClause?.namedBindings;
    // Taking an authority as a value is enough to lose its input closure through aliases,
    // computed access or a returned loader. Refuse that capability at its import boundary.
    if (filesystem.test(module) || moduleLoader.test(module)) unsupported();
    if (names && ts.isNamedImports(names)) for (const imported of names.elements) {
      const original = imported.propertyName?.text ?? imported.name.text;
      if (moduleLoader.test(module) && original === "createRequire" || filesystem.test(module) && ["readFile", "readFileSync"].includes(original)) unsupported();
      if (["node:module", "module"].includes(module) && original === "createRequire") factories.add(imported.name.text);
      if (/^(?:node:)?fs(?:\/promises)?$/.test(module) && ["readFile", "readFileSync"].includes(original)) readers.add(imported.name.text);
    }
  }
  function visit(node) {
    if (ts.isCallExpression(node)) {
      const callee = node.expression, argument = node.arguments[0];
      const name = ts.isIdentifier(callee) ? callee.text : ts.isPropertyAccessExpression(callee) ? callee.name.text : "";
      if (callee.kind === ts.SyntaxKind.ImportKeyword || name === "require") {
        if (!argument || !ts.isStringLiteralLike(argument)) throw new Error("Unresolved dynamic Studio build module input: " + file);
        if (filesystem.test(argument.text) || moduleLoader.test(argument.text)) unsupported();
      }
      if (factories.has(name) || readers.has(name)) throw new Error("Unsupported Studio build loader/read input; bind it through a static import: " + file);
    }
    if (ts.isImportEqualsDeclaration(node) && ts.isExternalModuleReference(node.moduleReference)) {
      const argument = node.moduleReference.expression;
      if (!argument || !ts.isStringLiteralLike(argument) || filesystem.test(argument.text) || moduleLoader.test(argument.text)) unsupported();
    }
    if (ts.isIdentifier(node) && node.text === "require" && !(ts.isCallExpression(node.parent) && node.parent.expression === node)
        && !(node.parent.name === node)) throw new Error("Unresolved Studio build require alias: " + file);
    ts.forEachChild(node, visit);
  }
  visit(source);
}

/** Raw/URL module data is an exact byte input even when its suffix also denotes source code. */
export function physicalModuleInputs(repoRoot, sources) {
  const result = new Set();
  for (const file of sources) {
    const path = relative(repoRoot, file);
    if (isAbsolute(path) || path === ".." || path.startsWith(".." + sep) || path.split(sep).includes("node_modules") || !code.test(file)) continue;
    for (const imported of ts.preProcessFile(readFileSync(file, "utf8"), true, true).importedFiles) {
      if (/[?#]/.test(imported.fileName) && !imported.fileName.startsWith("."))
        throw new Error("Unsupported non-relative Studio raw/URL input: " + file + " -> " + imported.fileName);
      if (!imported.fileName.startsWith(".")) continue;
      const found = moduleFiles(file, imported.fileName);
      for (const input of found) if (/[?#]/.test(imported.fileName) || !code.test(input)) result.add(input);
    }
  }
  return [...result].sort();
}

/** Conservative build/delivery closure, separate from the shipping hash-call census. */
export function studioHashBuildInputs(repoRoot, clients) {
  const files = new Set(), queue = [], active = new Set(), ownedPackages = new Set();
  const baseline = resolve(repoRoot, "Studio/scripts/baselines/json-hash-review.json");
  const excluded = new Set(["node_modules", "bin", "obj", "dist", ".git", ".agent-prefs", ".github", ".specify", ".claude", ".agents",
    "docs", "specs", "examples", "test-results", "tests", "__tests__", "probes"]);
  const text = /\.(?:[cm]?[jt]sx?|html?|json|ya?ml|css|scss|svg)$/;
  function add(file, build = false) {
    file = resolve(file);
    if (file === baseline) return;
    if (!files.has(file) || build && !active.has(file)) queue.push(file);
    files.add(file);
    if (file.endsWith("package.json")) {
      const name = JSON.parse(readFileSync(file, "utf8")).name;
      if (typeof name === "string" && name.length) ownedPackages.add(name);
    }
    if (build) active.add(file);
    if (build && code.test(file)) {
      for (let scope = dirname(file); ; scope = dirname(scope)) {
        const owned = relative(repoRoot, scope);
        if (isAbsolute(owned) || owned === ".." || owned.startsWith(".." + sep))
          throw new Error("Studio build helper escaped the repository: " + file);
        const manifest = join(scope, "package.json");
        if (existsSync(manifest)) { add(manifest); break; }
        if (scope === resolve(repoRoot)) break;
      }
    }
  }
  function walk(folder, sourceRoot) {
    for (const entry of readdirSync(folder, { withFileTypes: true })) {
      const file = join(folder, entry.name);
      if (entry.isDirectory()) {
        if (!excluded.has(entry.name) && file !== sourceRoot) walk(file, sourceRoot);
      } else if (text.test(entry.name) || entry.name === ".npmrc") add(file, /^vite.*config\.[cm]?[jt]s$/.test(entry.name));
    }
  }
  walk(join(repoRoot, "Studio"), join(repoRoot, "Studio/src"));
  for (const client of clients) walk(client, join(client, "src"));
  for (let index = 0; index < queue.length; index++) {
    const file = queue[index];
    if (file.endsWith("package.json")) {
      const manifest = JSON.parse(readFileSync(file, "utf8"));
      for (const [name, command] of Object.entries(manifest.scripts ?? {})) if (/^(?:build(?::|$)|prebuild$|prepack$|prepare$)/.test(name))
        for (const match of command.matchAll(/(?:^|\s)(?:node|tsx|ts-node)\s+([^\s]+\.[cm]?[jt]s)(?=\s|$)/g)) {
          const entry = resolve(dirname(file), match[1]);
          if (!existsSync(entry)) throw new Error("Missing Studio build script entry: " + entry);
          add(entry, true);
        }
    }
    if (/tsconfig[^/\\]*\.json$/.test(file)) {
      // Reading a base config for provenance need not select compilation inputs.
      // JSON/extends-resolution errors still refuse the receipt.
      const host = { ...ts.sys, readFile(path) { const value = ts.sys.readFile(path); if (value !== undefined) add(path); return value; },
        onUnRecoverableConfigFileDiagnostic(error) { throw new Error(ts.flattenDiagnosticMessageText(error.messageText, " ")); } };
      const parsed = ts.getParsedCommandLineOfConfigFile(file, { noEmit: true }, host);
      const errors = parsed?.errors.filter(error => ![18002, 18003].includes(error.code)) ?? [];
      if (errors.length) throw new Error("Unresolved Studio build configuration: " + JSON.stringify(errors.map(error => ts.flattenDiagnosticMessageText(error.messageText, " "))));
    }
    if (!code.test(file)) continue;
    if (active.has(file)) assertStaticBuildInputs(file);
    for (const imported of ts.preProcessFile(readFileSync(file, "utf8"), true, true).importedFiles) {
      if (active.has(file) && [...ownedPackages].some(name => imported.fileName === name || imported.fileName.startsWith(name + "/")))
        throw new Error("Unsupported Studio build owned-package import: " + file + " -> " + imported.fileName);
      if (active.has(file) && imported.fileName.startsWith("#"))
        throw new Error("Unsupported Studio build package-import alias: " + file + " -> " + imported.fileName);
      if (active.has(file) && !imported.fileName.startsWith("node:") && /^(?:[a-z][a-z0-9+.-]*:|[/\\])/i.test(imported.fileName))
        throw new Error("Unsupported Studio build absolute/URL input: " + file + " -> " + imported.fileName);
      if (!imported.fileName.startsWith(".")) continue;
      moduleFiles(file, imported.fileName).forEach(input => add(input, active.has(file)));
    }
  }
  return [...files].sort();
}
