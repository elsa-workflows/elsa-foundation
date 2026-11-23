// A library can resolve different transitive versions on different frameworks.
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
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
  const sha = bytes => createHash('sha256').update(bytes).digest('hex');
  const packageBytes = Buffer.from('fixture package bytes');
  const hash = sha(packageBytes);
  const row = { id: 'Example', version: '1.2.3', name: 'Example.1.2.3.nupkg', digest: hash, kind: 'library',
    commit: '1'.repeat(40), producer_run: 23, producer_attempt: 1, assets_sha256: hash, lock_sha256: hash,
    frameworks: ['net8.0', 'net10.0'], requested: {}, groups: {}, ungrouped: [], targets: {}, shared: {}, runtimes: {},
    payload: [{ path: 'lib/net8.0/example.dll', sha256: hash }], package_ids: ['example'] };
  for (const [tfm, version] of [['net8.0', '8.0.1'], ['net10.0', '10.0.1']]) {
    row.groups[tfm] = [{ id: 'Direct', version: '[' + version + ',20)' }];
    row.requested[tfm] = { BuildOnly: { target: 'Package', version: '[9, )' } };
    row.targets[tfm] = { ['Direct/' + version]: { type: 'package', dependencies: { Child: version } },
      ['Child/' + version]: { type: 'package' }, 'BuildOnly/9.0': { type: 'package' } };
  }
  writeFileSync(join(inputs, 'row.json'), JSON.stringify(row));
  const run = spawnSync(process.execPath, [fileURLToPath(new URL('./generate.mjs', import.meta.url)), inputs, output], { encoding: 'utf8' });
  assert.equal(run.status, 0, run.stderr);
  const bom = JSON.parse(readFileSync(join(output, row.name + '.cdx.json')));
  const select = id => bom.components.filter(c => c.name === id);
  assert.deepEqual(select('Child').map(c => c.version).sort(), ['10.0.1', '8.0.1']);
  for (const c of select('Direct')) {
    assert.ok(c.properties.some(p => p.name === 'elsa:nuspec-version' && p.value === '[' + c.version + ',20)'));
    const edge = bom.dependencies.find(edge => edge.ref === c['bom-ref']);
    assert.equal(edge.dependsOn.length, 1);
    assert.ok(select('Child').some(child => child['bom-ref'] === edge.dependsOn[0] && child.version === c.version));
  }
  assert.ok(select('BuildOnly').every(c => c.scope === 'excluded'));
  assert.equal(bom.metadata.component.components[0].hashes[0].content, hash);

  const nuget = join(temp, 'nuget');
  mkdirSync(nuget);
  writeFileSync(join(nuget, row.name), packageBytes);
  writeFileSync(join(temp, 'computation.json'), JSON.stringify({ affected: [row.id], commit: row.commit }));
  const path = join(output, row.name + '.cdx.json');
  function seal() {
    writeFileSync(join(output, 'subjects.json'), JSON.stringify([{
      id: row.id, version: row.version, name: row.name, sha256: hash, sbom_sha256: sha(readFileSync(path))
    }]));
  }
  function validate(runId = '23') {
    return spawnSync(process.execPath, [fileURLToPath(new URL('./attest.mjs', import.meta.url)), temp, 'validate'], {
      encoding: 'utf8', env: { ...process.env, GITHUB_SHA: row.commit, GITHUB_RUN_ID: runId, GITHUB_RUN_ATTEMPT: '2' }
    }).status;
  }
  seal();
  assert.equal(validate(), 0, 'A later attempt can reuse the original producer');
  assert.notEqual(validate('999'), 0, 'Wrong workflow run');
  writeFileSync(path, JSON.stringify({ ...bom, specVersion: '9.9' }));
  assert.notEqual(validate(), 0, 'Altered inventory bytes');
  seal();
  assert.notEqual(validate(), 0, 'Unsupported CycloneDX version even with a matching inventory digest');
  writeFileSync(path, JSON.stringify({ ...bom, bomFormat: 'unknown' }));
  seal();
  assert.notEqual(validate(), 0, 'Malformed inventory');
  writeFileSync(path, JSON.stringify(bom));
  seal();
  writeFileSync(join(nuget, row.name), 'swapped package');
  assert.notEqual(validate(), 0, 'Different package bytes');
});
