import test from 'node:test';
import assert from 'node:assert/strict';
import { chart } from './selection.mjs';

const root = { id: '$', kind: 'root', initial: 'Review' };
const compound = (id, parent, initial) => ({ id, parent, kind: 'compound', initial });
const atomic = (id, parent) => ({ id, parent, kind: 'atomic' });
const transition = (id, source, target, order, extra = {}) =>
  ({ id, source, target, order, trigger: 'event', mode: 'reenter', ...extra });
const visit = id => ({ state: id, visit: `${id}/original` });
const event = { kind: 'event' };
const ids = steps => steps.map(s => s.state).sort();

const serialNodes = [root, compound('Review', '$', 'Checking'), atomic('Checking', 'Review'),
  atomic('Changes', 'Review'), atomic('Withdrawn', '$')];
const serialVisits = ['Review', 'Checking'].map(visit);
const serial = transitions => chart({ nodes: serialNodes, transitions });

const parallelNodes = [root, { id: 'Review', parent: '$', kind: 'parallel' },
  { id: 'E', parent: 'Review', kind: 'region', initial: 'Editing' },
  { id: 'L', parent: 'Review', kind: 'region', initial: 'Legal' },
  atomic('Editing', 'E'), atomic('Edited', 'E'), atomic('Legal', 'L'), atomic('Cleared', 'L'),
  atomic('Withdrawn', '$')];
const parallelVisits = ['Review', 'Editing', 'Legal'].map(visit);
const parallel = transitions => chart({ nodes: parallelNodes, transitions });

test('eligible child suppresses parent before a faulting parent guard', () => {
  const model = serial([
    transition('parent', 'Review', 'Withdrawn', 0, { guard: () => { throw Error('must not run'); } }),
    transition('child', 'Checking', 'Changes', 1)
  ]);
  assert.deepEqual(model.select(serialVisits, event).selected, ['child']);
});

test('false child uses parent fallback; considered guard failures propagate', () => {
  const transitions = [transition('child', 'Checking', 'Changes', 0, { guard: () => false }),
    transition('parent', 'Review', 'Withdrawn', 1)];
  const result = serial(transitions).select(serialVisits, event);
  assert.deepEqual(result.selected, ['parent']);
  assert.deepEqual(ids(result.exits), ['Checking', 'Review']);
  assert.deepEqual(result.exits.find(s => s.state === 'Review').after, ['exit:Checking']);
  transitions[0].guard = () => { throw Error('considered guard'); };
  assert.throws(() => serial(transitions).select(serialVisits, event), /considered guard/);
});

test('first eligible source rule wins by explicit order independent of array order', () => {
  const result = serial([
    transition('later', 'Checking', 'Withdrawn', 8),
    transition('false', 'Checking', 'Changes', 1, { guard: () => false }),
    transition('chosen', 'Checking', 'Changes', 3)
  ]).select(serialVisits, event);
  assert.deepEqual(result.selected, ['chosen']);
});

test('shared ancestor is considered once across two active regions', () => {
  let calls = 0;
  const result = parallel([
    transition('parent', 'Review', 'Withdrawn', 0, { guard: () => { calls++; return false; } })
  ]).select(parallelVisits, event);
  assert.equal(calls, 1);
  assert.deepEqual(result.selected, []);
});

test('compatible regions select together and preserve all phase barriers', () => {
  const result = parallel([
    transition('edit', 'Editing', 'Edited', 1), transition('legal', 'Legal', 'Cleared', 2)
  ]).select(parallelVisits, event);
  assert.deepEqual(result.selected, ['edit', 'legal']);
  assert.deepEqual(ids(result.exits), ['Editing', 'Legal']);
  for (const action of result.actions) assert.deepEqual(action.after, ['exit:Editing', 'exit:Legal']);
  for (const entry of result.entries) assert.deepEqual(entry.after, ['action:edit', 'action:legal']);
  assert.ok(result.exits.every(s => s.after.length === 0));
  assert.ok(result.actions.every(s => !s.after.some(id => id.startsWith('action:'))));
  assert.ok(result.entries.every(s => !s.after.some(id => id.startsWith('entry:'))));
  assert.deepEqual(result.actions.map(a => a.sourceVisit), ['Editing/original', 'Legal/original']);
});

test('unrelated whole-container exit gets no implicit priority', () => {
  for (const wholeFirst of [false, true]) {
    const result = parallel([
      transition('edit', 'Editing', 'Edited', wholeFirst ? 2 : 1),
      transition('withdraw', 'Legal', 'Withdrawn', wholeFirst ? 1 : 2)
    ]).select(parallelVisits, event);
    assert.deepEqual(result.selected, [wholeFirst ? 'withdraw' : 'edit']);
  }
});

test('internal action conflicts with a candidate exiting its source visit', () => {
  const result = parallel([
    transition('comment', 'Editing', undefined, 1, { mode: 'internal' }),
    transition('withdraw', 'Legal', 'Withdrawn', 2)
  ]).select(parallelVisits, event);
  assert.deepEqual(result.selected, ['comment']);
  assert.deepEqual(result.exits, []);
  assert.deepEqual(result.entries, []);
  assert.equal(result.actions[0].sourceVisit, 'Editing/original');
});

test('loser does not revive a later same-source rule or suppressed ancestor', () => {
  const result = parallel([
    transition('withdraw', 'Legal', 'Withdrawn', 1),
    transition('edit', 'Editing', 'Edited', 2),
    transition('later', 'Editing', undefined, 3, { mode: 'internal' }),
    transition('fallback', 'Review', 'Withdrawn', 4)
  ]).select(parallelVisits, event);
  assert.deepEqual(result.candidates, ['withdraw', 'edit']);
  assert.deepEqual(result.selected, ['withdraw']);
});

test('compound preservation exits descendants but preserves parent visit', () => {
  const result = serial([transition('revise', 'Review', 'Changes', 1, { mode: 'preserve' })])
    .select(serialVisits, event);
  assert.deepEqual(ids(result.exits), ['Checking']);
  assert.deepEqual(ids(result.entries), ['Changes']);
  assert.equal(result.actions[0].sourceVisit, 'Review/original');
});

test('compound re-entry exits old parent visit and enters explicit target', () => {
  const result = serial([transition('restart', 'Review', 'Changes', 1)]).select(serialVisits, event);
  assert.deepEqual(ids(result.exits), ['Checking', 'Review']);
  assert.deepEqual(ids(result.entries), ['Changes', 'Review']);
  assert.equal(result.exits.find(s => s.state === 'Review').visit, 'Review/original');
  assert.equal(result.entries.find(s => s.state === 'Review').visit, undefined);
  assert.deepEqual(result.entries.find(s => s.state === 'Changes').after, ['entry:Review']);
});

test('self re-entry expands initial children rather than restoring history', () => {
  const result = serial([transition('restart', 'Review', 'Review', 1)])
    .select(['Review', 'Changes'].map(visit), event);
  assert.deepEqual(ids(result.exits), ['Changes', 'Review']);
  assert.deepEqual(ids(result.entries), ['Checking', 'Review']);
});

test('leaf self re-entry replaces only that visit', () => {
  const result = serial([transition('retry', 'Checking', 'Checking', 1)]).select(serialVisits, event);
  assert.deepEqual(ids(result.exits), ['Checking']);
  assert.deepEqual(ids(result.entries), ['Checking']);
});

test('independent internal reactions preserve every visit without serializing actions', () => {
  const result = parallel([
    transition('comment', 'Editing', undefined, 1, { mode: 'internal' }),
    transition('note', 'Legal', undefined, 2, { mode: 'internal' })
  ]).select(parallelVisits, event);
  assert.deepEqual(result.selected, ['comment', 'note']);
  assert.deepEqual(result.exits, []);
  assert.deepEqual(result.entries, []);
  assert.ok(result.actions.every(s => s.after.length === 0));
});

test('parallel-parent descendant re-entry resets every region', () => {
  const result = parallel([transition('reset', 'Review', 'Edited', 1)]).select(parallelVisits, event);
  assert.deepEqual(ids(result.exits), ['Editing', 'Legal', 'Review']);
  assert.deepEqual(ids(result.entries), ['Edited', 'Legal', 'Review']);
  assert.deepEqual(result.exits.find(s => s.state === 'Review').after, ['exit:Editing', 'exit:Legal']);
  assert.deepEqual(result.entries.find(s => s.state === 'Edited').after, ['entry:Review']);
  assert.deepEqual(result.entries.find(s => s.state === 'Legal').after, ['entry:Review']);
});

test('nearest common compound stays active when nested siblings change', () => {
  const nodes = [root, compound('Review', '$', 'Inner'), compound('Inner', 'Review', 'A'),
    atomic('A', 'Inner'), atomic('B', 'Inner')];
  const result = chart({ nodes, transitions: [transition('move', 'A', 'B', 1)] })
    .select(['Review', 'Inner', 'A'].map(visit), event);
  assert.deepEqual(ids(result.exits), ['A']);
  assert.deepEqual(ids(result.entries), ['B']);
});

test('entering a nested parallel target expands explicit and initial paths', () => {
  const nodes = parallelNodes.map(n => n.id === '$' ? { ...n, initial: 'Withdrawn' } : n);
  const result = chart({ nodes, transitions: [transition('submit', 'Withdrawn', 'Edited', 1)] })
    .select([visit('Withdrawn')], event);
  assert.deepEqual(ids(result.entries), ['Edited', 'Legal', 'Review']);
  assert.deepEqual(result.entries.find(s => s.state === 'Review').after, ['action:submit']);
});

test('chart and visit enumeration order cannot alter selection or phase plan', () => {
  const transitions = [transition('legal', 'Legal', 'Cleared', 2), transition('edit', 'Editing', 'Edited', 1)];
  const expected = parallel(transitions).select(parallelVisits, event);
  const reversed = chart({ nodes: [...parallelNodes].reverse(), transitions: [...transitions].reverse() });
  assert.deepEqual(reversed.select([...parallelVisits].reverse(), event), expected);
});

test('exact restored visits produce identical plans; replacement visits remain distinguishable', () => {
  const model = serial([transition('move', 'Checking', 'Changes', 1)]);
  const original = model.select(serialVisits, event);
  assert.deepEqual(model.select(JSON.parse(JSON.stringify(serialVisits)), event), original);
  const replaced = serialVisits.map(v => ({ ...v, visit: v.visit + '/replacement' }));
  assert.notEqual(model.select(replaced, event).actions[0].sourceVisit, original.actions[0].sourceVisit);
});

test('ordinary guard receives exact source visit and values, never promises durable evaluation', () => {
  const model = serial([transition('move', 'Checking', 'Changes', 1, { guard: context =>
    context.visit.visit === 'Checking/original' && context.values.allowed && context.input.code === 3 })]);
  assert.deepEqual(model.select(serialVisits, { ...event, code: 3 }, { allowed: true }).selected, ['move']);
  assert.deepEqual(model.select(serialVisits, { ...event, code: 2 }, { allowed: true }).selected, []);
});

test('trigger categories are distinct and unhandled input selects no work', () => {
  const model = serial([transition('move', 'Checking', 'Changes', 1)]);
  const result = model.select(serialVisits, { kind: 'completion' });
  assert.deepEqual(result, { candidates: [], selected: [], exits: [], actions: [], entries: [] });
});

test('invalid authoring shapes are rejected explicitly', () => {
  assert.throws(() => parallel([transition('cross', 'Editing', 'Cleared', 1)]), /cross-region/);
  assert.throws(() => parallel([transition('bad', 'Review', 'Edited', 1, { mode: 'preserve' })]), /Parallel/);
  assert.throws(() => serial([transition('ancestor', 'Checking', 'Review', 1)]), /Ancestor target unresolved/);
  assert.throws(() => serial([transition('self', 'Review', 'Review', 1, { mode: 'preserve' })]), /Self/);
  assert.throws(() => serial([transition('bad', 'Checking', 'Changes', 1, { mode: 'internal' })]), /no target/);
  assert.throws(() => serial([transition('bad', 'Checking', 'Changes', 1, { mode: 'preserve' })]), /Preserve/);
  assert.throws(() => serial([transition('a', 'Checking', 'Changes', 1),
    transition('b', 'Checking', 'Changes', 1)]), /Unique/);
  assert.throws(() => serial([transition('a', 'Checking', 'Changes', 1),
    transition('a', 'Checking', 'Changes', 2)]), /Unique/);
});

test('invalid topology is refused before an input is evaluated', () => {
  for (const nodes of [
    serialNodes.map(n => n.id === 'Review' ? { ...n, initial: 'missing' } : n),
    serialNodes.map(n => n.id === 'Checking' ? { ...n, parent: 'missing' } : n),
    serialNodes.map(n => n.id === 'Review' ? { ...n, parent: 'Checking' } : n),
    [...serialNodes, atomic('Checking', 'Review')],
    serialNodes.map(n => n.id === 'Checking' ? { ...n, kind: 'region' } : n)
  ]) assert.throws(() => chart({ nodes, transitions: [] }));
});

test('impossible active configurations are refused', () => {
  const model = serial([]);
  for (const visits of [[], [visit('Checking')], [visit('Review')],
    ['Review', 'Checking', 'Changes'].map(visit), ['Review', 'Checking', 'Withdrawn'].map(visit),
    [visit('Review'), visit('Checking'), visit('Checking')],
    [{ state: 'Review', visit: 'same' }, { state: 'Checking', visit: 'same' }],
    [{ state: 'Review', visit: 'parent' }, { state: 'Checking', visit: '' }]
  ]) assert.throws(() => model.select(visits, event));
  assert.throws(() => parallel([]).select(['Review', 'Editing'].map(visit), event));
});

test('asynchronous or nonboolean guard results are rejected', () => {
  for (const guard of [() => 1, () => Promise.resolve(true)]) {
    assert.throws(() => serial([transition('bad', 'Checking', 'Changes', 1, { guard })])
      .select(serialVisits, event), /boolean synchronously/);
  }
});

test('explicit final is a valid target and cannot author an outgoing reaction', () => {
  const nodes = serialNodes.map(n => n.id === 'Changes' ? { ...n, kind: 'final' } : n);
  const model = chart({ nodes, transitions: [transition('finish', 'Checking', 'Changes', 1)] });
  assert.deepEqual(ids(model.select(serialVisits, event).entries), ['Changes']);
  assert.throws(() => chart({ nodes, transitions: [transition('bad', 'Changes', 'Checking', 1)] }),
    /Invalid transition source/);
});
