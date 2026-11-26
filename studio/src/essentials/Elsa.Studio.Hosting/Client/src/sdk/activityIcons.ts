import type { ComponentType, SVGProps } from "react";

/** Props a registered extension activity icon should accept. Use `currentColor` so tone tokens apply. */
export type StudioActivityIconComponent = ComponentType<SVGProps<SVGSVGElement> & { size?: number }>;

const registeredActivityIcons = new Map<string, StudioActivityIconComponent>();
const activityIconListeners = new Set<() => void>();

function normalizeExtensionIconName(name: string): string | null {
  const trimmed = name.trim();
  if (!trimmed) return null;
  const bare = trimmed.startsWith("ext:") ? trimmed.slice(4) : trimmed;
  if (!/^[A-Za-z][A-Za-z0-9_-]*$/.test(bare)) return null;
  return bare.toLowerCase();
}

/**
 * Registers React icon components for catalog values of the form `ext:<name>`.
 * Names match case-insensitively. Call from an extension module's `register()`, or later —
 * subscribers re-render so a late registration appears without a remount. Passing `null`
 * unregisters that name.
 */
export function registerActivityIcons(icons: Record<string, StudioActivityIconComponent | null | undefined>): void {
  for (const [name, component] of Object.entries(icons)) {
    const key = normalizeExtensionIconName(name);
    if (!key) continue;
    if (component) registeredActivityIcons.set(key, component);
    else registeredActivityIcons.delete(key);
  }
  for (const listener of activityIconListeners) listener();
}

/** Looks up a component registered by `registerActivityIcons`. Accepts `name` or `ext:name`. */
export function getRegisteredActivityIcon(name: string): StudioActivityIconComponent | undefined {
  const key = normalizeExtensionIconName(name);
  return key ? registeredActivityIcons.get(key) : undefined;
}

/** Subscribe to `registerActivityIcons` so a mounted glyph can pick up a late registration. */
export function subscribeActivityIcons(listener: () => void): () => void {
  activityIconListeners.add(listener);
  return () => {
    activityIconListeners.delete(listener);
  };
}
