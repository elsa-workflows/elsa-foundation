// Generate per-artifact frontend inventories after bundling and before packing.
import assert from 'node:assert/strict';
import { createHash, randomUUID } from 'node:crypto';
import { existsSync, mkdirSync, readFileSync, readdirSync, realpathSync, statSync, writeFileSync } from 'node:fs';
import { basename, dirname, join, relative, resolve } from 'node:path';
import { Models, Serialize, Spec, Validation } from '@cyclonedx/cyclonedx-library';
import { PackageURL } from 'packageurl-js';

const [mode, recordsDir, dataPath, outputDir] = process.argv.slice(2);
assert.ok(['npm', 'nuget'].includes(mode));
const json = path => JSON.parse(readFileSync(path));
const hash = path => createHash('sha256').update(readFileSync(path)).digest('hex');
const version = process.env.VERSION;
const env = name => { assert.ok(process.env[name], name); return process.env[name]; };
const property = (item, key, value) => item.properties.add(new Models.Property('elsa:' + key, value));
const serializer = new Serialize.JsonSerializer(new Serialize.JSON.Normalize.Factory(Spec.Spec1dot6));
const validator = new Validation.JsonStrictValidator(Spec.Version.v1dot6);
const records = readdirSync(recordsDir).map(file => json(join(recordsDir, file)));
const names = ['studio-sdk', 'studio-ui', 'studio-code-editor', 'studio-workflows'].map(name => '@elsa-workflows/' + name);
const lockHash = hash('pnpm-lock.yaml');

function npmPurl(name, version) {
  const parts = name.split('/');
  return new PackageURL('npm', parts.length === 2 ? parts[0] : undefined, parts.at(-1), version, undefined, undefined).toString();
}

async function inventory(name, directory, records, manifest, locked, output) {
  assert.ok(version && records.length, 'Missing bundle records for ' + name);
  const root = new Models.Component('library', name, { version, bomRef: 'root', purl: mode === 'npm' ?
    npmPurl(name, version) : new PackageURL('nuget', undefined, name.toLowerCase(), version).toString() });
  const bom = new Models.Bom({ serialNumber: 'urn:uuid:' + randomUUID(), metadata: new Models.Metadata({ component: root }) });
  for (const [key, value] of Object.entries({
    scope: mode === 'npm' ? 'npm library' : 'NuGet static web assets',
    repository: env('GITHUB_REPOSITORY'), commit: env('GITHUB_SHA'), 'producer-run': env('GITHUB_RUN_ID'),
    'producer-attempt': env('GITHUB_RUN_ATTEMPT'), 'pnpm-lock-sha256': lockHash
  })) property(bom.metadata, key, value);

  const components = new Map();
  function component(id, installed, workspace) {
    const published = workspace && names.includes(id) ? version : installed;
    const key = id + '@' + published;
    if (!components.has(key)) {
      const c = new Models.Component('library', id, { version: published, bomRef: key });
      if (!workspace || names.includes(id)) c.purl = npmPurl(id, published);
      if (workspace) property(c, 'source', 'producer workspace');
      components.set(key, c);
      bom.components.add(c);
    }
    return components.get(key);
  }
  const addDependency = (parent, child) => parent.dependencies.add(child.bomRef);
  const externals = new Set(records.flatMap(r => r.externals));
  for (const imported of externals) property(root, 'external-import', imported);
  const bundled = new Map(records.flatMap(r => r.packages).map(p => [p.name + '@' + p.version, p]));
  for (const p of bundled.values()) {
    if (p.name === name) continue;
    const c = component(p.name, p.version, p.workspace);
    property(c, 'bundled', 'true');
    addDependency(root, c);
  }
  if (manifest) {
    assert.equal(manifest.name, name);
    assert.equal(manifest.version, version);
    // pnpm reports its deduplicated graph across paths; combine edges at each occurrence.
    function descend(parent, children) {
      for (const [id, d] of Object.entries(children)) {
        const c = component(id, d.version, d.version.startsWith('link:'));
        addDependency(parent, c);
        descend(c, { ...d.dependencies, ...d.optionalDependencies });
      }
    }
    const direct = { ...locked.dependencies, ...locked.optionalDependencies };
    descend(root, direct);
    for (const field of ['dependencies', 'peerDependencies', 'optionalDependencies']) {
      for (const [id, range] of Object.entries(manifest[field] ?? {})) {
        // An optional dependency can legitimately be absent on the build platform.
        property(root, field + ':' + id, range);
        assert.ok(direct[id] || field === 'optionalDependencies' || manifest.peerDependenciesMeta?.[id]?.optional,
          'Missing locked resolution for ' + id);
      }
    }
  }

  // Hash every distributable frontend file, including copied assets and declarations
  // produced outside Vite. Fail if bytes captured by Rollup changed before packing.
  for (const r of records) {
    for (const [file, expected] of Object.entries(r.files)) {
      assert.equal(hash(join(r.outDir, file)), expected, 'Stale bundle output: ' + file);
    }
  }
  const prefix = mode === 'npm' ? 'dist/' : 'staticwebassets/';
  for (const file of readdirSync(directory, { recursive: true }).sort()) {
    if (file.split('/').includes('.vite') || !statSync(join(directory, file)).isFile()) continue;
    if (join(directory, file) === output) continue;
    const c = new Models.Component('file', prefix + file, { bomRef: 'file:' + prefix + file });
    c.hashes.set('SHA-256', hash(join(directory, file)));
    root.components.add(c);
  }
  const result = serializer.serialize(bom, { sortLists: true }) + '\n';
  assert.equal(await validator.validate(result), null, 'Invalid CycloneDX 1.6 document for ' + name);
  mkdirSync(dirname(output), { recursive: true });
  writeFileSync(output, result);
}

if (mode === 'npm') {
  const projects = json(dataPath);
  assert.deepEqual(projects.map(p => p.name).sort(), [...names].sort());
  for (const p of projects) {
    const dist = realpathSync(join(p.path, 'dist'));
    const builds = records.filter(r => r.kind === 'npm' && r.outDir === dist);
    assert.equal(builds.length, 1, 'Expected one library build for ' + p.name);
    await inventory(p.name, dist, builds, json(join(p.path, 'package.json')), p, join(dist, 'sbom.cdx.json'));
  }
} else {
  for (const p of json(dataPath).packable_projects) {
    const directory = resolve('..', dirname(p.project), 'wwwroot');
    if (!existsSync(directory) || !readdirSync(directory).length) continue;
    const assets = realpathSync(directory);
    const builds = records.filter(r => r.kind !== 'npm' && !relative(assets, r.outDir).startsWith('..'));
    // Hosting owns two distinct outputs in one directory, the shell and the vendor shims.
    assert.deepEqual(builds.map(r => r.kind).sort(), p.id === 'Elsa.Studio.Hosting' ? ['shell', 'vendor'] : ['module'], p.id);
    await inventory(p.id, assets, builds, null, null, join(outputDir, basename(p.project, '.csproj') + '.cdx.json'));
  }
}
