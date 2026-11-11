/**
 * Carries the decision created from an Evaluate Decision picker's New decision button back to that picker.
 *
 * The picker opens `/decisions?new=1&handoff=<token>` in another workspace tab; the editor there completes
 * the token after its first save. The workflow's tab is hidden meanwhile and its effects are torn down, so
 * the result is stored here and taken when the picker's effects run again. Workspace tabs share this
 * document, so a module-level store reaches the picker; a browser-tab fallback runs in another document and
 * gets no token. Only the picker that began a token can take its result, so other inspectors never react.
 */
export interface NewDecisionResult {
  id: string;
  name: string;
}

export const newDecisionHandoffParameter = "handoff";
const capacity = 20;
const entries = new Map<string, NewDecisionResult | null>();
const listeners = new Set<() => void>();

function token() {
  return typeof crypto !== "undefined" && typeof crypto.randomUUID === "function" ? crypto.randomUUID() : `${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}`;
}

/** Starts a handoff and returns its token. The oldest unfinished handoffs are forgotten past a small cap. */
export function beginNewDecisionHandoff() {
  const value = token();
  entries.set(value, null);
  while (entries.size > capacity) entries.delete(entries.keys().next().value!);
  return value;
}

/** Drops a handoff, finished or not, so a later save cannot reach the picker that began it. */
export function cancelNewDecisionHandoff(value: string) {
  entries.delete(value);
}

/** Records the decision a handoff's editor created. Unknown or already completed tokens are ignored. */
export function completeNewDecisionHandoff(value: string, result: NewDecisionResult) {
  if (!entries.has(value) || entries.get(value)) return;
  entries.set(value, result);
  listeners.forEach(listener => listener());
}

/** Returns a completed handoff's decision once, removing it; null while the editor has not saved. */
export function takeNewDecisionHandoff(value: string) {
  const result = entries.get(value);
  if (!result) return null;
  entries.delete(value);
  return result;
}

/** Notifies when any handoff completes, for a picker that is visible at that moment. */
export function subscribeNewDecisionHandoffs(listener: () => void) {
  listeners.add(listener);
  return () => { listeners.delete(listener); };
}
