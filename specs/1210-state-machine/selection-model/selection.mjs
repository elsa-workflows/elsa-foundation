// Executable design model only. No product project imports this module.
const containers = new Set(['root', 'compound', 'parallel', 'region']);
const kinds = new Set([...containers, 'atomic', 'final']);
const state = node => node.kind !== 'root' && node.kind !== 'region';
const compare = (a, b) => a < b ? -1 : a > b ? 1 : 0;
const fail = message => { throw new Error(message); };

export function chart(definition) {
  const nodes = new Map();
  for (const node of definition.nodes) {
    if (typeof node.id !== 'string' || !node.id || nodes.has(node.id) || !kinds.has(node.kind))
      fail('Invalid node identity/kind');
    nodes.set(node.id, Object.freeze({ ...node }));
  }
  const roots = [...nodes.values()].filter(n => n.kind === 'root');
  if (roots.length !== 1 || roots[0].parent != null) fail('One root required');
  const root = roots[0].id;
  const children = id => [...nodes.values()].filter(n => n.parent === id).map(n => n.id).sort(compare);
  const path = id => {
    const result = [];
    while (id != null) {
      if (!nodes.has(id) || result.includes(id)) fail('Missing parent or containment cycle');
      result.push(id);
      id = nodes.get(id).parent;
    }
    if (result.at(-1) !== root) fail('Node not rooted');
    return result;
  };
  const inside = (id, ancestor) => path(id).includes(ancestor);
  const branch = (id, ancestor) => path(id)[path(id).indexOf(ancestor) - 1];
  for (const node of nodes.values()) {
    path(node.id);
    const owned = children(node.id);
    if (node.id !== root) {
      const parent = nodes.get(node.parent);
      if (!containers.has(parent.kind)) fail('Leaf cannot own children');
      if ((node.kind === 'region') !== (parent.kind === 'parallel')) fail('Parallel owns only regions');
    }
    if (containers.has(node.kind)) {
      if (!owned.length) fail('Container needs children');
      if (node.kind !== 'parallel' && !owned.includes(node.initial)) fail('Explicit initial child required');
    } else if (owned.length) fail('Leaf cannot own children');
  }
  const transitionIds = new Set();
  const orders = new Set();
  const transitions = definition.transitions.map(t => {
    if (typeof t.id !== 'string' || !t.id || transitionIds.has(t.id) ||
        !Number.isSafeInteger(t.order) || orders.has(t.order))
      fail('Unique transition identity and explicit order required');
    transitionIds.add(t.id);
    orders.add(t.order);
    const source = nodes.get(t.source);
    if (!source || !state(source) || source.kind === 'final') fail('Invalid transition source');
    if (typeof t.trigger !== 'string' || !t.trigger.length) fail('Explicit trigger category required');
    if (t.guard != null && typeof t.guard !== 'function') fail('Guard must be synchronous function');
    if (t.mode === 'internal') {
      if (t.target != null) fail('Internal reaction has no target');
    } else {
      const target = nodes.get(t.target);
      if (!target || !state(target)) fail('Invalid target');
      if (t.mode !== 'preserve' && t.mode !== 'reenter') fail('Explicit visit mode required');
      if (t.source === t.target && t.mode !== 'reenter') fail('Self target requires re-entry');
      if (t.source !== t.target && inside(t.source, t.target)) fail('Ancestor target unresolved in model');
      if (inside(t.target, t.source) && t.source !== t.target) {
        if (source.kind === 'parallel' && t.mode !== 'reenter') fail('Parallel descendant requires re-entry');
      } else if (t.mode === 'preserve') fail('Preserve mode requires compound descendant target');
      for (const node of nodes.values()) {
        if (node.kind === 'parallel' && t.source !== node.id && inside(t.source, node.id) &&
            inside(t.target, node.id) && branch(t.source, node.id) !== branch(t.target, node.id))
          fail('Direct cross-region edge');
      }
    }
    return Object.freeze({ ...t });
  }).sort((a, b) => a.order - b.order);

  const initial = (boundary = root, target) => {
    const result = [];
    const enter = id => {
      const node = nodes.get(id);
      if (state(node)) result.push(id);
      if (!containers.has(node.kind)) return;
      if (node.kind === 'parallel') {
        children(id).forEach(enter);
      } else {
        const selected = target && target !== id && inside(target, id) ? branch(target, id) : node.initial;
        enter(selected);
      }
    };
    // Boundary itself is retained; entry starts below it.
    const node = nodes.get(boundary);
    if (node.kind === 'parallel') children(boundary).forEach(enter);
    else enter(target && inside(target, boundary) ? branch(target, boundary) : node.initial);
    return result;
  };

  function configuration(visits) {
    const active = new Map();
    const occurrences = new Set();
    for (const visit of visits) {
      if (!nodes.has(visit.state) || !state(nodes.get(visit.state)) || active.has(visit.state) ||
          typeof visit.visit !== 'string' || !visit.visit || occurrences.has(visit.visit))
        fail('Invalid active visit');
      occurrences.add(visit.visit);
      active.set(visit.state, Object.freeze({ ...visit }));
    }
    const reached = new Set();
    const walk = id => {
      const node = nodes.get(id);
      if (state(node)) {
        if (!active.has(id)) fail('Missing active ancestor/region state');
        reached.add(id);
      }
      if (!containers.has(node.kind)) return;
      if (node.kind === 'parallel') children(id).forEach(walk);
      else {
        const present = children(id).filter(child => active.has(child));
        if (present.length !== 1) fail('Exactly one active child required');
        walk(present[0]);
      }
    };
    walk(root);
    if (reached.size !== active.size) fail('Inactive branch contains visits');
    return active;
  }

  function boundary(t) {
    if (t.mode === 'internal') return null;
    if (t.source === t.target || (inside(t.target, t.source) && t.mode === 'reenter'))
      return nodes.get(t.source).parent;
    if (inside(t.target, t.source)) return t.source;
    return path(t.source).find(id => inside(t.target, id));
  }

  function select(visits, input, values = {}) {
    const active = configuration(visits);
    const candidates = [];
    const sources = [...active.keys()].sort((a, b) => path(b).length - path(a).length || compare(a, b));
    for (const source of sources) {
      // Hierarchy precedence is checked BEFORE ancestor guards are evaluated.
      if (candidates.some(c => c.transition.source !== source && inside(c.transition.source, source))) continue;
      const t = transitions.find(t => {
        if (t.source !== source || t.trigger !== input.kind) return false;
        const result = t.guard ? t.guard({ input, values, visit: active.get(source) }) : true;
        if (typeof result !== 'boolean') fail('Guard must return boolean synchronously');
        return result;
      });
      if (!t) continue;
      const retained = boundary(t);
      let exiting = [];
      let entering = [];
      if (retained != null) {
        const replaced = branch(t.source, retained);
        exiting = [...active.keys()].filter(id => retained === t.source
          ? id !== retained && inside(id, retained) : inside(id, replaced));
        entering = initial(retained, t.target);
      }
      candidates.push({ transition: t, sourceVisit: active.get(source).visit, exiting, entering });
    }
    const selected = [];
    for (const c of candidates.sort((a, b) => a.transition.order - b.transition.order)) {
      const conflicts = selected.some(s => c.exiting.some(id => s.exiting.includes(id)) ||
        c.exiting.includes(s.transition.source) || s.exiting.includes(c.transition.source));
      if (!conflicts) selected.push(c);
    }
    const exiting = [...new Set(selected.flatMap(c => c.exiting))];
    const entering = [...new Set(selected.flatMap(c => c.entering))];
    const exits = exiting.sort(compare).map(id => ({
      id: `exit:${id}`, state: id, visit: active.get(id).visit,
      // Region containers have no work; link the nearest exited state descendants across them.
      after: exiting.filter(child => child !== id &&
        path(child).slice(1).find(parent => exiting.includes(parent)) === id)
        .map(child => `exit:${child}`).sort(compare)
    }));
    const actions = selected.map(c => ({ id: `action:${c.transition.id}`, transition: c.transition.id,
      sourceVisit: c.sourceVisit, after: exits.map(e => e.id) }));
    const entries = entering.sort(compare).map(id => {
      const parent = path(id).slice(1).find(ancestor => entering.includes(ancestor));
      return { id: `entry:${id}`, state: id, after: parent ? [`entry:${parent}`] : actions.map(a => a.id) };
    });
    return {
      selected: selected.map(c => c.transition.id),
      candidates: candidates.map(c => c.transition.id),
      exits, actions, entries
    };
  }
  return Object.freeze({ initial, select });
}
