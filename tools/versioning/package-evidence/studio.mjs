// Extend pnpm's standard CycloneDX output with packed workspace links and Vite's bundle facts.
import assert from 'node:assert/strict';
import { createHash, randomUUID } from 'node:crypto';
import { existsSync, mkdirSync, readFileSync, readdirSync, realpathSync, statSync, writeFileSync } from 'node:fs';
import { basename, dirname, join, relative, resolve } from 'node:path';
import { createRequire } from 'node:module';
import { Spec, Validation } from '@cyclonedx/cyclonedx-library';
import { PackageURL } from 'packageurl-js';

const [mode, recordsDir, dataPath, outputDir] = process.argv.slice(2);
assert.ok(['npm', 'nuget'].includes(mode));
const json = path => JSON.parse(readFileSync(path));
const hash = path => createHash('sha256').update(readFileSync(path)).digest('hex');
const env = name => { assert.ok(process.env[name], name); return process.env[name]; };
const version = env('VERSION');
const typescript = mode === 'npm' ? createRequire(resolve('package.json'))('typescript') : null;
const validator = new Validation.JsonStrictValidator(Spec.Version.v1dot6);
const records = readdirSync(recordsDir).map(file => json(join(recordsDir, file)));
const names = ['studio-sdk', 'studio-ui', 'studio-code-editor', 'studio-workflows'].map(name => '@elsa-workflows/' + name);
const fullName = c => c.group ? c.group + '/' + c.name : c.name;
const npmPurl = (name, v) => {
  const parts = name.split('/');
  return new PackageURL('npm', parts.length === 2 ? parts[0] : undefined, parts.at(-1), v).toString();
};
function resolvedManifest(resolver, dep) {
  const path = resolver.resolve.paths(dep)?.map(dir => join(dir, dep, 'package.json')).find(existsSync);
  return path ? realpathSync(path) : undefined;
}
function property(item, key, value) {
  (item.properties ??= []).push({ name: 'elsa:' + key, value });
}

const libraries = new Map();
if (mode === 'npm') {
  for (const r of records.filter(r => r.kind === 'npm')) {
    const manifest = json(join(dirname(r.outDir), 'package.json'));
    assert.equal(manifest.version, version);
    const slug = manifest.name.replace('@', '').replace('/', '-');
    const locked = json(join(dataPath, slug + '.cdx.json'));
    assert.equal(fullName(locked.metadata.component), manifest.name);
    libraries.set(manifest.name, { manifest, locked, build: r });
  }
  assert.deepEqual([...libraries.keys()].sort(), [...names].sort());
}

async function inventory(name, directory, builds, output) {
  const purl = mode === 'npm' ? npmPurl(name, version) : new PackageURL('nuget', undefined, name.toLowerCase(), version).toString();
  const root = { type: 'library', name, version, purl, 'bom-ref': purl, components: [] };
  const metadata = { component: root };
  for (const [key, value] of Object.entries({
    scope: mode === 'npm' ? 'npm library' : 'NuGet static web assets',
    repository: env('GITHUB_REPOSITORY'), commit: env('GITHUB_SHA'), 'producer-run': env('GITHUB_RUN_ID'),
    'producer-attempt': env('GITHUB_RUN_ATTEMPT'), 'pnpm-lock-sha256': hash('pnpm-lock.yaml')
  })) property(metadata, key, value);
  const components = new Map();
  const edges = new Map();
  function graphRow(ref) {
    if (!edges.has(ref)) edges.set(ref, new Set());
  }
  function edge(from, to) {
    assert.ok(from && to, 'Unidentified component in dependency graph');
    if (from === to) return;
    graphRow(from);
    edges.get(from).add(to);
  }
  graphRow(root['bom-ref']);
  const requiredInstall = new Map();
  const runtime = new Set([root['bom-ref']]);
  const visited = new Set();
  function addLocked(id) {
    if (visited.has(id)) return;
    visited.add(id);
    const { manifest, locked } = libraries.get(id);
    const parent = npmPurl(id, version);
    const oldRoot = locked.metadata.component['bom-ref'];
    if (id !== name) components.set(parent, { ...locked.metadata.component, version, purl: parent, 'bom-ref': parent });
    for (const c of locked.components ?? []) components.set(c['bom-ref'], structuredClone(c));
    for (const row of locked.dependencies ?? []) {
      const ref = row.ref === oldRoot ? parent : row.ref;
      graphRow(ref);
      for (const child of row.dependsOn ?? []) edge(ref, child);
    }
    const direct = new Set([...(edges.get(parent) ?? [])].map(ref => fullName(components.get(ref))));
    for (const kind of ['dependencies', 'optionalDependencies', 'peerDependencies']) {
      for (const [dep, range] of Object.entries(manifest[kind] ?? {})) {
        if (id === name) property(root, kind + ':' + dep, range);
        if (direct.has(dep)) continue;
        // pnpm writes the external graph for each importer. Workspace siblings have their
        // own graph and acquire the release version when the publish manifests are prepared.
        if (libraries.has(dep)) {
          edge(parent, npmPurl(dep, version));
          addLocked(dep);
        } else {
          assert.ok(kind === 'optionalDependencies' || manifest.peerDependenciesMeta?.[dep]?.optional,
            'Missing locked resolution for ' + id + ' -> ' + dep);
        }
      }
    }
  }
  if (mode === 'npm') {
    metadata.tools = libraries.get(name).locked.metadata.tools;
    addLocked(name);
    // pnpm's SBOM emits each package identity once and can omit a dependency edge
    // when the same target was reached elsewhere. Read the frozen install to retain
    // those edges, while requiring every resolved component to occur in the lock SBOM.
    const checked = new Set();
    function installedEdges(dir, parent) {
      if (checked.has(dir)) return;
      checked.add(dir);
      const pkgPath = join(dir, 'package.json');
      const pkg = json(pkgPath);
      const resolver = createRequire(pkgPath);
      for (const dep of Object.keys({ ...pkg.dependencies, ...pkg.peerDependencies, ...pkg.optionalDependencies })) {
        const file = resolvedManifest(resolver, dep);
        const optional = Object.hasOwn(pkg.optionalDependencies ?? {}, dep) || pkg.peerDependenciesMeta?.[dep]?.optional;
        if (!file && optional) continue;
        assert.ok(file, 'Missing installed production dependency: ' + dep);
        const resolved = json(file);
        const ref = npmPurl(resolved.name, resolved.version);
        if (optional && !components.has(ref)) continue;
        assert.ok(components.has(ref) || ref === root['bom-ref'], 'Lock SBOM lacks installed ' + ref);
        edge(parent, ref);
        // Distinct installations of the same PURL can share an edge. Any required
        // declaration makes that edge required for the aggregate component.
        const kinds = requiredInstall.get(parent) ?? new Map();
        requiredInstall.set(parent, kinds);
        kinds.set(ref, kinds.get(ref) || !optional);
        installedEdges(dirname(file), ref);
      }
    }
    installedEdges(dirname(directory), root['bom-ref']);
  }
  const externals = new Set(builds.flatMap(r => r.externals));
  for (const imported of [...externals].sort()) property(root, 'external-import', imported);
  const bundled = new Map(builds.flatMap(r => r.packages).map(p => [p.name + '@' + p.version, p]));
  const bundledRefs = new Map();
  for (const [key, p] of bundled) {
    if (p.name === name) {
      bundledRefs.set(key, root['bom-ref']);
      continue;
    }
    const shipped = p.workspace && names.includes(p.name) ? version : p.version;
    const purl = p.workspace && !names.includes(p.name) ? undefined : npmPurl(p.name, shipped);
    const ref = purl ?? 'workspace:' + p.name;
    const c = components.get(ref) ?? { type: 'library', name: p.name, version: shipped, purl, 'bom-ref': ref };
    if (p.workspace) property(c, 'source', 'producer workspace');
    property(c, 'bundled', 'true');
    components.set(ref, c);
    bundledRefs.set(key, ref);
    runtime.add(ref);
  }
  const dependencyNames = pkg => Object.keys({ ...pkg.dependencies, ...pkg.peerDependencies, ...pkg.optionalDependencies });
  const packageName = specifier => specifier.split('/').slice(0, specifier.startsWith('@') ? 2 : 1).join('/');
  function externalRef(file) {
    const actual = json(file);
    const tested = !file.includes('/node_modules/') && names.includes(actual.name) ? version : actual.version;
    const ref = npmPurl(actual.name, tested);
    if (!components.has(ref) && ref !== root['bom-ref']) {
      const c = { type: 'library', name: actual.name, version: tested, purl: ref, 'bom-ref': ref };
      property(c, 'version-context', 'producer-resolved external');
      components.set(ref, c);
    }
    runtime.add(ref);
    if (!dependencyNames(actual).length) graphRow(ref);
    return ref;
  }
  for (const r of builds) {
    const directories = new Map(r.packages.map(p => [p.directory, bundledRefs.get(p.name + '@' + p.version)]));
    for (const dir of r.entries) edge(root['bom-ref'], directories.get(dir));
    for (const [dir, targets] of r.imports) {
      const resolver = createRequire(join(dir, 'package.json'));
      for (const target of targets) {
        const file = directories.has(target) ? null : resolvedManifest(resolver, packageName(target));
        const child = directories.get(target) ?? (file && externalRef(file));
        if (child) edge(directories.get(dir), child);
      }
    }
    const externalNames = new Set(r.externals.map(packageName));
    for (const p of r.packages) {
      const from = directories.get(p.directory);
      const path = join(p.directory, 'package.json');
      const dependencies = dependencyNames(json(path));
      if (!dependencies.length) graphRow(from);
      const resolver = createRequire(path);
      for (const dep of dependencies) {
        const file = resolvedManifest(resolver, dep);
        if (!file) continue;
        const child = directories.get(dirname(file)) ?? (externalNames.has(dep) && externalRef(file));
        if (child) edge(from, child);
      }
    }
    for (const [file, expected] of Object.entries(r.files)) {
      assert.equal(hash(join(r.outDir, file)), expected, 'Stale bundle output: ' + file);
    }
  }
  if (mode === 'npm') {
    // Optional peers and their transitive closure stay optional unless another
    // required path or a retained runtime import also reaches them.
    for (const from of runtime) {
      for (const to of edges.get(from) ?? []) {
        if (requiredInstall.get(from)?.get(to) !== false) runtime.add(to);
      }
    }
    for (const [ref, component] of components) {
      if (!runtime.has(ref)) component.scope = 'optional';
    }
  }
  const declarationImports = new Set();
  const prefix = mode === 'npm' ? 'dist/' : 'staticwebassets/';
  for (const file of readdirSync(directory, { recursive: true }).sort()) {
    const path = join(directory, file);
    if (path === output || file.split('/').includes('.vite') || !statSync(path).isFile()) continue;
    if (typescript && /\.d\.(?:c|m)?ts$/.test(file)) {
      const info = typescript.preProcessFile(readFileSync(path, 'utf8'), true, true);
      for (const { fileName } of [...info.importedFiles, ...info.typeReferenceDirectives]) {
        if (!fileName.startsWith('.') && !fileName.startsWith('/')) declarationImports.add(fileName);
      }
    }
    root.components.push({ type: 'file', name: prefix + file, 'bom-ref': 'file:' + prefix + file,
      hashes: [{ alg: 'SHA-256', content: hash(path) }] });
  }
  for (const name of [...declarationImports].sort()) property(root, 'declaration-import', name);
  const bom = { bomFormat: 'CycloneDX', specVersion: '1.6', serialNumber: 'urn:uuid:' + randomUUID(), version: 1,
    metadata, components: [...components.values()], dependencies: [...edges].map(([ref, deps]) => ({ ref, dependsOn: [...deps].sort() })) };
  const result = JSON.stringify(bom) + '\n';
  const errors = await validator.validate(result);
  assert.equal(errors, null, JSON.stringify(errors));
  mkdirSync(dirname(output), { recursive: true });
  writeFileSync(output, result);
}

if (mode === 'npm') {
  for (const [name, { build }] of libraries) await inventory(name, build.outDir, [build], join(build.outDir, 'sbom.cdx.json'));
} else {
  for (const p of json(dataPath).packable_projects) {
    const directory = resolve('..', dirname(p.project), 'wwwroot');
    if (!existsSync(directory) || !readdirSync(directory).length) continue;
    const assets = realpathSync(directory);
    const builds = records.filter(r => r.kind !== 'npm' && !relative(assets, r.outDir).startsWith('..'));
    assert.deepEqual(builds.map(r => r.kind).sort(), p.id === 'Elsa.Studio.Hosting' ? ['shell', 'vendor'] : ['module'], p.id);
    await inventory(p.id, assets, builds, join(outputDir, basename(p.project, '.csproj') + '.cdx.json'));
  }
}
