// Compare the common structural profile with an independently implemented design model.
// The proposed ancestor-target profile remains explicitly outside this reference model.
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { chart } from './selection.mjs';

if (process.argv.length !== 3) throw new Error('Pass the JSON file produced by Statecharts.Run --selection-export.');
const samples = JSON.parse(readFileSync(process.argv[2], 'utf8'));
const normalize = plan => ({
  selected: [...plan.selected].sort(), candidates: [...plan.candidates].sort(),
  exits: plan.exits.map(s => ({ ...s, after: [...s.after].sort() })).sort((a, b) => a.id.localeCompare(b.id)),
  actions: plan.actions.map(s => ({ ...s, after: [...s.after].sort() })).sort((a, b) => a.id.localeCompare(b.id)),
  entries: plan.entries.map(s => ({ ...s, after: [...s.after].sort() })).sort((a, b) => a.id.localeCompare(b.id))
});
let compared = 0;
const proposed = [];
for (const sample of samples) {
  if (sample.proposedAncestor) { proposed.push(sample.name); continue; }
  const transitions = sample.transitions.map(t => ({ ...t, guard: () => {
    const value = sample.guards?.[t.id];
    if (value === null) throw new Error(`Q2: suppressed/later guard evaluated: ${t.id}`);
    return value ?? true;
  } }));
  const actual = chart({ nodes: sample.nodes, transitions }).select(sample.visits, { kind: 'go' });
  assert.deepEqual(normalize(actual), normalize(sample.plan), sample.name);
  compared++;
}
assert.deepEqual(proposed.sort(), ['ancestor-compound', 'ancestor-parallel']);
assert.equal(compared, 28);
console.log(`${compared} common profiles matched the Node reference; ${proposed.length} proposed ancestor profiles explicitly excluded, not passed.`);
