import { invertedEffects } from "@codemirror/commands";
import { StateEffect, StateField } from "@codemirror/state";

export const setOwnerHistoryValue = StateEffect.define<unknown>();
export const ownerHistoryValue = StateField.define<unknown>({
  create: () => undefined,
  update(value, transaction) {
    for (const effect of transaction.effects) if (effect.is(setOwnerHistoryValue)) value = effect.value;
    return value;
  }
});
export const ownerHistoryInversion = invertedEffects.of(transaction =>
  transaction.effects.some(effect => effect.is(setOwnerHistoryValue))
    ? [setOwnerHistoryValue.of(transaction.startState.field(ownerHistoryValue))]
    : []);

/** JSON owner data is copied at the boundary; callers cannot mutate an earlier history position. */
export function snapshotOwnerValue(value: unknown): unknown {
  if (value === null || typeof value === "string" || typeof value === "boolean" ||
      typeof value === "number" && Number.isFinite(value) || value === undefined) return value;
  if (Array.isArray(value)) return Object.freeze(value.map(snapshotOwnerValue));
  if (value && typeof value === "object" && Object.getPrototypeOf(value) === Object.prototype) {
    return Object.freeze(Object.fromEntries(Object.entries(value).map(([key, item]) => [key, snapshotOwnerValue(item)])));
  }
  throw new Error("Editor history data must be JSON values.");
}

export function ownerValueKey(value: unknown): string {
  return JSON.stringify(value, (_key, item) => item && typeof item === "object" && !Array.isArray(item)
    ? Object.fromEntries(Object.entries(item).sort(([left], [right]) => left < right ? -1 : left > right ? 1 : 0)) : item) ?? "undefined";
}
