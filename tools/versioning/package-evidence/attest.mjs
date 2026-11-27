// Use the same official signing and storage client as actions/attest. Each package
// needs its own predicate; a single shared SBOM across all packages would be false.
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { appendFileSync, readdirSync, readFileSync } from 'node:fs';
import { basename, join } from 'node:path';
import { attest, attestProvenance } from '@actions/attest';
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
const pending = [];
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
  assert.equal(bom.bomFormat, 'CycloneDX');
  assert.equal(bom.specVersion, '1.6');
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
    const url = new URL('https://api.github.com/repos/elsa-workflows/elsa-foundation/attestations/sha256:' + subject.sha256);
    url.searchParams.set('predicate_type', 'https://cyclonedx.org/bom');
    url.searchParams.set('per_page', '1');
    const response = await fetch(url, { headers: { Authorization: 'Bearer ' + process.env.GH_TOKEN }, signal: AbortSignal.timeout(30_000) });
    if (response.status !== 404) {
      assert.ok(response.ok, 'Prior attestation lookup failed: ' + response.status);
      if ((await response.json()).attestations.length) {
        // Signatures exist: verify their complete original pair before reusing them.
        execFileSync('python3', [fileURLToPath(new URL('./verify.py', import.meta.url)), join(plan, 'nuget', subject.name)], { stdio: 'inherit', timeout: 130_000 });
        continue;
      }
    }
    assert.equal(attempt, process.env.GITHUB_RUN_ATTEMPT,
      'Signing a new digest requires the original build attempt. Re-run all jobs to rebuild and sign in the same attempt.');
    pending.push({ subject: { name: subject.name, digest: { sha256: subject.sha256 } }, bom });
  }
}

if (pending.length) {
  const signing = { sigstore: 'public-good', token: process.env.GH_TOKEN };
  // Write provenance first. An interrupted upload can leave a provenance-only record, never a new SBOM without its producer.
  await attestProvenance({ ...signing, subjects: pending.map(record => record.subject) });
  for (const record of pending) {
    await attest({ ...signing, subjects: [record.subject], predicateType: 'https://cyclonedx.org/bom', predicate: record.bom });
    console.log('Stored SBOM for ' + record.subject.name + ' sha256:' + record.subject.digest.sha256);
  }
}

if (mode === 'sign') {
  appendFileSync(process.env.GITHUB_OUTPUT, 'subjects_digest=' + digest(join(evidence, 'subjects.json')) + '\n');
}
