export function readObject(draft: string): Record<string, unknown> | null {
  try {
    const value: unknown = JSON.parse(draft || "{}");
    return isRecord(value) ? value : null;
  } catch { return null; }
}

export function isStringMap(value: unknown, itemKind: "string" | "string[]") {
  if (!isRecord(value)) return false;
  return Object.values(value).every(item => itemKind === "string"
    ? typeof item === "string"
    : Array.isArray(item) && item.every(entry => typeof entry === "string"));
}

export function isJsonValue(value: unknown): boolean {
  if (value === null || ["string", "number", "boolean"].includes(typeof value)) return true;
  if (Array.isArray(value)) return value.every(isJsonValue);
  return isRecord(value) && Object.values(value).every(isJsonValue);
}

export function isRecord(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === "object" && !Array.isArray(value);
}

export function asRecord(value: unknown) {
  return isRecord(value) ? value : null;
}
