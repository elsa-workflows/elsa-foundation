/**
 * True when a presentation label or summary is an identity token (GUID, long hex, or a
 * version+build hash such as `1.0.0+e26c11bd…` / `0+e26c11bd…`) rather than a human name.
 * Kept off the eager Definitions landing closure; sequence cards and run labels import it.
 */
export function isTechnicalActivityLabel(value: string | null | undefined) {
  const text = value?.trim().replace(/^"(.*)"$/, "$1").trim() ?? "";
  return text.length > 0 && technicalActivityLabelPattern.test(text);
}

const technicalActivityLabelPattern =
  /^(?:\{?[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}\}?|[0-9a-f]{16,}|(?:\d+\.)*\d+\+[0-9a-f]{12,})$/i;
