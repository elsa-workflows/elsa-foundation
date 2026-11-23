// A library can resolve different transitive versions on different frameworks.
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdtempSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import test from 'node:test';

test('target-specific ranges and build-only inputs stay distinct from packed files', t => {
  const temp = mkdtempSync(join(tmpdir(), 'elsa-package-evidence-'));
  t.after(() => rmSync(temp, { recursive: true, force: true }));
  const inputs = join(temp, 'inputs');
  const output = join(temp, 'sboms');
  mkdirSync(inputs);
  mkdirSync(output);
  const hash = 'a'.repeat(64);
  const row = { id: 'Example', version: '1.2.3', name: 'Example.1.2.3.nupkg', digest: hash, kind: 'library',
    commit: '1'.repeat(40), producer_run: 23, producer_attempt: 1, assets_sha256: hash, lock_sha256: hash,
    frameworks: ['net8.0', 'net10.0'], groups: {}, ungrouped: [], targets: {}, shared: {}, runtimes: {},
    payload: [{ path: 'lib/net8.0/example.dll', sha256: hash }], package_ids: ['example'] };
  for (const [tfm, version] of [['net8.0', '8.0.1'], ['net10.0', '10.0.1']]) {
    row.groups[tfm] = [{ id: 'Direct', version: '[1,2)' }];
    row.targets[tfm] = { 'Direct/1.1': { type: 'package', dependencies: { Child: version } },
      ['Child/' + version]: { type: 'package' }, 'BuildOnly/9.0': { type: 'package' } };
  }
  writeFileSync(join(inputs, 'row.json'), JSON.stringify(row));
  const run = spawnSync(process.execPath, [fileURLToPath(new URL('./generate.mjs', import.meta.url)), inputs, output], { encoding: 'utf8' });
  assert.equal(run.status, 0, run.stderr);
  const bom = JSON.parse(readFileSync(join(output, row.name + '.cdx.json')));
  const select = id => bom.components.filter(c => c.name === id);
  assert.deepEqual(select('Child').map(c => c.version).sort(), ['10.0.1', '8.0.1']);
  for (const c of select('Direct')) {
    assert.ok(c.properties.some(p => p.name === 'elsa:nuspec-version' && p.value === '[1,2)'));
    const edge = bom.dependencies.find(edge => edge.ref === c['bom-ref']);
    assert.equal(edge.dependsOn.length, 1);
    assert.ok(select('Child').some(child => child['bom-ref'] === edge.dependsOn[0]));
  }
  assert.ok(select('BuildOnly').every(c => c.scope === 'excluded'));
  assert.equal(bom.metadata.component.components[0].hashes[0].content, hash);
});
