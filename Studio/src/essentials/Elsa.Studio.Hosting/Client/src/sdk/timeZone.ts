import { useSyncExternalStore } from "react";

export const studioTimeZoneStorageKey = "elsa-studio-time-zone";
export const studioTimeZoneChangedEvent = "elsa-studio:time-zone-changed";
// Hosting and externally loaded SDK modules share one session fallback when storage is blocked.
const session = ((globalThis as typeof globalThis & { __elsaStudioTimeZone?: { transient?: string } }).__elsaStudioTimeZone ??= {});

export function isStudioTimeZone(value: unknown): value is string {
  if (value === "browser") return true;
  if (typeof value !== "string" || value.trim() !== value || !value || /^[+-]/.test(value)) return false;
  try { new Intl.DateTimeFormat("en-US", { timeZone: value }); return true; } catch { return false; }
}

export function getStudioTimeZonePreference(): string {
  if (session.transient !== undefined) return session.transient;
  try {
    const stored = typeof window === "undefined" ? null : window.localStorage.getItem(studioTimeZoneStorageKey);
    return isStudioTimeZone(stored) ? stored : "browser";
  } catch { return "browser"; }
}

export function setStudioTimeZonePreference(value: string): void {
  if (!isStudioTimeZone(value)) throw new RangeError("Choose UTC, the browser's zone, or an IANA time zone.");
  session.transient = value;
  if (typeof window === "undefined") return;
  try {
    if (value === "browser") window.localStorage.removeItem(studioTimeZoneStorageKey);
    else window.localStorage.setItem(studioTimeZoneStorageKey, value);
    session.transient = undefined;
  } catch { /* Keep this session's choice when browser storage is unavailable. */ }
  window.dispatchEvent(new Event(studioTimeZoneChangedEvent));
}

export function resolveStudioTimeZone(preference = getStudioTimeZonePreference()): string {
  return preference === "browser" || !isStudioTimeZone(preference) ? Intl.DateTimeFormat().resolvedOptions().timeZone || "UTC" : preference;
}

function subscribe(onChange: () => void) {
  if (typeof window === "undefined") return () => undefined;
  window.addEventListener(studioTimeZoneChangedEvent, onChange);
  const storageChanged = (event: StorageEvent) => {
    if (event.key !== null && event.key !== studioTimeZoneStorageKey) return;
    try { if (event.storageArea && event.storageArea !== window.localStorage) return; } catch { return; }
    session.transient = undefined;
    onChange();
  };
  window.addEventListener("storage", storageChanged);
  return () => {
    window.removeEventListener(studioTimeZoneChangedEvent, onChange);
    window.removeEventListener("storage", storageChanged);
  };
}

/** A module can subscribe without depending on the Hosting implementation or remounting its editor. */
export function useStudioTimeZone() {
  return useSyncExternalStore(subscribe, getStudioTimeZonePreference, () => "browser");
}

function offsetMinutes(date: Date, timeZone: string): number {
  const name = new Intl.DateTimeFormat("en-US", { timeZone, timeZoneName: "longOffset", hour: "2-digit", hourCycle: "h23" })
    .formatToParts(date).find(part => part.type === "timeZoneName")?.value;
  if (name === "GMT" || name === "UTC") return 0;
  const match = /^(?:GMT|UTC)([+-])(\d{2}):(\d{2})(?::(\d{2}))?$/.exec(name ?? "");
  if (!match) throw new RangeError("This date uses an offset that cannot be represented in whole minutes.");
  return (match[1] === "-" ? -1 : 1) * (Number(match[2]) * 60 + Number(match[3]) + Number(match[4] ?? 0) / 60);
}

function offsetText(minutes: number): string {
  const absolute = Math.abs(minutes);
  const seconds = Math.round(absolute * 60);
  return `${minutes < 0 ? "-" : "+"}${String(Math.floor(seconds / 3600)).padStart(2, "0")}:${String(Math.floor(seconds / 60) % 60).padStart(2, "0")}${seconds % 60 ? `:${String(seconds % 60).padStart(2, "0")}` : ""}`;
}

export function formatStudioDate(value: string | number | Date | null | undefined, preference = getStudioTimeZonePreference()): string {
  if (value === undefined || value === null || value === "") return "";
  const date = value instanceof Date ? value : new Date(value);
  if (!Number.isFinite(date.getTime())) return String(value);
  const zone = resolveStudioTimeZone(preference);
  return `${new Intl.DateTimeFormat(undefined, { timeZone: zone, dateStyle: "medium", timeStyle: "medium" }).format(date)} UTC${offsetText(offsetMinutes(date, zone))}`;
}

function wallParts(date: Date, timeZone: string): number[] {
  const parts = new Intl.DateTimeFormat("en-US", { timeZone, year: "numeric", month: "2-digit", day: "2-digit", hour: "2-digit", minute: "2-digit", second: "2-digit", hourCycle: "h23" }).formatToParts(date);
  return ["year", "month", "day", "hour", "minute", "second"].map(type => Number(parts.find(part => part.type === type)?.value));
}

/** Converts a wall time using that date's zone offset. A DST gap is refused; a repeated hour uses its earlier instant. */
export function localDateTimeToIso(value: string, preference = getStudioTimeZonePreference(), originalInstant?: string): string {
  const explicit = /(?:Z|[+-]\d{2}:\d{2})$/.test(value);
  const wall = explicit ? value.replace(/(?:Z|[+-]\d{2}:\d{2})$/, "") : value;
  const match = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})(?::(\d{2})(?:\.(\d{1,7}))?)?$/.exec(wall);
  if (!match) throw new RangeError("Enter a valid date and time.");
  const fields = match.slice(1, 7).map(part => Number(part ?? 0));
  const milliseconds = Number((match[7] ?? "").slice(0, 3).padEnd(3, "0"));
  const [year, month, day, hour, minute, second] = fields;
  const candidate = new Date(0);
  candidate.setUTCFullYear(year, month - 1, day);
  candidate.setUTCHours(hour, minute, second, milliseconds);
  if (year < 1 || [candidate.getUTCFullYear(), candidate.getUTCMonth() + 1, candidate.getUTCDate(), candidate.getUTCHours(), candidate.getUTCMinutes(), candidate.getUTCSeconds()].some((part, index) => part !== fields[index]))
    throw new RangeError("Enter a valid calendar date and time.");
  if (explicit) {
    const offset = /([+-])(\d{2}):(\d{2})$/.exec(value);
    if (offset && (Number(offset[2]) > 14 || Number(offset[3]) > 59 || Number(offset[2]) === 14 && Number(offset[3]) !== 0) || (!Number.isFinite(Date.parse(value)) || new Date(value).getUTCFullYear() < 1 || new Date(value).getUTCFullYear() > 9999))
      throw new RangeError("Enter a valid date and offset between -14:00 and +14:00.");
    return value;
  }
  if (originalInstant && studioLocalDateTime(originalInstant, preference) === value) {
    localDateTimeToIso(originalInstant, preference); // Validate the original explicit instant, including .NET range.
    const offset = offsetMinutes(new Date(originalInstant), resolveStudioTimeZone(preference));
    if (!Number.isInteger(offset)) throw new RangeError("This historical zone offset cannot be sent as a .NET date.");
    return `${wall}${offsetText(offset)}`;
  }
  const zone = resolveStudioTimeZone(preference);
  const wallUtc = candidate.getTime();
  const offsets = new Set<number>();
  for (let hours = -36; hours <= 36; hours += 3) offsets.add(offsetMinutes(new Date(wallUtc + hours * 3_600_000), zone));
  const instants = [...offsets].map(offset => ({ offset, date: new Date(wallUtc - offset * 60_000) }))
    .filter(item => wallParts(item.date, zone).every((part, index) => part === fields[index]))
    .sort((left, right) => left.date.getTime() - right.date.getTime());
  if (!instants.length) throw new RangeError(`That local time does not exist in ${zone} because the clock changes. Choose another time.`);
  if (instants[0].date.getUTCFullYear() < 1 || instants[0].date.getUTCFullYear() > 9999) throw new RangeError("The instant must fall between years 0001 and 9999 UTC.");
  if (!Number.isInteger(instants[0].offset)) throw new RangeError("This historical zone offset cannot be sent as a .NET date. Choose UTC or an explicit whole-minute offset.");
  return `${wall}${offsetText(instants[0].offset)}`;
}

/** Wall-time text for a datetime-local control. Pass the original instant on unchanged edits to preserve a repeated hour. */
export function studioLocalDateTime(value: string, preference = getStudioTimeZonePreference()): string {
  const date = new Date(value);
  if (!Number.isFinite(date.getTime())) return value;
  const [year, month, day, hour, minute, second] = wallParts(date, resolveStudioTimeZone(preference));
  const fraction = /\.(\d{1,7})(?:Z|[+-]\d{2}:\d{2})$/.exec(value)?.[1];
  return `${String(year).padStart(4, "0")}-${String(month).padStart(2, "0")}-${String(day).padStart(2, "0")}T${String(hour).padStart(2, "0")}:${String(minute).padStart(2, "0")}:${String(second).padStart(2, "0")}${fraction ? `.${fraction}` : ""}`;
}
