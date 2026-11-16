// Mutate isolated copies, then require the named semantic assertion to fail.
import { mkdtempSync, readFileSync, writeFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { spawnSync } from 'node:child_process';
import assert from 'node:assert/strict';

const source = readFileSync(new URL('./selection.mjs', import.meta.url), 'utf8');
const tests = readFileSync(new URL('./selection.test.mjs', import.meta.url), 'utf8');
const controls = [
  {
    name: 'hierarchy suppression',
    test: 'eligible child suppresses parent before a faulting parent guard',
    from: 'if (candidates.some(c => c.transition.source !== source && inside(c.transition.source, source))) continue;',
    to: '// negative control: evaluate ancestor guards even after an eligible child'
  },
  {
    name: 'source-visit conflict',
    test: 'internal action conflicts with a candidate exiting its source visit',
    from: 'c.exiting.includes(s.transition.source) || s.exiting.includes(c.transition.source)',
    to: 'false'
  },
  {
    name: 'exit/action barrier',
    test: 'compatible regions select together and preserve all phase barriers',
    from: 'sourceVisit: c.sourceVisit, after: exits.map(e => e.id)',
    to: 'sourceVisit: c.sourceVisit, after: []'
  }
];

for (const control of controls) {
  assert.equal(source.split(control.from).length, 2, 'Mutation must match exactly once');
  const directory = mkdtempSync(join(tmpdir(), 'elsa-selection-control-'));
  try {
    writeFileSync(join(directory, 'selection.mjs'), source.replace(control.from, control.to));
    writeFileSync(join(directory, 'selection.test.mjs'), tests);
    const result = spawnSync(process.execPath, ['--test', '--test-reporter=tap',
      `--test-name-pattern=^${control.test}$`, join(directory, 'selection.test.mjs')],
    { encoding: 'utf8', timeout: 10_000 });
    assert.ifError(result.error);
    assert.equal(result.status, 1, result.stdout + result.stderr);
    assert.ok(result.stdout.includes(`not ok 1 - ${control.test}`), result.stdout + result.stderr);
    assert.match(result.stdout, /# fail 1\b/);
    console.log(`Detected: ${control.name}; named semantic test failed as expected`);
  } finally {
    rmSync(directory, { recursive: true, force: true });
  }
}
