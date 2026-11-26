export function enabledMenuIndexes(flags: ReadonlyArray<{ disabled?: boolean } | boolean>): number[] {
  return flags
    .map((item, index) => {
      const disabled = typeof item === "boolean" ? item : Boolean(item.disabled);
      return disabled ? -1 : index;
    })
    .filter(index => index >= 0);
}

export function stepEnabledIndex(enabled: readonly number[], current: number, delta: number): number {
  if (enabled.length === 0) return current;
  const position = enabled.indexOf(current);
  const from = position >= 0 ? position : delta > 0 ? -1 : 0;
  return enabled[(from + delta + enabled.length * 2) % enabled.length];
}

export function focusIndexedItem(idPrefix: string, index: number) {
  document.getElementById(`${idPrefix}-item-${index}`)?.focus();
}
