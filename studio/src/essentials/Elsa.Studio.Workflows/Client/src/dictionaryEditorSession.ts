export type DictionaryEditorTab = "table" | "json";

export interface DictionaryEditorSessionState<Row = unknown> {
  baseline: string;
  rows?: Row[];
  jsonDraft?: string;
  complexDrafts?: Record<string, string>;
  activeTab: DictionaryEditorTab;
}

const sessions = new Map<string, DictionaryEditorSessionState>();

export function readDictionaryEditorSession<Row>(key: string): DictionaryEditorSessionState<Row> | null {
  return (sessions.get(key) as DictionaryEditorSessionState<Row> | undefined) ?? null;
}

export function writeDictionaryEditorSession<Row>(key: string, state: DictionaryEditorSessionState<Row>) {
  sessions.set(key, state as DictionaryEditorSessionState);
}

export function clearDictionaryEditorSession(key: string) {
  sessions.delete(key);
}

export function clearDictionaryEditorSessionScope(scope: string) {
  const prefix = `${scope}:`;
  for (const key of sessions.keys()) {
    if (key.startsWith(prefix)) sessions.delete(key);
  }
}

export function dictionaryValueSignature(value: unknown) {
  try {
    return JSON.stringify(value ?? {}) ?? "{}";
  } catch {
    return "[unserializable]";
  }
}
