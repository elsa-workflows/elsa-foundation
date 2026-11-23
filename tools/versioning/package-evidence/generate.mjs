// Normalize the original NuGet restore and package contents with the CycloneDX SDK.
import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';
import { readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { Models, Serialize, Spec, Validation } from '@cyclonedx/cyclonedx-library';
import { PackageURL } from 'packageurl-js';

const serializer = new Serialize.JsonSerializer(new Serialize.JSON.Normalize.Factory(Spec.Spec1dot6));
const validator = new Validation.JsonStrictValidator(Spec.Version.v1dot6);

function nugetComponent(type, name, version, context) {
  const purl = new PackageURL('nuget', undefined, name.toLowerCase(), version, undefined, undefined).toString();
  return new Models.Component(type, name, { version, purl, bomRef: context + purl });
}

function property(component, name, value) {
  component.properties.add(new Models.Property('elsa:' + name, String(value)));
}

function graph(bom, root, name, libraries, direct, role, packageIds, requested = {}) {
  const context = role + ':' + name + ':';
  const group = new Models.Component('data', name, { bomRef: context });
  property(group, 'inventory-scope', role);
  bom.components.add(group);
  root.dependencies.add(group.bomRef);
  const byId = new Map();
  for (const [key, definition] of Object.entries(libraries)) {
    const [id, version] = key.split('/');
    assert.ok(version && !byId.has(id.toLowerCase()), 'Unexpected library key: ' + key);
    // A local project in a shipped .deps.json need not be an independently published NuGet package.
    const component = definition.type === 'package' || packageIds.has(id.toLowerCase()) || direct.has(id)
      ? nugetComponent('library', id, version, context)
      : new Models.Component('library', id, { version, bomRef: context + key });
    property(component, 'inventory-scope', role);
    property(component, 'target', name);
    if (definition.type) property(component, 'restore-type', definition.type);
    bom.components.add(component);
    byId.set(id.toLowerCase(), component);
  }
  function find(id) {
    const component = byId.get(id.toLowerCase());
    assert.ok(component, 'Missing ' + id + ' from ' + name);
    return component;
  }
  for (const [id, requirement] of Object.entries(requested)) {
    // This is a producer-declared input, which may be private or bundled and absent from the nuspec.
    const resolved = byId.get(id.toLowerCase());
    if (resolved) {
      property(resolved, 'producer-declared', requirement.target);
      if (requirement.version) property(resolved, 'restore-requested-version', requirement.version);
    }
    property(group, 'restore-request', JSON.stringify({ id, ...requirement }));
  }
  for (const [key, definition] of Object.entries(libraries)) {
    const component = find(key.split('/')[0]);
    for (const child of Object.keys(definition.dependencies ?? {})) component.dependencies.add(find(child).bomRef);
  }
  for (const id of direct.keys()) {
    const component = find(id);
    group.dependencies.add(component.bomRef);
    const declaration = direct.get(id);
    for (const [field, value] of Object.entries(declaration)) {
      if (field !== 'id') property(component, 'nuspec-' + field, value);
    }
  }
  if (role === 'shipped-tool-runtime') {
    for (const component of byId.values()) group.dependencies.add(component.bomRef);
    return;
  }
  const components = new Map([...byId.values()].map(component => [component.bomRef, component]));
  const closure = new Set(group.dependencies);
  for (const ref of closure) {
    for (const child of components.get(ref).dependencies) closure.add(child);
  }
  for (const component of byId.values()) {
    const scope = group.dependencies.has(component.bomRef) ? 'declared' :
      closure.has(component.bomRef) ? 'producer-resolved transitive' : 'producer-only';
    property(component, 'dependency-scope', scope);
    if (!closure.has(component.bomRef)) component.scope = 'excluded';
  }
}

async function generate(row) {
  const root = nugetComponent(row.kind === 'tool' ? 'application' : row.kind === 'template' ? 'data' : 'library',
    row.id, row.version, '');
  root.hashes.set('SHA-256', row.digest);
  property(root, 'package-kind', row.kind);
  const bom = new Models.Bom({ serialNumber: 'urn:uuid:' + randomUUID(), metadata: new Models.Metadata({ component: root }) });
  for (const key of ['commit', 'producer_run', 'producer_attempt', 'assets_sha256', 'lock_sha256']) {
    property(bom.metadata, key.replaceAll('_', '-'), row[key]);
  }
  const handled = new Set();
  const packageIds = new Set(row.package_ids);
  for (const [target, libraries] of Object.entries(row.targets)) {
    const framework = target.split('/')[0].toLowerCase();
    assert.ok(row.frameworks.some(f => f.toLowerCase() === framework), 'Unknown restore framework: ' + target);
    handled.add(framework);
    const declared = [...row.ungrouped, ...(row.groups[framework] ?? row.groups[''] ?? [])];
    graph(bom, root, target, libraries, new Map(declared.map(d => [d.id, d])), 'producer-restore', packageIds, row.requested[framework]);
    for (const shared of row.shared[framework] ?? []) property(root, 'shared-framework:' + framework, shared);
  }
  assert.ok(handled.size && row.frameworks.every(f => handled.has(f.toLowerCase())), 'Missing restore framework');
  for (const framework of [...Object.keys(row.groups), ...Object.keys(row.shared)]) {
    assert.ok(!framework || handled.has(framework), 'Unmatched nuspec framework: ' + framework);
  }
  for (const [manifest, libraries] of Object.entries(row.runtimes)) {
    graph(bom, root, manifest, libraries, new Map(), 'shipped-tool-runtime', packageIds);
  }
  for (const item of row.payload) {
    const file = new Models.Component('file', item.path, { bomRef: 'file:' + item.path });
    file.hashes.set('SHA-256', item.sha256);
    for (const framework of item.runtime_frameworks ?? []) {
      property(file, 'required-shared-framework', JSON.stringify(framework));
    }
    if (item.roll_forward) property(file, 'roll-forward', item.roll_forward);
    for (const declaration of item.template_references ?? []) {
      property(file, 'template-project-declaration', JSON.stringify(declaration));
    }
    root.components.add(file);
  }
  const json = serializer.serialize(bom, { sortLists: true }) + '\n';
  const errors = await validator.validate(json);
  assert.equal(errors, null, JSON.stringify(errors));
  return json;
}

const [inputs, output] = process.argv.slice(2);
for (const path of readdirSync(inputs)) {
  const row = JSON.parse(readFileSync(join(inputs, path)));
  writeFileSync(join(output, row.name + '.cdx.json'), await generate(row), { flag: 'wx' });
}
