// Use the same official signing and storage client as actions/attest. Each package
// needs its own predicate; a single shared SBOM across all packages would be false.
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { readdirSync, readFileSync } from 'node:fs';
import { basename, join } from 'node:path';
import { attest } from '@actions/attest';
import { Spec, Validation } from '@cyclonedx/cyclonedx-library';

const [plan, mode] = process.argv.slice(2);
assert.ok(['validate', 'sign'].includes(mode));
const json = path => JSON.parse(readFileSync(path));
const digest = path => createHash('sha256').update(readFileSync(path)).digest('hex');
const evidence = join(plan, 'sboms');
const subjects = json(join(evidence, 'subjects.json'));
const computation = json(join(plan, 'computation.json'));
const ids = subjects.map(s => s.id).sort();
assert.deepEqual(ids, [...computation.affected].sort(), 'Subject set differs from affected set');
assert.equal(computation.commit, process.env.GITHUB_SHA);
assert.deepEqual(subjects.map(s => s.name).sort(),
  readdirSync(join(plan, 'nuget')).filter(n => n.endsWith('.nupkg')).sort());
const validator = new Validation.JsonStrictValidator(Spec.Version.v1dot6);
for (const subject of subjects) {
  assert.equal(basename(subject.name), subject.name);
  assert.equal(subject.name.toLowerCase(), (subject.id + '.' + subject.version + '.nupkg').toLowerCase());
  assert.equal(digest(join(plan, 'nuget', subject.name)), subject.sha256);
  const bomBytes = readFileSync(join(evidence, subject.name + '.cdx.json'), 'utf8');
  assert.equal(createHash('sha256').update(bomBytes).digest('hex'), subject.sbom_sha256);
  assert.ok(Buffer.byteLength(bomBytes) <= 16 * 1024 * 1024, 'SBOM exceeds GitHub attestation size limit');
  const errors = await validator.validate(bomBytes);
  assert.equal(errors, null, JSON.stringify(errors));
  const bom = JSON.parse(bomBytes);
  assert.equal(bom.metadata.component.name, subject.id);
  assert.equal(bom.metadata.component.version, subject.version);
  assert.deepEqual(bom.metadata.component.hashes, [{ alg: 'SHA-256', content: subject.sha256 }]);
  for (const [key, expected] of Object.entries({
    commit: process.env.GITHUB_SHA, 'producer-run': process.env.GITHUB_RUN_ID
  })) {
    const values = bom.metadata.properties.filter(p => p.name === 'elsa:' + key).map(p => p.value);
    assert.deepEqual(values, [expected]);
  }
  // A retry can reuse the successful pack job from an earlier attempt of this same run.
  const attempt = bom.metadata.properties.find(p => p.name === 'elsa:producer-attempt')?.value;
  assert.ok(/^[1-9][0-9]*$/.test(attempt) && Number(attempt) <= Number(process.env.GITHUB_RUN_ATTEMPT));
  if (mode === 'sign') {
    await attest({ subjects: [{ name: subject.name, digest: { sha256: subject.sha256 } }],
      predicateType: 'https://cyclonedx.org/bom', predicate: bom, sigstore: 'public-good', token: process.env.GH_TOKEN });
    console.log('Stored SBOM for ' + subject.name + ' sha256:' + subject.sha256);
  }
}
